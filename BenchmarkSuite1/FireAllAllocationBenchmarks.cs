using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Attributes;
using ReteCore;
using ReteEngine;
using ReteProgram;
using System;
using System.Collections.Generic;

namespace ReteCoreBenchmarks
{
    [MemoryDiagnoser]
    public class FireAllAllocationBenchmarks
    {
        private ReteEngine.ReteEngine _engine;
        private List<SystemStatus> _statuses;
        private List<Sensor> _sensors;
        [Params(50, 200)]
        public int FactCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _engine = new ReteEngine.ReteEngine();
            int fired = 0;
            _engine.Begin("JoinRule").Where<SystemStatus>("sys", s => s.IsActive).And<Sensor>("sensor-join", (token, sensor) => sensor.IsTriggered && token.Get<SystemStatus>("sys").Name == sensor.Name).Then(token => fired++);
            _statuses = new List<SystemStatus>();
            _sensors = new List<Sensor>();
            for (int i = 0; i < FactCount; i++)
            {
                _statuses.Add(new SystemStatus { Name = $"S{i}", IsActive = true });
                _sensors.Add(new Sensor { Id = Guid.NewGuid(), Name = $"S{i}", IsTriggered = true, Type = "Generic" });
            }
        }

        [Benchmark]
        public void AssertAndFireAll_TokenAndTraversalAllocation()
        {
            var engine = new ReteEngine.ReteEngine();
            int fired = 0;
            engine.Begin("JoinRule").Where<SystemStatus>("sys", s => s.IsActive).And<Sensor>("sensor-join", (token, sensor) => sensor.IsTriggered && token.Get<SystemStatus>("sys").Name == sensor.Name).Then(token => fired++);
            for (int i = 0; i < FactCount; i++)
            {
                engine.Assert(_statuses[i]);
                engine.Assert(_sensors[i]);
            }

            engine.FireAll();
        }

        [Benchmark]
        public void AssertAndFireAll_RollbackOnDiscard()
        {
            var engine = new ReteEngine.ReteEngine();
            // Intentionally avoids joining against Sensor to prevent a combinatorial
            // cross-product of activations; this isolates the per-activation cost of
            // staging changes and rolling them back via RuleExecutionContext.Discard().
            engine.Begin("StageThenFail")
                .Where<SystemStatus>("sys", s => s.IsActive)
                .CtxThen(context =>
                {
                    context.Assert(new Fact<int>(1));
                    context.Publish();
                    throw new InvalidOperationException("Simulated failure after staging changes.");
                });

            for (int i = 0; i < FactCount; i++)
            {
                engine.Assert(_statuses[i]);
            }

            try
            {
                engine.FireAll();
            }
            catch (InvalidOperationException)
            {
                // Expected: staged changes are discarded (rolled back) by Activation.Fire()'s catch block.
            }
        }
    }
}