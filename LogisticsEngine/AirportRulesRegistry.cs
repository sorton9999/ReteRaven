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

            // =================================================================
            // Gate Conflict Emergency Alert (Using Indexed Exists Node)
            // =================================================================
            engine.Begin("GateConflictAlert")
                .Priority(100)
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .And<Gate>("GT", sharedJoinCheck)
                .And<Flight>("FL", sharedFlightLandedCheck)
                // ADVANCED NODE: Check if ANOTHER flight exists assigned to this exact gate
                
                .Exists<Flight>("ConflictingFlight", (token, otherFlight) =>
                    otherFlight.Status == "Landed" &&
                    otherFlight.AssignedGateId == token.Get<Gate>("GT").GateId &&
                    otherFlight.FlightNumber != token.Get<Flight>("FL").FlightNumber)

                //.Exists<Gate>("ConflictingGate", (token, gate) =>
                //    gate.IsOccupied == true && token.Get<Flight>("FL").AssignedGateId == gate.GateId)
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    var gate = t.Get<Gate>("GT");
                    Console.WriteLine($"\n[🚨 EMERGENCY RULE] GATE CONFLICT DETECTED!");
                    Console.WriteLine($"--> Danger: Flight {flight.FlightNumber} has landed at Gate {gate.GateId}, but another aircraft is occupying or assigned to that space!");
                    // Flight now has a conflict
                    flight.Status = "Conflict";
                    engine.FireAll();
                });

            // =================================================================
            // Unattended Landing Alert (Using Not / Negation Node)
            // =================================================================
            // NOTE: Because this shares the exact same top two lines as Rule 3A,
            // your Beta-Node Sharing architecture will completely reuse that inner memory!
            engine.Begin("UnattendedLandingAlert")
                .First()
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .And<Gate>("GT", sharedJoinCheck)
                .And<Flight>("FL", sharedFlightLandedCheck)
                // ADVANCED NODE: Check if there is a complete ABSENCE of ground crew at this terminal
                .Not<GroundCrew>("GC", sharedCrewJoinTerminals)
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    var gate = t.Get<Gate>("GT");
                    Console.WriteLine($"\n[⚠️ SECURITY ALERT] UNATTENDED LANDING!");
                    Console.WriteLine($"--> Safety Warning: Flight {flight.FlightNumber} touched down at {gate.GateId} ({gate.TerminalName}), but NO ground crew is registered at this terminal!");
                });
            
            engine.Begin("SuccessLanding")
                .Priority(50)
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .And<Gate>("GT", (token, gate) => token.Get<Flight>("FL").AssignedGateId == gate.GateId && !gate.IsOccupied)
                .And<Flight>("FL", (t, flight) => flight.Status != "Conflict")
                .And<GroundCrew>("GC", sharedCrewJoinTerminals)
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    var gate = t.Get<Gate>("GT");
                    flight.Status = "Gated";
                    gate.IsOccupied = true;
                    Console.WriteLine($"\n[SUCCESS] Successful operation: Flight {flight.FlightNumber} safely docked at {gate.GateId}. Landed...Let's Party!!");
                });

            engine.Begin("FlightConflict")
                .Priority(70)
                .Where<Flight>("FL", sharedFlightBaseCheck)
                .Exists<Flight>("FL", (t, flight) => flight.Status == "Conflict")
                .Then(t =>
                {
                    var flight = t.Get<Flight>("FL");
                    Console.WriteLine($"[CONFLICT] Flight {flight.FlightNumber} has a conflict at Gate: {flight.AssignedGateId}");
                });
            

            Console.WriteLine("[ENGINE INIT] Step 3 Advanced Logic & Shared Beta-Memory Nodes compiled.");
        }


    }

}
