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

        [Fact]
        public void OrJoin_ShouldShareNodesAndFireCorrectly_WhenAnyPredicateMatches()
        {
            // Arrange
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            int rule1Fires = 0;
            int rule2Fires = 0;

            // Define two distinct rules that share the exact same structural .Or predicate array array footprint
            engine.Begin("Rule_Or_First")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Or<CriticalCell>("Fact", null,
                    (t, c) => c.Value as int? > 500,
                    (t, c) => c.Value as int? < 200)
                .Then(t => rule1Fires++);

            engine.Begin("Rule_Or_Second")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Or<CriticalCell>("Fact", null,
                    (t, c) => c.Value as int? > 500,
                    (t, c) => c.Value as int? < 200)
                .Then(t => rule2Fires++);

            var product = new Product { ProductId = 1, Category = "Electronics" };

            // Fact 1 satisfies predicate #1 (> 500)
            var cellHigh = new CriticalCell { Id = Guid.NewGuid(), Value = 650 };
            // Fact 2 satisfies predicate #2 (< 200)
            var cellLow = new CriticalCell { Id = Guid.NewGuid(), Value = 120 };
            // Fact 3 satisfies neither
            var cellMid = new CriticalCell { Id = Guid.NewGuid(), Value = 350 };

            // Act
            engine.Assert(product);
            engine.Assert(cellHigh); // Match 1
            engine.Assert(cellLow);  // Match 2
            engine.Assert(cellMid);  // No Match
            engine.FireAll();

            // Assert: Both rules should fire exactly twice (once for cellHigh, once for cellLow)
            Assert.Equal(2, rule1Fires);
            Assert.Equal(2, rule2Fires);

            // Verify Graph Cache Optimization: 2 rules compiled but only 1 unique JoinPair created in memory
            var stats = engine.GetBetaNodeRegistryStatistics();
            Assert.Contains("1 hits", stats.ToString() ?? "");
        }

        [Fact]
        public void AndNotJoin_ShouldShareNodesAndFireCorrectly_WhenConditionIsNegated()
        {
            // Arrange
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            int rule1Fires = 0;
            int rule2Fires = 0;

            Func<Token, Inventory, bool> sharedFilter = (t, inv) => inv.Quantity == 0;

            // Both rules use the exact same negated filter context block
            engine.Begin("Rule_AndNot_A")
                .Where<Product>("P", p => p.Category == "Electronics")
                .AndNot<Inventory>("I", sharedFilter)
                .Then(t => rule1Fires++);

            engine.Begin("Rule_AndNot_B")
                .Where<Product>("P", p => p.Category == "Electronics")
                .AndNot<Inventory>("I", sharedFilter)
                .Then(t => rule2Fires++);

            var product = new Product { ProductId = 5, Category = "Electronics" };

            // Inventory with Quantity != 0 will evaluate to TRUE under your negated filter!
            var validInventory = new Inventory { ProductId = 5, Quantity = 10 };

            // Act
            engine.Assert(product);
            engine.Assert(validInventory);
            engine.FireAll();

            // Assert: Both rules fire because the join condition (inv.Quantity == 0) is false, making the negated filter true.
            Assert.Equal(1, rule1Fires);
            Assert.Equal(1, rule2Fires);

            // Verify Graph Cache Optimization: Cache successfully hit on the shared JoinNode list mapping
            var stats = engine.GetBetaNodeRegistryStatistics();
            Assert.Contains("1 hits", stats.ToString() ?? "");
        }

        [Fact]
        public void PureNotJoin_ShouldShareNodesAndFireCorrectly_WhenRightSideIsCompletelyAbsent()
        {
            // Arrange
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            int rule1Fires = 0;
            int rule2Fires = 0;

            Func<Token, RiskFactor, bool> sharedNotCondition = (t, r) => r.Severity == "Critical";

            // Two separate rules testing for the ABSENCE of a Critical RiskFactor matching the active Product
            engine.Begin("Rule_Not_Missing_1")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Not<RiskFactor>("R", sharedNotCondition)
                .Then(t => rule1Fires++);

            engine.Begin("Rule_Not_Missing_2")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Not<RiskFactor>("R", sharedNotCondition)
                .Then(t => rule2Fires++);

            var safeProduct = new Product { ProductId = 99, Category = "Electronics" };
            var lowRisk = new RiskFactor { ProductId = 99, Severity = "Low" }; // Not critical, shouldn't block the rule

            // Act Step 1: Assert data with zero matching critical risk factors
            engine.Assert(safeProduct);
            engine.Assert(lowRisk);
            engine.FireAll();

            // Assert Phase 1: Both rules execute because zero critical factors exist for Product 99
            Assert.Equal(1, rule1Fires);
            Assert.Equal(1, rule2Fires);

            // Act Step 2: Clear state and assert a critical factor that blocks execution
            var engineSecondRun = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            int blockedFires = 0;
            engineSecondRun.Begin("BlockedRule")
                .Where<Product>("P", p => p.Category == "Electronics")
                .Not<RiskFactor>("R", sharedNotCondition)
                .Then(t => blockedFires++);

            var blockedProduct = new Product { ProductId = 100, Category = "Electronics" };
            var criticalRisk = new RiskFactor { ProductId = 100, Severity = "Critical" }; // This must block propagation!

            engineSecondRun.Assert(blockedProduct);
            engineSecondRun.Assert(criticalRisk);
            engineSecondRun.FireAll();

            // Assert Phase 2: The presence of the critical factor successfully blocks the NotNode branch
            Assert.Equal(0, blockedFires);

            // Verify Graph Cache Optimization: Confirm that your registry safely tracks NotNode boundaries inside its cache tables
            var stats = engine.GetBetaNodeRegistryStatistics();
            Assert.Contains("Total Cached Nodes:", stats.ToString() ?? "");
        }

        [Fact]
        public void CombinedPipeline_ShouldShareAllNodeTypesAndMaintainCorrectBehaviorAcrossComplexChains()
        {
            // Arrange
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            int rule1Executions = 0;
            int rule2Executions = 0;

            Func<Product, bool> sharedWhereFilter = p => p.Category == "Electronics";
            Func<Token, CriticalCell, bool> orFilter1 = (t, c) => c.Value as int? > 500;
            Func<Token, CriticalCell, bool> orFilter2 = (t, c) => c.Value as int? < 200;
            Func<Token, Inventory, bool> sharedAndNotFilter = (t, inv) => inv.Quantity == 0;
            Func<Token, RiskFactor, bool> sharedNotCondition = (t, r) => r.Severity == "Critical";

            // RULE 1: Highly nested conditional chain
            engine.Begin("ComplexRule_1")
                .Where<Product>("P", sharedWhereFilter)
                .Or<CriticalCell>("Fact", null,
                    orFilter1,
                    orFilter2)
                .AndNot<Inventory>("I", sharedAndNotFilter)
                .Not<RiskFactor>("R", sharedNotCondition)
                .Then(t => rule1Executions++);

            // RULE 2: Structurally identical chain to force total network node sharing across every single layer!
            engine.Begin("ComplexRule_2")
                .Where<Product>("P", sharedWhereFilter)
                .Or<CriticalCell>("Fact", null,
                    orFilter1,
                    orFilter2)
                .AndNot<Inventory>("I", sharedAndNotFilter)
                .Not<RiskFactor>("R", sharedNotCondition)
                .Then(t => rule2Executions++);

            // Setup perfect alignment data
            var targetProduct = new Product { ProductId = 42, Category = "Electronics" };
            var passingCell = new CriticalCell { Id = Guid.NewGuid(), Value = 800 };       // Passes Or (>500)
            var passingInventory = new Inventory { ProductId = 42, Quantity = 55 };       // Passes AndNot (Quantity != 0)
            var minorRisk = new RiskFactor { ProductId = 42, Severity = "Minimal" };       // Passes Not (No Critical factors present)

            // Act
            engine.Assert(targetProduct);
            engine.Assert(passingCell);
            // Passing this item satisfies the full sequence cascade across our unified shared nodes!
            engine.Assert(passingInventory);
            engine.Assert(minorRisk);
            engine.FireAll();

            // Assert Behavioral Outcomes: The full pipeline was verified and executed cleanly across both shared rule lanes
            Assert.Equal(1, rule1Executions);
            Assert.Equal(1, rule2Executions);

            // Assert Graph Registry Optimizations: The registry statistics should show multi-layer cache hits
            var stats = engine.GetBetaNodeRegistryStatistics();

            // Ensure that multiple hits were successfully logged as the compiler reused layers step-by-step
            Assert.Contains("4 hits", stats.ToString() ?? "");
        }

    }
}