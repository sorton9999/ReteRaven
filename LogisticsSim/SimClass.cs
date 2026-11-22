using LogisticsEngine;
using LogisticsEngine.AirportLogistics.Engine;
using Microsoft.VisualBasic.FileIO;
using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogisticsSim
{
    internal class SimClass
    {
        private bool _loggingEnabled = true;
        private Timer timer;

        public SimClass() => timer = new Timer(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);

        public async Task Run()
        {
            timer.Change(0, 5000); // Start the timer to trigger every 5 seconds
            bool logging = true;
            while (true)
            {
                DisplayMenu();
                var input = Console.ReadLine();
                await Task.Run(() => HandleUserInput(input!, ref logging));
                WaitForUserAcknowledgment();
            }
        }

        private void TimerCallback(object? state)
        {
            // This method is called by the timer every 5 seconds.
            // You can add any periodic tasks you want to perform here.
        }

        private void ToggleLogging(ref bool logging)
        {
            logging = !logging;
            DisplayLoggingStatus(logging);
        }

        private void ClearConsole()
        {
            Console.Clear();
            Console.WriteLine("=== AIRPORT LOGISTICS: SIMULATION MENU ===");
        }

        private void DisplayMenu()
        {
            Console.WriteLine("\n=== AIRPORT LOGISTICS: SIMULATION MENU ===");
            Console.WriteLine("1. Run Asynchronous Telemetry Simulation");
            Console.WriteLine("2. Run Advanced Landing Simulation");
            Console.WriteLine("3. Run Agenda Groups Simulation");
            Console.WriteLine("4: Run Aggregation Simulation");
            Console.WriteLine("5: Run Group By Simulation");
            Console.WriteLine("C. Clear the console");
            Console.WriteLine("L. Toggle Logging");
            Console.WriteLine("Q. Exit");
            Console.WriteLine("=========================================");
            Console.Write("Select an option (1-5, C|c, L|l, Q|q): ");
        }

        private void HandleUserInput(string input, ref bool logging)
        {
            switch (input)
            {
                case "1":
                    RunTerminalAsyncSim(logging).GetAwaiter().GetResult();
                    break;
                case "2":
                    RunTerminalLandingSim(logging).GetAwaiter().GetResult();
                    break;
                case "3":
                    RunTerminalGroupSim(logging).GetAwaiter().GetResult();
                    break;
                case "4":
                    RunAirportAggregateSim(logging).GetAwaiter().GetResult();
                    break;
                case "5":
                    RunGroupByVerificationSim(logging).GetAwaiter().GetResult();
                    break;
                case "C":
                case "c":
                    ClearConsole();
                    break;
                case "L":
                case "l":
                    ToggleLogging(ref logging);
                    break;
                case "Q":
                case "q":
                    Console.WriteLine("Exiting simulation.");
                    Environment.Exit(0);
                    break;
                default:
                    Console.WriteLine("Invalid option. Please try again.");
                    break;
            }
        }

        private void WaitForUserAcknowledgment()
        {
            Console.WriteLine("\nPress any key to return to the main menu...");
            Console.ReadKey();
        }

        private void DisplayLoggingStatus(bool logging)
        {
            Console.WriteLine($"Logging is currently {(logging ? "Enabled" : "Disabled")}");
        }

        private void CaptureAndHandleKeyPress(ref bool logging)
        {
            if (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(intercept: true);
                if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) && keyInfo.Key == ConsoleKey.L)
                {
                    ToggleLogging(ref logging);
                }
            }
        }

        private async Task RunTerminalAsyncSim(bool logging)
        {
            Console.WriteLine("=== AIRPORT LOGISTICS: ASYNCHRONOUS TELEMETRY SIMULATOR ===");

            // 1. Initialize Engine and load both Step 1 and Step 2 rules
            var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: logging);
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

            //Console.ReadLine();
        }

        private async Task RunTerminalLandingSim(bool logging)
        {
            await Task.Run(() =>
            {
                Console.WriteLine("=== AIRPORT LOGISTICS: STEP 3 ADVANCED LOGIC & BETA SHARING ===");

                // Initialize engine and load rules
                //var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: true);
                var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: logging);

                AirportRulesRegistry.InitializeStep3Rules(engine);

                // Instantiate infrastructure
                var gateA1 = new Gate { GateId = "A1", TerminalName = "Terminal A", IsOccupied = false };
                var gateA2 = new Gate { GateId = "A2", TerminalName = "Terminal A", IsOccupied = false };
                var gateB1 = new Gate { GateId = "B1", TerminalName = "Terminal B", IsOccupied = false };

                // Instantiate flights creating a structural conflict
                var flight101 = new Flight { FlightNumber = "AA101", AssignedGateId = "A1", Status = "EnRoute" };
                var flight202 = new Flight { FlightNumber = "UA202", AssignedGateId = "A2", Status = "EnRoute" };
                var flight303 = new Flight { FlightNumber = "DL303", AssignedGateId = "A2", Status = "EnRoute" };

                Console.WriteLine("\n[SIMULATION] Asserting Flights and Gates...");
                engine.Assert(gateA1, gateA2, gateB1);
                engine.Assert(flight101, flight202, flight303);

                Console.WriteLine("\n--- Phase 1: Evaluating baseline network (No alerts expected) ---");
                engine.FireAll();

                // =================================================================
                // Trigger Rule 3B (Unattended Landing Alert via Not node)
                // =================================================================
                Console.WriteLine("\n--- Phase 2: Simulating Flight AA101 Landing Touchdown ---");
                flight101.Status = "Landed";

                // This should trigger the Unattended Alert because we haven't asserted any GroundCrew yet!
                engine.FireAll();

                // =================================================================
                // Resolve the Not node, and Trigger Rule 3A (Exists Conflict)
                // =================================================================
                Console.WriteLine("\n--- Phase 3: Deploying Ground Crew & Simulating Second Landing ---");
                var crewA = new GroundCrew { CrewId = "Crew_Alpha", AssignedTerminal = "Terminal A" };
                engine.Assert(crewA); // This should satisfy the negation node, clearing Rule 3B

                // Force the second conflicting flight to land
                flight202.Status = "Landed";

                engine.FireAll();

                // This should trigger the GateConflictAlert via the Exists node!
                flight303.Status = "Landed";
                engine.FireAll();

                // Redirect to unoccupied gate, but it should fire an uncrewed gate alert!
                flight303.AssignedGateId = gateB1.GateId;
                flight303.Status = "Landed";
                engine.FireAll();
                var crewB = new GroundCrew { CrewId = "Crew_Beta", AssignedTerminal = "Terminal B" };
                engine.Assert(crewB);
                // New crew assigned, All safe to gate.
                engine.FireAll();

                Console.WriteLine("\n=== STEP 3 VERIFICATION COMPLETE ===");
                //Console.ReadLine();

            }).ConfigureAwait(false);
        }

        private async Task RunTerminalGroupSim(bool logging)
        {
            await Task.Run (() =>
            {
                Console.WriteLine("=== AIRPORT LOGISTICS: STEP 4 AGENDA GROUPS & RULE FLOW ===");
                var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: logging);
                var agendaFiringOrder = new List<string>();
                engine.RuleFired += (_, e) =>
                    agendaFiringOrder.Add($"{e.AgendaGroup}: {e.RuleName}");
                var changes = new List<AgendaFocusStackChangedEventArgs>();
                engine.Agenda.FocusStackChanged += (_, eventArgs) => changes.Add(eventArgs);

                // Queue first, then define the operational workflow stage rules.
                engine.DefineAgendaGroup("GateOperations");
                engine.DefineAgendaGroup("SafetyClearance");
                engine.DefineAgendaGroup("EmergencyOverride");
                AirportRulesRegistry.InitializeStep4Rules(engine);

                var gateA1 = new Gate { GateId = "A1", TerminalName = "Terminal A", IsOccupied = false };
                var gateA2 = new Gate { GateId = "A2", TerminalName = "Terminal A", IsOccupied = false };
                var gateB1 = new Gate { GateId = "B1", TerminalName = "Terminal B", IsOccupied = false };
                var crewA = new GroundCrew { CrewId = "Crew_Alpha", AssignedTerminal = "Terminal A" };

                var flight101 = new Flight { FlightNumber = "AA101", AssignedGateId = "A1", Status = "Landed" };
                var flight202 = new Flight { FlightNumber = "UA202", AssignedGateId = "B1", Status = "Landed" };

                engine.Assert(gateA1, gateA2, gateB1, crewA, flight101, flight202);

                engine.FireGroup("GateOperations");
                engine.FireGroup("SafetyClearance");
                engine.FireGroup("EmergencyOverride");
                engine.FocusAgendaGroup("GateOperations")
                .FireAll();

                var flight203 = new Flight { FlightNumber = "DL203", AssignedGateId = "A1", Status = "Landed" };

                engine.Assert(flight203);
                engine.FireAll();

                Console.WriteLine("Agendas:\n" +
                    string.Join(Environment.NewLine, changes.Select(a =>
                    $"GROUP -- CURRENT: {string.Join(", ", a.CurrentStack)}, PREV: {string.Join(", ", a.PreviousStack)}, REASON: {a.Reason}")));
                Console.WriteLine($"Final focus: {engine.Agenda.FocusedAgendaGroup}");
                Console.WriteLine($"Final firing order: {string.Join(" -> ", agendaFiringOrder)}");
                Console.WriteLine(engine.GetAgendaGroupStatistics());

                }).ConfigureAwait(false);
        }

        private async Task RunAirportAggregateSim(bool logging)
        {
            await Task.Run(() =>
            {
                Console.WriteLine("=== AIRPORT LOGISTICS: STEP 5 AGGREGATION & LOAD BALANCING ===");
                var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: logging);
                AirportRulesRegistry.InitializeStep5Rules(engine);

                var termA = new Terminal { TerminalName = "Terminal A", MaxCapacity = 2 };
                var termB = new Terminal { TerminalName = "Terminal B", MaxCapacity = 2 };
                var crewA = new GroundCrew { CrewId = "Crew_Alpha", AssignedTerminal = "Terminal A" };

                var f1 = new Flight { FlightNumber = "AA101", AssignedGateId = "A1", Status = "Landed" };
                var f2 = new Flight { FlightNumber = "UA202", AssignedGateId = "A2", Status = "EnRoute" };
                var f3 = new Flight { FlightNumber = "DL303", AssignedGateId = "A3", Status = "EnRoute" };

                engine.Assert(termA, termB, crewA, f1, f2, f3);

                Console.WriteLine("\n--- Phase 1: 1 active flight in Terminal A (Below capacity & staffing limit) ---");
                engine.FireAll();

                Console.WriteLine("\n--- Phase 2: UA202 lands in Terminal A (Hits MaxCapacity = 2) ---");
                f2.Status = "Landed";
                engine.FireAll();

                Console.WriteLine("\n--- Phase 3: DL303 lands in Terminal A (3 flights, 1 crew -> Triggers Understaffed Alert) ---");
                f3.Status = "Landed";
                engine.FireAll();

                Console.WriteLine("\n--- Phase 4: Divert DL303 to Terminal B & depart AA101 (Counts decrement incrementally) ---");
                f3.AssignedGateId = "B1";
                f1.Status = "Departed";
                engine.FireAll();

            }).ConfigureAwait(false);
        }

        public async Task RunGroupByVerificationSim(bool logging)
        {
            await Task.Run(() =>
            {
                var engine = new ReteEngine.ReteEngine(enableConditionTraceLogging: logging);
                AirportRulesRegistry.InitializeGroupByTestRules(engine);

                var f1 = new Flight { FlightNumber = "AA101", AssignedGateId = "A1", Status = "EnRoute", MinutesDelayed = 10 };
                var f2 = new Flight { FlightNumber = "UA202", AssignedGateId = "A2", Status = "EnRoute", MinutesDelayed = 35 };
                var f3 = new Flight { FlightNumber = "DL303", AssignedGateId = "B1", Status = "Landed", MinutesDelayed = 5 };
                var f4 = new Flight { FlightNumber = "SW404", AssignedGateId = "", Status = "EnRoute", MinutesDelayed = 90 }; // Ignored by .Where

                Console.WriteLine("\n--- Phase 1: Initial Assert (2 assigned EnRoute, 1 Landed, 1 unassigned EnRoute ignored) ---");
                engine.Assert(f1, f2, f3, f4);
                engine.FireAll();
                PrintCounts(engine, "FlightStatus");

                Console.WriteLine("\n--- Phase 2: AA101 & UA202 Land (EnRoute drops to 0, Landed jumps to 3) ---");
                f1.Status = "Landed";
                f2.Status = "Landed";
                engine.FireAll();
                PrintCounts(engine, "FlightStatus");

                Console.WriteLine("\n--- Phase 3: AA101 & DL303 Dock -> 'Gated' (Landed drops to 1, Gated hits 2) ---");
                f1.Status = "Gated";
                f3.Status = "Gated";
                engine.FireAll();
                PrintCounts(engine, "FlightStatus");

                Console.WriteLine("\n--- Phase 4: DL303 Delay Spikes to 50 mins (Same bucket, Min/Max recalculate) ---");
                f3.MinutesDelayed = 50;
                engine.FireAll();
                PrintCounts(engine, "FlightStatus");

                Console.WriteLine("\n=== GROUPBY VERIFICATION COMPLETE ===");

            }).ConfigureAwait(false);
        }

        static void PrintCounts(ReteEngine.ReteEngine engine, string keyName)
        {
            var counts = engine.GetGroupByCounts(keyName);
            string formatted = counts.Count > 0
                ? string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}"))
                : "(none)";
            Console.WriteLine($"[ACTUAL COUNTS ({keyName})] {formatted}");
        }


    }
}
