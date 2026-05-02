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
using ReteProgram;

namespace ReteTest.Tests
{
    public class ReteBuilderIntegrationTests
    {
        [Fact]
        public void SimpleRule_Fires_When_Condition_Met()
        {
            var engine = new ReteEngine.ReteEngine();

            bool fired = false;

            engine.Begin("SimpleRule")
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fired = true, salience: 0);

            var status = new SystemStatus { Name = "S1", IsActive = true };
            engine.Assert(status);

            engine.FireAll();

            Assert.True(fired);
        }

        [Fact]
        public void JoinRule_Fires_When_BothFactsPresent()
        {
            var engine = new ReteEngine.ReteEngine();

            bool fired = false;

            engine.Begin("JoinRule")
                .Where<SystemStatus>("sys", initialCondition: s => s.IsActive)
                .And<Sensor>("sensor-join", (token, sensor) => sensor.IsTriggered)
                .Then(token => fired = true);

            var status = new SystemStatus { Name = "S2", IsActive = true };
            var sensor = new Sensor { Id = Guid.NewGuid(), IsTriggered = true, Type = "Generic" };

            engine.Assert(status);
            engine.Assert(sensor);

            engine.FireAll();

            Assert.True(fired);
        }

        [Fact]
        public void NotRule_Prevents_Firing_When_NegatedFact_Present()
        {
            var engine = new ReteEngine.ReteEngine();

            bool fired = false;

            engine.Begin("NotRule")
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Not<Sensor>("sensor-not", (token, sensor) => sensor.IsTriggered)
                .Then(token => fired = true);

            var status = new SystemStatus { Name = "S3", IsActive = true };
            var sensor = new Sensor { Id = Guid.NewGuid(), IsTriggered = true, Type = "Blocking" };

            engine.Assert(status);
            engine.Assert(sensor);

            engine.FireAll();

            Assert.False(fired);
        }

        [Fact]
        public void ExistsRule_Fires_When_MatchingFactExists()
        {
            var engine = new ReteEngine.ReteEngine();

            bool fired = false;

            engine.Begin("ExistsRule")
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Exists<Sensor>("sensor-exists", (token, sensor) => sensor.Type == "Temperature")
                .Then(token => fired = true);

            var status = new SystemStatus { Name = "S4", IsActive = true };
            var sensor = new Sensor { Id = Guid.NewGuid(), IsTriggered = false, Type = "Temperature" };

            engine.Assert(status);
            engine.Assert(sensor);

            engine.FireAll();

            Assert.True(fired);
        }

        [Fact]
        public void StartWith_And_JoinWith_Rules_Fire_When_Both_AlphaFactsPresent()
        {
            var engine = new ReteEngine.ReteEngine();

            bool fired = false;

            var alphaProduct = engine.GetAlphaMemory<Product>("product");
            var alphaInventory = engine.GetAlphaMemory<Inventory>("inventory");

            engine.Begin("StartJoinRule")
                .StartWith(alphaProduct, "productFact")
                .JoinWith<Inventory>(alphaInventory, (token, inv) => inv.ProductId == 1)
                .Then(token => fired = true);

            var product = new Product { ProductId = 1, Name = "Widget" };
            var inventory = new Inventory { ProductId = 1, Count = 10 };

            // StartWith/JoinWith use alpha memories, but assertions should still push facts into the engine
            engine.Assert(product);
            engine.Assert(inventory);

            engine.FireAll();

            Assert.True(fired);
        }

        [Fact]
        public void OrRule_Fires_When_Any_Alternate_Predicate_Matches()
        {
            var engine = new ReteEngine.ReteEngine();

            bool firedBySensor = false;
            bool firedByStatus = false;

            // First rule: fires if SystemStatus.IsActive OR a Sensor.Type == "Temperature"
            engine.Begin("OrRule")
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Or<Sensor>("sensor-or", "orDbg",
                    (token, sensor) => sensor.Type == "Temperature")
                .Then(token => firedBySensor = true);

            // Second rule: separate rule that only depends on Where to ensure an OR can be bypassed by the other branch
            var engine2 = new ReteEngine.ReteEngine();
            engine2.Begin("OrStatusRule")
                .Where<SystemStatus>("sys2", null, s => s.Name == "OrTrigger")
                .Then(token => firedByStatus = true);

            // Provide only a sensor that matches the OR predicate
            var status = new SystemStatus { Name = "S5", IsActive = true };
            var status2 = new SystemStatus { Name = "OrTrigger", IsActive = false };
            var sensor = new Sensor { Id = Guid.NewGuid(), IsTriggered = false, Type = "Temperature" };
            engine.Assert(status);
            engine2.Assert(status2);
            engine.Assert(sensor);

            engine.FireAll();

            Assert.True(firedBySensor);
            Assert.False(firedByStatus);
        }

        [Fact]
        public void IfRule_Fires_When_Global_Condition_Matches()
        {
            var engine = new ReteEngine.ReteEngine();

            bool firedByStatus = false;
            bool globalCondition = true;

            // First rule: fires if SystemStatus.IsActive OR a Sensor.Type == "Temperature"
            engine.Begin("IfRule")
                .If("if", () => globalCondition == true)
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => firedByStatus = true);

            // Provide only a sensor that matches the OR predicate
            var status = new SystemStatus { Name = "S5", IsActive = true };
            var status2 = new SystemStatus { Name = "OrTrigger", IsActive = false };
            engine.Assert(status);

            engine.FireAll();

            Assert.True(firedByStatus);
        }

        [Fact]
        public void LateFilterRule_Fires_When_LateCondition_Matches()
        {
            var engine = new ReteEngine.ReteEngine();
            bool firedByStatus = false;
            // Fires if SystemStatus.IsActive and a Sensor.Type == "Temperature",
            // but only if the sensor is triggered at the time of firing (late condition)
            engine.Begin("LateFilterRule")
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .And<Sensor>("sensor", (token, sensor) => sensor.Type == "Temperature")
                .If<Sensor>("sensor", (sensor) => sensor.IsTriggered)
                .Then(token => firedByStatus = true);
            // Provide both facts, but the sensor will only match the late condition, so the rule should still fire
            var status = new SystemStatus { Name = "LateStatus", IsActive = true };
            var sensor = new Sensor { Id = Guid.NewGuid(), IsTriggered = true, Type = "Temperature" };
            engine.Assert(status);
            engine.Assert(sensor);
            engine.FireAll();
            Assert.True(firedByStatus);
        }

        [Fact]
        public void PriorityRule_Simple_Fire_Order()
        {
            var engine = new ReteEngine.ReteEngine();
            string fireOrder = "";
            engine.Begin("PriorityRule1")
                .Priority(100)
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "A");
            engine.Begin("PriorityRule2")
                .Priority(200)
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "B");
            var status = new SystemStatus { Name = "S6", IsActive = true };
            engine.Assert(status);
            engine.FireAll();
            Assert.Equal("BA", fireOrder); // Rule with higher salience (B) should fire before A
        }
        [Fact]
        public void PriorityRule_First_Before_Next()
        {
            var engine = new ReteEngine.ReteEngine();
            string fireOrder = "";
            engine.Begin("PriorityRule1")
                .Next()
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "A");
            engine.Begin("PriorityRule2")
                .First()
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "B");
            var status = new SystemStatus { Name = "S7", IsActive = true };
            engine.Assert(status);
            // Fire first activation (B), then check that the next activation is A
            engine.FireAll();
            Assert.Equal("BA", fireOrder);
        }
        [Fact]
        public void SalienceRule_First_Before_Next()
        {
            var engine = new ReteEngine.ReteEngine();
            string fireOrder = "";
            engine.Begin("SalienceRule1")
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "A", 100);
            engine.Begin("SalienceRule2")
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "B", 200);
            var status = new SystemStatus { Name = "S8", IsActive = true };
            engine.Assert(status);
            // Fire first activation (B), then check that the next activation is A
            engine.FireAll();
            Assert.Equal("BA", fireOrder);
        }

        [Fact]
        public void PriorityAndSalience_Rules_Fire_In_Correct_Order()
        {
            var engine = new ReteEngine.ReteEngine();
            string fireOrder = "";
            engine.Begin("ComplexPriorityRule1")
                .Priority(100)
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "A", 200);
            engine.Begin("ComplexPriorityRule2")
                .First()
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "B", 200);
            engine.Begin("TimeOrderPriorityRule3")
                .Priority(100)
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "C", 200);
            engine.Begin("ComplexPriorityRule4")
                .Priority(100)
                .Where<SystemStatus>("sys", null, s => s.IsActive)
                .Then(token => fireOrder += "D", 150);
            var status = new SystemStatus { Name = "S9", IsActive = true };
            engine.Assert(status);
            // Expected order: B (highest priority and salience), then C (time order, last seen first fires), then A (next in order), then D
            engine.FireAll();
            Assert.Equal("BCAD", fireOrder);
        }
    }
}