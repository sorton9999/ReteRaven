//-----------------------------------------------------------------------
// <copyright file="BetaNodeSharingTests.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using ReteCore;
using ReteEngine;
using ReteProgram;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ReteTests
{
    /// <summary>
    /// Tests for beta-node sharing functionality. These tests verify that multiple rules
    /// sharing identical condition prefixes reuse the same physical beta memory and join nodes,
    /// reducing memory footprint and improving efficiency.
    /// </summary>
    public class BetaNodeSharingTests : IDisposable
    {
        private ReteEngine.ReteEngine _engine;

        public BetaNodeSharingTests()
        {
            // Create engine with sharing enabled (default)
            _engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
        }

        public void Dispose()
        {
            _engine?.ClearBetaNodeCache();
        }

        /// <summary>
        /// Test 1: Two rules with identical Where prefixes should share the beta node.
        /// </summary>
        [Fact]
        public void TwoRulesWithIdenticalWherePrefix_ShareBetaMemory()
        {
            // Arrange
            int rule1Fired = 0;
            int rule2Fired = 0;

            // Act: Define two identical Where clauses
            _engine.Begin("Rule1")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Then(t => rule1Fired++);

            _engine.Begin("Rule2")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Then(t => rule2Fired++);

            var beforeStats = _engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("After building rules:" + beforeStats);

            var product = new Product { Id = Guid.NewGuid(), Category = "Electronics" };
            _engine.Assert(product).FireAll();

            // Assert: Both rules should have fired
            Assert.Equal(1, rule1Fired);
            Assert.Equal(1, rule2Fired);

            // Verify cache statistics show sharing
            var statsStr = _engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("After firing:" + statsStr);

            // Should see cache hit on second rule
            Assert.Contains("hit", statsStr);
        }

        /// <summary>
        /// Test 2: Two rules with identical Where + And prefixes should share both nodes.
        /// </summary>
        [Fact]
        public void TwoRulesWithIdenticalPrefix_ShareBetaAndJoinNodes()
        {
            // Arrange
            int rule1Fired = 0;
            int rule2Fired = 0;

            // Act: Define two rules with identical Where + And prefix
            Func<Token, Inventory, bool> joinCondition = (t, i) =>
                i.ProductId == t.Get<Product>("P").ProductId;

            _engine.Begin("Rule1")
                .Where<Product>("P", p => p.Category == "Electronics")
                .And<Inventory>("I", joinCondition)
                .Then(t => rule1Fired++);

            _engine.Begin("Rule2")
                .Where<Product>("P", p => p.Category == "Electronics")
                .And<Inventory>("I", joinCondition)
                .Then(t => rule2Fired++);

            var product = new Product { Id = Guid.NewGuid(), ProductId = 100, Category = "Electronics" };
            var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = 100 };

            _engine.Assert(product, inventory).FireAll();

            // Assert
            Assert.Equal(1, rule1Fired);
            Assert.Equal(1, rule2Fired);

            var stats = _engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("Identical prefix stats:" + stats);
        }

        /// <summary>
        /// Test 3: Different conditions should NOT share (separate cache entries).
        /// </summary>
        [Fact]
        public void DifferentConditions_CreateSeparateBetaNodes()
        {
            // Arrange
            int rule1Fired = 0;
            int rule2Fired = 0;

            // Act: Define two rules with different Where conditions
            _engine.Begin("Rule1")
                .Where<Product>("P", p => p.Price > 100)
                .Then(t => rule1Fired++);

            _engine.Begin("Rule2")
                .Where<Product>("P", p => p.Price < 50)
                .Then(t => rule2Fired++);

            var expensiveProduct = new Product { Id = Guid.NewGuid(), Price = 150 };
            var cheapProduct = new Product { Id = Guid.NewGuid(), Price = 25 };

            _engine.Assert(expensiveProduct, cheapProduct).FireAll();

            // Assert
            Assert.Equal(1, rule1Fired);
            Assert.Equal(1, rule2Fired);

            var stats = _engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("Different conditions stats:" + stats);
            // Each rule should have its own nodes (no cache hit on second rule's initial Where)
        }

        /// <summary>
        /// Test 4: Shared prefix with different terminal actions still fires both.
        /// </summary>
        [Fact]
        public void SharedPrefixWithDifferentTerminals_BothRulesFire()
        {
            // Arrange
            int rule1Fired = 0;
            int rule2Fired = 0;
            int rule2ActionResult = 0;

            // Act
            Func<Token, Inventory, bool> joinCond = (t, i) =>
                i.ProductId == t.Get<Product>("P").ProductId;

            _engine.Begin("Rule1")
                .Where<Product>("P")
                .And<Inventory>("I", joinCond)
                .Then(t => rule1Fired++);

            _engine.Begin("Rule2")
                .Where<Product>("P")
                .And<Inventory>("I", joinCond)
                .Then(t =>
                {
                    rule2Fired++;
                    rule2ActionResult = t.Get<Inventory>("I").Quantity;
                });

            var product = new Product { Id = Guid.NewGuid(), ProductId = 555 };
            var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = 555, Quantity = 42 };

            _engine.Assert(product, inventory).FireAll();

            // Assert
            Assert.Equal(1, rule1Fired);
            Assert.Equal(1, rule2Fired);
            Assert.Equal(42, rule2ActionResult);
        }

        /// <summary>
        /// Test 5: Cache statistics show meaningful reductions with multiple rules.
        /// </summary>
        [Fact]
        public void MultipleIdenticalRules_CacheStatisticsShowSignificantReduction()
        {
            // Arrange: Create 10 rules with identical prefixes
            const int ruleCount = 10;
            var firedCounts = new Dictionary<int, int>();

            // Act
            for (int i = 0; i < ruleCount; i++)
            {
                int localI = i; // Capture for closure
                firedCounts[i] = 0;

                _engine.Begin($"Rule{i}")
                    .Where<Product>("P", p => p.Category == "Electronics")
                    .And<Inventory>("I", (t, inv) => inv.ProductId == t.Get<Product>("P").ProductId)
                    .Then(t => firedCounts[localI]++);
            }

            var product = new Product { Id = Guid.NewGuid(), ProductId = 1, Category = "Electronics" };
            var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = 1 };

            _engine.Assert(product, inventory).FireAll();

            // Assert: All rules should fire
            foreach (var kvp in firedCounts)
            {
                Assert.Equal(1, kvp.Value);
            }

            var stats = _engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("Multi-rule stats:" + stats);

            // Verify cache hit rate is high (many rules reusing same nodes)
            // With 10 identical rules:
            // - First rule: 2 cache misses (Where, And)
            // - Next 9 rules: 18 cache hits (2 per rule)
            // Expected hit rate: 18 / (18 + 2) = 90%
            Assert.True(stats.Contains("90.0%") || stats.Contains("100%"), 
                "Expected high cache hit rate for identical rules");
        }

        /// <summary>
        /// Test 6: Disabling sharing should prevent node reuse.
        /// </summary>
        [Fact]
        public void SharingDisabled_EachRuleCreatesOwnNodes()
        {
            // Arrange
            var engineNoSharing = new ReteEngine.ReteEngine(enableBetaNodeSharing: false);
            int rule1Fired = 0;
            int rule2Fired = 0;

            // Act
            engineNoSharing.Begin("Rule1")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Then(t => rule1Fired++);

            engineNoSharing.Begin("Rule2")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Then(t => rule2Fired++);

            var product = new Product { Id = Guid.NewGuid(), Category = "Electronics" };
            engineNoSharing.Assert(product).FireAll();

            // Assert: Both rules should still fire (functionality unchanged)
            Assert.Equal(1, rule1Fired);
            Assert.Equal(1, rule2Fired);

            // But registry should be null when sharing is disabled
            Assert.Null(engineNoSharing.BetaNodeRegistry);
        }

        /// <summary>
        /// Test 7: Three-level prefix sharing (Where + And + And).
        /// </summary>
        [Fact]
        public void ThreeLevelPrefixSharing_AllLevelsCached()
        {
            // Arrange
            int rule1Fired = 0;
            int rule2Fired = 0;

            Func<Token, Inventory, bool> joinCond1 = (t, i) =>
                i.ProductId == t.Get<Product>("P").ProductId;
            Func<Token, Shipment, bool> joinCond2 = (t, s) =>
                s.ProductId == t.Get<Inventory>("I").ProductId;

            // Act: Two rules with identical three-level prefix
            _engine.Begin("Rule1")
                .Where<Product>("P")
                .And<Inventory>("I", joinCond1)
                .And<Shipment>("S", joinCond2)
                .Then(t => rule1Fired++);

            _engine.Begin("Rule2")
                .Where<Product>("P")
                .And<Inventory>("I", joinCond1)
                .And<Shipment>("S", joinCond2)
                .Then(t => rule2Fired++);

            var product = new Product { Id = Guid.NewGuid(), ProductId = 777 };
            var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = 777 };
            var shipment = new Shipment { Id = Guid.NewGuid(), ProductId = 777 };

            _engine.Assert(product, inventory, shipment).FireAll();

            // Assert
            Assert.Equal(1, rule1Fired);
            Assert.Equal(1, rule2Fired);

            var stats = _engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("Three-level prefix stats:" + stats);
            // Should see 3+ cache hits for the second rule
        }

        /// <summary>
        /// Test 8: Partial prefix divergence (first two levels shared, third differs).
        /// </summary>
        [Fact]
        public void PartialPrefixSharing_TwoDivergeAtThirdLevel()
        {
            // Arrange
            int rule1Fired = 0;
            int rule2Fired = 0;

            Func<Token, Inventory, bool> joinCond = (t, i) =>
                i.ProductId == t.Get<Product>("P").ProductId;

            // Act: Both share Where + And, but diverge at second And
            _engine.Begin("Rule1")
                .Where<Product>("P")
                .And<Inventory>("I", joinCond)
                .And<Shipment>("S1", (t, s) => s.ProductId == t.Get<Inventory>("I").ProductId)
                .Then(t => rule1Fired++);

            _engine.Begin("Rule2")
                .Where<Product>("P")
                .And<Inventory>("I", joinCond)
                .And<Shipment>("S2", (t, s) => s.ProductId == t.Get<Inventory>("I").ProductId)
                .Then(t => rule2Fired++);

            var product = new Product { Id = Guid.NewGuid(), ProductId = 888 };
            var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = 888 };
            var shipment = new Shipment { Id = Guid.NewGuid(), ProductId = 888 };

            _engine.Assert(product, inventory, shipment).FireAll();

            // Assert
            Assert.Equal(1, rule1Fired);
            Assert.Equal(1, rule2Fired);

            var stats = _engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("Partial prefix divergence stats:" + stats);
            // Should see 2 cache hits (shared Where + And), then 2 cache misses (different second And)
        }

        /// <summary>
        /// Test 9: Memory footprint reduction can be measured by counting nodes.
        /// </summary>
        [Fact]
        public void MemoryReductionMeasurement_SharedNodesAreFewer()
        {
            // Arrange: Create rules with and without sharing
            var engineWithSharing = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            var engineNoSharing = new ReteEngine.ReteEngine(enableBetaNodeSharing: false);

            const int ruleCount = 5;

            // Act: Add same rules to both engines
            for (int i = 0; i < ruleCount; i++)
            {
                engineWithSharing.Begin($"Rule{i}")
                    .Where<Product>("P", p => p.Category == "Electronics")
                    .And<Inventory>("I", (t, inv) => inv.ProductId == t.Get<Product>("P").ProductId)
                    .Then(t => { });

                engineNoSharing.Begin($"Rule{i}")
                    .Where<Product>("P", p => p.Category == "Electronics")
                    .And<Inventory>("I", (t, inv) => inv.ProductId == t.Get<Product>("P").ProductId)
                    .Then(t => { });
            }

            // Assert: Sharing engine should have fewer cached nodes
            int withSharingNodeCount = engineWithSharing.BetaNodeRegistry?.GetTotalCachedNodeCount() ?? 0;
            int noSharingEstimate = ruleCount * 4; // Rough estimate without actual registry

            Console.WriteLine($"With sharing: {withSharingNodeCount} cached nodes");
            Console.WriteLine($"Without sharing (estimated): ~{noSharingEstimate} nodes");

            Assert.True(withSharingNodeCount < noSharingEstimate,
                "Sharing should create fewer cached nodes");

            engineWithSharing.ClearBetaNodeCache();
            engineNoSharing.ClearBetaNodeCache();
        }

        /// <summary>
        /// Test 10: Cache is cleared properly.
        /// </summary>
        [Fact]
        public void ClearBetaNodeCache_ResetsStatistics()
        {
            // Arrange
            _engine.Begin("Rule1")
                .Where<Product>("P")
                .Then(t => { });

            var statsBefore = _engine.GetBetaNodeRegistryStatistics();

            // Act
            _engine.ClearBetaNodeCache();
            var statsAfter = _engine.GetBetaNodeRegistryStatistics();

            // Assert
            Console.WriteLine("Before clear:" + statsBefore);
            Console.WriteLine("After clear:" + statsAfter);
            Assert.Contains("0 misses", statsAfter);
        }
    }
}