using LogisticsEngine.AirportLogistics.Engine;
using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogisticsEngine
{
    public static class AirportRulesRegistry
    {
        public static void InitializeBaseRules(ReteEngine.ReteEngine engine)
        {
            engine.Begin("AssignGateOnLanding")
                // Left Fact: Find Landed flights that have a gate assigned
                .Where<Flight>("FL", f => !string.IsNullOrEmpty(f.AssignedGateId))

                // The Native Join: Match the "GT" Alpha Memory bucket using your new wrapper method
                .And<Gate>("GT", (token, gate) =>
                {
                    var flight = token.Get<Flight>("FL");
                    return flight.AssignedGateId == gate.GateId;
                })

                // Downstream Filter: When Landed, the gate must currently be free
                .Where<Flight>("FL", f => f.Status == "Landed")
                .And<Gate>("GT", (t, g) => !g.IsOccupied)

                // Action Consequence
                .CtxThen(t =>
                {
                    var flight = (Flight)t.Match.Get<Flight>("FL");
                    var gate = (Gate)t.Match.Get<Gate>("GT");

                    Console.WriteLine($"\n[RULE FIRE] Match Found!");
                    Console.WriteLine($"--> Flight {flight.FlightNumber} has landed. Locking down Gate {gate.GateId} ({gate.TerminalName}).");

                    // Mutate state in-place
                    gate.IsOccupied = true;

                    // Fire your custom Publish-Resume pipeline
                    t.Update(gate, null);
                    t.Publish();
                });
            Console.WriteLine("[ENGINE INIT] Rules successfully compiled into Rete network.");
        }

        public static void InitializeStep2Rules(ReteEngine.ReteEngine engine)
        {
            // =================================================================
            // STEP 2 RULE: Automatically Flag Late Arrivals (In-Place Telemetry Monitoring)
            // =================================================================
            // Watches flying inventory. If a flight's delay creeps past 30 minutes 
            // and it isn't already marked as delayed or landed, escalate its status.
            engine.Begin("EscalateLateArrivals")
                .Where<Flight>("FL", f => f.Status == "EnRoute" && f.MinutesDelayed > 30)
                .CtxThen(t =>
                {
                    var flight = (Flight)t.Match.Get<Flight>("FL");

                    Console.WriteLine($"\n[RULE TRIGGER] ALERT: Flight {flight.FlightNumber} has logged a severe operational delay!");
                    Console.WriteLine($"-> Current Delay: {flight.MinutesDelayed} minutes. Escallating status to 'Delayed'.");

                    // 1. Mutate the status flag in-place on our tracking model
                    flight.Status = "Delayed";

                    // 2. Queue the modification back into the engine using your update routine
                    t.Update(flight, null);

                    // 3. Fire your Publish-Resume pipeline to flash this batch across the network
                    // before the core agenda finishes its execution pass
                    t.Publish();
                });

            Console.WriteLine("[ENGINE INIT] Step 2 telemetry escalation rules successfully compiled.");
        }

        public static void InitializeStep3Rules(ReteEngine.ReteEngine engine)
        {
            Func<Flight, bool> sharedFlightBaseCheck = f => !string.IsNullOrEmpty(f.AssignedGateId);
            Func<Token, Flight, bool> sharedFlightLandedCheck = (t, f) => f.Status == "Landed";
            Func<Token, Gate, bool> sharedJoinCheck = (token, gate) => token.Get<Flight>("FL").AssignedGateId == gate.GateId;
            Func<Token, GroundCrew, bool> sharedCrewJoinTerminals = (token, crew) => crew.AssignedTerminal == token.Get<Gate>("GT").TerminalName;
            Func<Token, bool> sharedFlightLandedFilter = t => t.Get<Flight>("FL").Status == "Landed";
            // =================================================================
            // RULE 1: Unattended Landing Alert (Not Node)
            // Fires when a flight is Landed at a gate whose terminal has NO GroundCrew.
            // =================================================================
            engine.Begin("UnattendedLandingAlert")
                .First()
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .And<Gate>("GT", sharedJoinCheck)
                .Where(sharedFlightLandedFilter)
                .Not<GroundCrew>("GC", sharedCrewJoinTerminals)
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    var gate = t.Get<Gate>("GT");
                    //Console.WriteLine($"[TOKEN DUMP] Keys: {string.Join(", ", t.NamedFacts.Select(kv => $"{kv.Key}={kv.Value}"))} | FL.AssignedGateId={flight.AssignedGateId} | GT.GateId={gate.GateId}");
                    Console.WriteLine($"\n[⚠️ SECURITY ALERT] UNATTENDED LANDING!");
                    Console.WriteLine($"--> Safety Warning: Flight {flight.FlightNumber} touched down at {gate.GateId} ({gate.TerminalName}), but NO ground crew is registered at this terminal!");
                });

            // =================================================================
            // RULE 2: Gate Conflict Emergency Alert (Exists Node)
            // Priority 100: Runs BEFORE SuccessLanding so a second flight arriving
            // at an occupied/gated space is flagged as Conflict instead of docking.
            // =================================================================
            engine.Begin("GateConflictAlert")
                .Priority(100)
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .And<Gate>("GT", sharedJoinCheck)
                .Where(sharedFlightLandedFilter)
                .Exists<Flight>("ConflictingFlight", (token, otherFlight) =>
                    !ReferenceEquals(otherFlight, token.Get<Flight>("FL")) &&
                    otherFlight.AssignedGateId == token.Get<Gate>("GT").GateId &&
                    (otherFlight.Status == "Gated" || otherFlight.Status == "Landed"))
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    var gate = t.Get<Gate>("GT");
                    //Console.WriteLine($"[TOKEN DUMP] Keys: {string.Join(", ", t.NamedFacts.Select(kv => $"{kv.Key}={kv.Value}"))} | FL.AssignedGateId={flight.AssignedGateId} | GT.GateId={gate.GateId}");
                    Console.WriteLine($"\n[🚨 EMERGENCY RULE] GATE CONFLICT DETECTED!");
                    Console.WriteLine($"--> Danger: Flight {flight.FlightNumber} has landed at Gate {gate.GateId}, but another aircraft is occupying or assigned to that space!");
                    flight.Status = "Conflict";
                });

            // =================================================================
            // RULE 3: Flight Conflict Logger
            // Priority 70: Fires immediately after GateConflictAlert sets Status = "Conflict",
            // bound strictly to the conflicting flight itself (not Exists).
            // =================================================================
            engine.Begin("FlightConflict")
                .Priority(70)
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .And<Flight>("FL", (t, flight) => flight.Status == "Conflict")
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    Console.WriteLine($"[CONFLICT] Flight {flight.FlightNumber} has a conflict at Gate: {flight.AssignedGateId}");
                });

            // =================================================================
            // RULE 4: Success Landing
            // Priority 50: Docks a Landed flight only if its assigned gate is unoccupied
            // and ground crew is present at the terminal.
            // =================================================================
            engine.Begin("SuccessLanding")
                .Priority(50)
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .And<Gate>("GT", (token, gate) => token.Get<Flight>("FL").AssignedGateId == gate.GateId && !gate.IsOccupied)
                .Where(sharedFlightLandedFilter)
                .And<GroundCrew>("GC", sharedCrewJoinTerminals)
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    var gate = t.Get<Gate>("GT");
                    //Console.WriteLine($"[TOKEN DUMP] Keys: {string.Join(", ", t.NamedFacts.Select(kv => $"{kv.Key}={kv.Value}"))} | FL.AssignedGateId={flight.AssignedGateId} | GT.GateId={gate.GateId}");
                    flight.Status = "Gated";
                    gate.IsOccupied = true;
                    Console.WriteLine($"\n[SUCCESS] Successful operation: Flight {flight.FlightNumber} safely docked at {gate.GateId}. Landed...Let's Party!!");
                });

            Console.WriteLine("[ENGINE INIT] Step 3 Advanced Logic & Shared Beta-Memory Nodes compiled.");
        }

        public static void InitializeStep4Rules(ReteEngine.ReteEngine engine)
        {
            Func<Flight, bool> hasGate = f => !string.IsNullOrEmpty(f.AssignedGateId);
            Func<Token, Gate, bool> matchGate = (t, g) => t.Get<Flight>("FL").AssignedGateId == g.GateId;
            Func<Token, GroundCrew, bool> matchCrew = (t, c) => c.AssignedTerminal == t.Get<Gate>("GT").TerminalName;
            Func<Token, bool> isLanded = t => t.Get<Flight>("FL").Status == "Landed";

            // =================================================================
            // GROUP 1: "EmergencyOverride" (Dynamic Focus Interrupt)
            // =================================================================
            engine.Begin("EmergencyGateConflict")
                .AgendaGroup("EmergencyOverride")
                .AutoFocus()
                .Priority(100)
                .Where<Flight>("FL", hasGate)
                .And<Gate>("GT", matchGate)
                .Where(isLanded)
                .Exists<Flight>("Other", (t, other) =>
                    !ReferenceEquals(other, t.Get<Flight>("FL")) &&
                    other.AssignedGateId == t.Get<Gate>("GT").GateId &&
                    (other.Status == "Gated" || other.Status == "ClearedToDock"))
                .CtxThen(t =>
                {
                    var flight = t.Match.Get<Flight>("FL");
                    var gate = t.Match.Get<Gate>("GT");
                    var fSnap = (Flight)flight.Snapshot();
                    fSnap.Status = "Conflict";
                    t.Update(fSnap, null);
                    t.Publish();
                    Console.WriteLine($"[EmergencyOverride] CONFLICT: {flight.FlightNumber} blocked at occupied Gate {gate.GateId}. Diverting to holding pattern!");
                });

            // =================================================================
            // GROUP 2: "SafetyClearance" (Pre-Docking Validation)
            // Must complete before GateOperations can dock any aircraft.
            // =================================================================
            engine.Begin("VerifyGroundCrew")
                .AgendaGroup("SafetyClearance")
                .Priority(80)
                .Where<Flight>("FL", hasGate)
                .And<Gate>("GT", matchGate)
                .Where(isLanded)
                .And<GroundCrew>("GC", matchCrew)
                .Where(t => !t.Get<Gate>("GT").IsOccupied)
                .CtxThen(t =>
                {
                    var flight = t.Match.Get<Flight>("FL");
                    var crew = t.Match.Get<GroundCrew>("GC");
                    var fSnap = (Flight)flight.Snapshot();
                    fSnap.Status = "ClearedToDock";
                    Console.WriteLine($"[SafetyClearance] {fSnap.FlightNumber} cleared by {crew.CrewId} for Gate {fSnap.AssignedGateId}.");
                    t.Update(fSnap, null);
                    t.Publish();
                    // Dynamically shift focus to GateOperations to dock immediately
                    engine.FocusAgendaGroup("GateOperations").FireAll();
                });

            engine.Begin("HoldUnattendedFlight")
                .AgendaGroup("SafetyClearance")
                .Priority(70)
                .Where<Flight>("FL", hasGate)
                .And<Gate>("GT", matchGate)
                .Where(isLanded)
                .Not<GroundCrew>("GC", matchCrew)
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    var gate = t.Get<Gate>("GT");
                    Console.WriteLine($"[SafetyClearance] HOLD: {flight.FlightNumber} waiting at {gate.GateId} — no crew in {gate.TerminalName}.");
                });

            // =================================================================
            // GROUP 3: "GateOperations" (Physical Docking Execution)
            // =================================================================
            engine.Begin("ExecuteGateDocking")
                .AgendaGroup("GateOperations")
                .Priority(50)
                .Where<Flight>("FL", hasGate)
                .And<Gate>("GT", matchGate)
                .Where(t => t.Get<Flight>("FL").Status == "ClearedToDock" && !t.Get<Gate>("GT").IsOccupied)
                .CtxThen(t =>
                {
                    var flight = t.Match.Get<Flight>("FL");
                    var gate = t.Match.Get<Gate>("GT");
                    var fSnap = (Flight)flight.Snapshot();
                    var gSnap = (Gate)gate.Snapshot();
                    fSnap.Status = "Gated";
                    gSnap.IsOccupied = true;
                    t.Update(fSnap, null);
                    t.Update(gSnap, null);
                    t.Publish();
                    Console.WriteLine($"[GateOperations] DOCKED: {fSnap.FlightNumber} secured at Gate {gSnap.GateId}.");
                });

            // =================================================================
            // GROUP 4: "MAIN" (Default Summary & Audit)
            // Runs automatically after focused groups pop off the stack.
            // =================================================================
            engine.Begin("AuditShiftSummary")
                .Where<Flight>("FLB", f => f.Status == "Gated" || f.Status == "Conflict")
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FLB");
                    Console.WriteLine($"[MAIN Audit] Flight {flight.FlightNumber} final state: {flight.Status} (Gate {flight.AssignedGateId})");
                });
        }

        public static void InitializeStep5Rules(ReteEngine.ReteEngine engine)
        {
            Func<Token, Flight, bool> activeFlightsInTerminal = (token, flight) =>
                (flight.Status == "Landed" || flight.Status == "Gated") &&
                flight.AssignedGateId != null &&
                flight.AssignedGateId.StartsWith(token.Get<Terminal>("TM").TerminalName.Last().ToString());

            // =================================================================
            // RULE 1: Terminal Congestion Alert (Count >= MaxCapacity)
            // Anchors on each Terminal and counts active (Landed/Gated) flights
            // assigned to any gate belonging to that terminal.
            // =================================================================
            engine.Begin("TerminalCongestionAlert")
                .Priority(100)
                .Where<Terminal>("TM")
                .Group<Flight>("AllFlights")
                    .Where<Flight>(activeFlightsInTerminal)
                    .Count<Flight>("ActiveFlightCount")
                .Where(token => token.Get<int>("ActiveFlightCount") >= token.Get<Terminal>("TM").MaxCapacity)
                .Then(token =>
                {
                    var terminal = token.Get<Terminal>("TM");
                    int count = token.Get<int>("ActiveFlightCount");
                    Console.WriteLine($"\n[CONGESTION ALERT] {terminal.TerminalName} at capacity ({count}/{terminal.MaxCapacity} active flights)!");
                });

            // =================================================================
            // RULE 2: Terminal Understaffed Alert (Flights > Crews * 2)
            // Chains two Group aggregations on the same Terminal token!
            // =================================================================
            engine.Begin("TerminalUnderstaffedAlert")
                .Priority(80)
                .Where<Terminal>("TM")
                .Group<Flight>("AllFlights")
                    .Where<Flight>(activeFlightsInTerminal)
                    .Count<Flight>("ActiveFlightCount")
                .Group<GroundCrew>("AllCrews")
                    .Where<GroundCrew>((token, crew) =>
                        crew.AssignedTerminal == token.Get<Terminal>("TM").TerminalName)
                    .Count<GroundCrew>("CrewCount")
                .Where(token =>
                {
                    int flights = token.Get<int>("ActiveFlightCount");
                    int crews = token.Get<int>("CrewCount");
                    return flights > 0 && flights > crews * 2;
                })
                .Then(token =>
                {
                    var terminal = token.Get<Terminal>("TM");
                    int flights = token.Get<int>("ActiveFlightCount");
                    int crews = token.Get<int>("CrewCount");
                    Console.WriteLine($"[STAFFING WARNING] {terminal.TerminalName} understaffed: {flights} active flights with only {crews} crew(s)!");
                });

            // =================================================================
            // RULE 3: Terminal Telemetry Summary (Main Audit)
            // =================================================================
            engine.Begin("TerminalStatusReport")
                .Priority(10)
                .Where<Terminal>("TM")
                .Group<Flight>("AllFlights")
                    .Where<Flight>(activeFlightsInTerminal)
                    .Count<Flight>("ActiveFlightCount")
                .Where(token => token.Get<int>("ActiveFlightCount") > 0)
                .Then(token =>
                {
                    var terminal = token.Get<Terminal>("TM");
                    int count = token.Get<int>("ActiveFlightCount");
                    Console.WriteLine($"[TELEMETRY] {terminal.TerminalName}: {count} active flight(s) currently tracked.");
                });
        }

        public static void InitializeGroupByTestRules(ReteEngine.ReteEngine engine)
        {
            Func<Flight, string> byStatus = f => f.Status;
            Func<Flight, bool> hasAssignedGate = f => !string.IsNullOrEmpty(f.AssignedGateId);
            Func<Flight, int> byDelay = f => f.MinutesDelayed;

            Console.WriteLine("=== AIRPORT LOGISTICS: GROUPBY & HAVING VERIFICATION ===");

            // RULE 1: Live Status Bottleneck Alert
            // Groups assigned flights by Status, counts each bucket, and fires when >= 2 share a status.
            engine.Begin("StatusBottleneckAlert")
                .Priority(100)
                .GroupBy<Flight, string>(byStatus, asKeyName: "FlightStatus")
                .Where<Flight>(hasAssignedGate)
                .Count<Flight>("BucketCount")
                .Having(t => t.Get<int>("BucketCount") >= 2)
                .Then(t =>
                {
                    string status = t.Get<string>("FlightStatus");
                    int count = t.Get<int>("BucketCount");
                    Console.WriteLine($"[BOTTLENECK ALERT] {count} assigned flights are currently in '{status}' status!");
                });

            // RULE 2: General Status Telemetry (Any bucket with >= 1 flight)
            engine.Begin("StatusTelemetry")
                .Priority(50)
                .GroupBy<Flight, string>(byStatus, asKeyName: "FlightStatus")
                .Where<Flight>(hasAssignedGate)
                .Count<Flight>("BucketCount")
                .Having(t => t.Get<int>("BucketCount") >= 1)
                .Then(t =>
                {
                    string status = t.Get<string>("FlightStatus");
                    int count = t.Get<int>("BucketCount");
                    Console.WriteLine($"[TELEMETRY] Status '{status}': {count} active flight(s).");
                });

            // RULE 3: Maximum Delay per Status Bucket (.Max)
            engine.Begin("MaxDelayByStatus")
                .Priority(40)
                .GroupBy<Flight, string>(byStatus, asKeyName: "FlightStatus")
                .Where<Flight>(hasAssignedGate)
                .Max<Flight, int>(byDelay, "MaxDelay")
                .Having(t => t.Get<int>("MaxDelay") >= 20)
                .Then(t =>
                {
                    string status = t.Get<string>("FlightStatus");
                    int maxDelay = t.Get<int>("MaxDelay");
                    Console.WriteLine($"[MAX DELAY ALERT] Worst delay in '{status}' is {maxDelay} mins!");
                });

            // RULE 4: Minimum Delay per Status Bucket (.Min)
            engine.Begin("MinDelayByStatus")
                .Priority(30)
                .GroupBy<Flight, string>(byStatus, asKeyName: "FlightStatus")
                .Where<Flight>(hasAssignedGate)
                .Min<Flight, int>(byDelay, "MinDelay")
                .Having(t => t.Get<int>("MinDelay") >= 0)
                .Then(t =>
                {
                    string status = t.Get<string>("FlightStatus");
                    int minDelay = t.Get<int>("MinDelay");
                    Console.WriteLine($"[MIN DELAY TELEMETRY] Best (lowest) delay in '{status}' is {minDelay} mins.");
                });
        }

    }

}
