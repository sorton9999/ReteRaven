
using LogisticsEngine;
using LogisticsEngine.AirportLogistics.Engine;
using ReteEngine;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LogisticsSim
{
    class Program
    {
        /*
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== AIRPORT LOGISTICS: ASYNCHRONOUS TELEMETRY SIMULATOR ===");

            // 1. Initialize Engine and load both Step 1 and Step 2 rules
            var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: false);
            AirportRulesRegistry.InitializeBaseRules(engine);
            AirportRulesRegistry.InitializeStep2Rules(engine);

            // 2. Setup baseline facts
            var gateA1 = new Gate { GateId = "A1", TerminalName = "Terminal A", IsOccupied = false };
            var flight101 = new Flight { FlightNumber = "AA101", AssignedGateId = "A1", Status = "EnRoute", MinutesDelayed = 0 };

            engine.Assert(gateA1);
            engine.Assert(flight101);
            engine.FireAll(); // Run initial baseline matchpass

            // 3. Create a Cancellation Token to cleanly stop our background thread later
            using var cts = new CancellationTokenSource();

            Console.WriteLine("\n[SIMULATOR] Starting Asynchronous Telemetry Feed Task...");

            // =================================================================
            // START BACKGROUND SIMULATION TASK
            // =================================================================
            Task telemetryTask = Task.Run(async () =>
            {
                int currentDelay = 0;
                while (!cts.Token.IsCancellationRequested)
                {
                    // Simulate time passing: Add a 10-minute delay chunk every second
                    await Task.Delay(1000, cts.Token);
                    currentDelay += 10;

                    Console.WriteLine($"\n[TELEMETRY STREAM] Update: Flight {flight101.FlightNumber} delay is now {currentDelay} mins.");

                    // Mutate the object in-place on the background thread.
                    // This kicks off your internal INotifyPropertyChanged logic.
                    flight101.MinutesDelayed = currentDelay;
                }
            }, cts.Token);

            // =================================================================
            // MAIN INFERENCE ENGINE MONITORING LOOP
            // =================================================================
            Console.WriteLine("[ENGINE] Inference layer is now listening for state mutations. Press ENTER to stop simulation.");

            // Keep the engine looping as long as the user hasn't pressed Enter or the flight hasn't landed/escalated
            while (!Console.KeyAvailable)
            {
                // Actively check the Agenda for rule activations that were queued up by background refreshes
                engine.FireAll();

                // Yield control briefly to avoid maxing out a CPU core while checking the agenda
                await Task.Delay(100);

                // Graceful loop exit condition if our rule successfully executed its pipeline change
                if (flight101.Status == "Delayed")
                {
                    Console.WriteLine("\n[ENGINE SUCCESS] Escalation captured! Halting monitoring loop.");
                    break;
                }
            }

            // Clean up background tasks
            cts.Cancel();
            try { await telemetryTask; } catch (TaskCanceledException) { }

            // =================================================================
            // POST-SIMULATION VERIFICATION
            // =================================================================
            Console.WriteLine("\n--- Step 2 Asynchronous Simulation Metrics ---");
            Console.WriteLine($"Final Recorded Delay: {flight101.MinutesDelayed} minutes");
            Console.WriteLine($"Final Engine Status: {flight101.Status} (Expected: Delayed)");

            if (flight101.Status == "Delayed")
            {
                Console.WriteLine("\n SUCCESS: The engine successfully processed asynchronous in-place updates and executed the publish-resume pipeline!");
            }
            else
            {
                Console.WriteLine("\n❌ FAILURE: Telemetry updates were either swallowed or failed to trigger the agenda.");
            }

            Console.ReadLine();
        }
        */

        static void Main(string[] args)
        {
            Console.WriteLine("=== AIRPORT LOGISTICS: STEP 3 ADVANCED LOGIC & BETA SHARING ===");

            // 1. Initialize engine and load rules
            //var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: true);
            var engine = new ReteEngine.ReteEngine();
            AirportRulesRegistry.InitializeStep3Rules(engine);

            // 2. Instantiate infrastructure
            var gateA1 = new Gate { GateId = "A1", TerminalName = "Terminal A", IsOccupied = false };
            var gateA2 = new Gate { GateId = "A2", TerminalName = "Terminal A", IsOccupied = false };

            // 3. Instantiate flights creating a structural conflict
            var flight101 = new Flight { FlightNumber = "AA101", AssignedGateId = "A1", Status = "EnRoute" };
            var flight202 = new Flight { FlightNumber = "UA202", AssignedGateId = "A2", Status = "EnRoute" };
            var flight303 = new Flight { FlightNumber = "DL303", AssignedGateId = "A2", Status = "EnRoute" };

            Console.WriteLine("\n[SIMULATION] Asserting Flights and Gates...");
            engine.Assert(gateA1, gateA2);
            engine.Assert(flight101, flight202, flight303);

            Console.WriteLine("\n--- Phase 1: Evaluating baseline network (No alerts expected) ---");
            engine.FireAll();

            // =================================================================
            // TEST 1: Trigger Rule 3B (Unattended Landing Alert via Not node)
            // =================================================================
            Console.WriteLine("\n--- Phase 2: Simulating Flight AA101 Landing Touchdown ---");
            flight101.Status = "Landed";

            // This should trigger the Unattended Alert because we haven't asserted any GroundCrew yet!
            engine.FireAll();

            // =================================================================
            // TEST 2: Resolve the Not node, and Trigger Rule 3A (Exists Conflict)
            // =================================================================
            Console.WriteLine("\n--- Phase 3: Deploying Ground Crew & Simulating Second Landing ---");
            var crewA = new GroundCrew { CrewId = "Crew_Alpha", AssignedTerminal = "Terminal A" };
            engine.Assert(crewA); // This should satisfy the negation node, clearing Rule 3B

            // Force the second conflicting flight to land
            flight202.Status = "Landed";

            // This should trigger the GateConflictAlert via the Exists node!
            engine.FireAll();

            flight303.Status = "Landed";
            //flight202.AssignedGateId = gateA2.GateId;
            engine.FireAll();

            Console.WriteLine("\n=== STEP 3 VERIFICATION COMPLETE ===");
            Console.ReadLine();
        }

    }
}
