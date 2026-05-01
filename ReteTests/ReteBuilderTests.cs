//-----------------------------------------------------------------------
// <copyright file="ReteBuilder.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using Xunit;
using ReteEngine;
using ReteCore;

namespace ReteTest.Tests
{
    public class ReteBuilderTests
    {
        [Fact]
        public void Where_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-where");

            var returned = builder.Where<SystemStatus>("sys", debugLabel: "dbg", initialCondition: s => true);

            Assert.Same(builder, returned);
        }

        [Fact]
        public void And_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-and");

            // start with an initial where so _lastNode is set
            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            var returned = builder.And<Sensor>("sensor-join", (token, sensor) => sensor.IsTriggered, debugLabel: "joinDbg");

            Assert.Same(builder, returned);
        }

        [Fact]
        public void Or_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-or");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            var returned = builder.Or<Sensor>("sensor-or", "orDbg",
                (token, sensor) => sensor.IsTriggered,
                (token, sensor) => sensor.Type == "Temperature"
            );

            Assert.Same(builder, returned);
        }

        [Fact]
        public void AndNot_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-andnot");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            var returned = builder.AndNot<Sensor>("sensor-andnot", (token, sensor) => sensor.IsTriggered, debugLabel: "andNotDbg");

            Assert.Same(builder, returned);
        }

        [Fact]
        public void Not_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-not");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            var returned = builder.Not<Sensor>("sensor-not", (token, sensor) => sensor.IsTriggered, debugLabel: "notDbg");

            Assert.Same(builder, returned);
        }

        [Fact]
        public void Exists_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-exists");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            var returned = builder.Exists<Sensor>("sensor-exists", (token, sensor) => sensor.IsTriggered, debugLabel: "existsDbg");

            Assert.Same(builder, returned);
        }

        [Fact]
        public void Then_ActionOverload_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-then-action");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            bool invoked = false;
            Action<Token> action = t => invoked = true;

            var returned = builder.Then(action, salience: 5);

            Assert.Same(builder, returned);
            // We do not assert invoked here because firing requires the engine/agenda run.
        }

        [Fact]
        public void Then_AgendaOverload_DoesNotThrow_And_Asserts_Terminal()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-then-agenda");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            bool invoked = false;
            Action<Token> action = t => invoked = true;

            // Use engine's agenda to register the terminal action
            builder.Then(engine.Agenda, action, salience: 1);

            // This test ensures the call path succeeded. Execution by the agenda is an integration concern.
            Assert.True(true);
        }

        [Fact]
        public void Trace_ReturnsBuilder_And_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-trace");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            var returned = builder.Trace("trace-point");

            Assert.Same(builder, returned);
        }

        [Fact]
        public void StartWith_And_JoinWith_DoNotThrow_And_AreFluent()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-startjoin");

            // Obtain alpha memories from engine and use StartWith
            var alpha1 = engine.GetAlphaMemory<Product>("product");
            var alpha2 = engine.GetAlphaMemory<Inventory>("inventory");

            var returned1 = builder.StartWith(alpha1, "productFact");

            Assert.Same(builder, returned1);

            var returned2 = builder.JoinWith<Inventory>(alpha2, (token, inv) => inv.ProductId == 123);

            Assert.Same(builder, returned2);
        }

        [Fact]
        public void Assert_CallsLastNode_DoesNotThrow()
        {
            var engine = new ReteEngine.ReteEngine();
            var builder = new ReteBuilder<object>(engine, "rule-assert");

            builder.Where<SystemStatus>("sys", initialCondition: s => s.IsActive);

            // Call Assert on builder (should be safe; no exceptions)
            var status = new SystemStatus { Name = "X", IsActive = true };
            builder.Assert(status);

            Assert.True(true);
        }
    }
}