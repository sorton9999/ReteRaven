//-----------------------------------------------------------------------
// <copyright file="BetaNodeSharingIntegrationTests.cs">
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
using System.Diagnostics;
using Xunit;

namespace ReteTests
{
    /// <summary>
    /// Integration tests for beta-node sharing, focusing on real-world scenarios,
    /// performance characteristics, and memory efficiency.
    /// </summary>
    public class BetaNodeSharingIntegrationTests
    {
        /// <summary>
        /// Test: Real-world scenario with multiple product rules sharing common prefixes.
        /// </summary>
        [Fact]
        public void RealWorldScenario_MultipleProductRulesWithSharing()
        {
            // Arrange
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);

            int alertsGenerated = 0;
            int shippingPrepared = 0;
            int insuranceTagged = 0;

            // Act: Define multiple rules that share common "Product + Inventory" prefix
            Func<Token, Inventory, bool> commonJoin = (t, i) =>
                i.ProductId == t.Get<Product>("P").ProductId;

            // Rule 1: Alert on low stock
            engine.Begin("AlertLowStock")
                .Where<Product>("P", p => p.Category == "Electronics")
                .And<Inventory>("I", commonJoin)
                .Then(t =>
                {
                    var inv = t.Get<Inventory>("I");
                    if (inv.Quantity < 10)
                        alertsGenerated++;
                });

            // Rule 2: Prepare shipping
            engine.Begin("PrepareShipping")
                .Where<Product>("P", p => p.Category == "Electronics")
                .And<Inventory>("I", commonJoin)
                .Then(t =>
                {
                    var inv = t.Get<Inventory>("I");
                    if (inv.Quantity > 0)
                        shippingPrepared++;
                });

            // Rule 3: Tag high-value items for insurance
            engine.Begin("TagInsurance")
                .Where<Product>("P", p => p.Category == "Electronics")
                .And<Inventory>("I", commonJoin)
                .Then(t =>
                {
                    var prod = t.Get<Product>("P");
                    if (prod.Price > 1000)
                        insuranceTagged++;
                });

            var product = new Product
            {
                Id = Guid.NewGuid(),
                ProductId = 100,
                Category = "Electronics",
                Price = 1500,
                Name = "Smart TV"
            };
            var inventory = new Inventory
            {
                Id = Guid.NewGuid(),
                ProductId = 100,
                Quantity = 5
            };

            engine.Assert(product, inventory).FireAll();

            // Assert: All three rules should fire from shared prefix
            Assert.Equal(1, alertsGenerated);
            Assert.Equal(1, shippingPrepared);
            Assert.Equal(1, insuranceTagged);

            var stats = engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("Real-world scenario stats:" + stats);
        }

        /// <summary>
        /// Test: Performance comparison with large number of rules.
        /// </summary>
        [Fact]
        public void PerformanceBenchmark_LargeRuleSetWithSharing()
        {
            // Arrange
            const int ruleCount = 20;
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            var stopwatch = Stopwatch.StartNew();

            // Act: Add 20 rules with identical prefixes
            for (int i = 0; i < ruleCount; i++)
            {
                int ruleIndex = i;
                engine.Begin($"Rule{i}")
                    .Where<Product>("P", p => p.Category == "Electronics")
                    .And<Inventory>("I", (t, inv) => inv.ProductId == t.Get<Product>("P").ProductId)
                    .Then(t => { /* no-op */ });
            }

            stopwatch.Stop();
            var buildTime = stopwatch.ElapsedMilliseconds;

            // Fire with data
            stopwatch.Restart();
            var products = new Product[10];
            var inventories = new Inventory[10];
            for (int i = 0; i < 10; i++)
            {
                products[i] = new Product
                {
                    Id = Guid.NewGuid(),
                    ProductId = i,
                    Category = "Electronics"
                };
                inventories[i] = new Inventory
                {
                    Id = Guid.NewGuid(),
                    ProductId = i,
                    Quantity = 100
                };
            }

            engine.Assert(products);
            engine.Assert(inventories);
            engine.FireAll();

            stopwatch.Stop();
            var fireTime = stopwatch.ElapsedMilliseconds;

            // Assert
            var stats = engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine($"Build time: {buildTime}ms");
            Console.WriteLine($"Fire time: {fireTime}ms");
            Console.WriteLine("Performance stats:" + stats);

            // Verify no exceptions and reasonable performance
            Assert.True(buildTime < 5000, "Rule building should be fast");
            Assert.True(fireTime < 5000, "Fact propagation should be fast");
        }

        /// <summary>
        /// Test: Complex rule hierarchy with partial sharing.
        /// </summary>
        [Fact]
        public void ComplexHierarchy_PartialSharingWithDivergence()
        {
            // Arrange
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            int shipmentsPrepared = 0;
            int warrantyChecked = 0;

            Func<Token, Inventory, bool> invJoin = (t, i) =>
                i.ProductId == t.Get<Product>("P").ProductId;
            Func<Token, Shipment, bool> shipJoin = (t, s) =>
                s.ProductId == t.Get<Inventory>("I").ProductId;

            // Act: Two rules with shared prefix (P→I), then diverge
            engine.Begin("PrepareShipment")
                .Where<Product>("P")
                .And<Inventory>("I", invJoin)
                .And<Shipment>("S", shipJoin)
                .Then(t => shipmentsPrepared++);

            engine.Begin("CheckWarranty")
                .Where<Product>("P")
                .And<Inventory>("I", invJoin)
                .Then(t => warrantyChecked++);

            var product = new Product { Id = Guid.NewGuid(), ProductId = 555 };
            var inventory = new Inventory { Id = Guid.NewGuid(), ProductId = 555 };
            var shipment = new Shipment { Id = Guid.NewGuid(), ProductId = 555 };

            engine.Assert(product, inventory, shipment).FireAll();

            // Assert
            Assert.Equal(1, shipmentsPrepared);
            Assert.Equal(1, warrantyChecked);

            var stats = engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine("Complex hierarchy stats:" + stats);
        }

        /// <summary>
        /// Test: Stress test with many facts and shared rules.
        /// </summary>
        [Fact]
        public void StressTest_ManyFactsWithSharedRules()
        {
            // Arrange
            var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: true);
            int rulesFired = 0;

            // Act: 5 shared rules
            for (int i = 0; i < 5; i++)
            {
                engine.Begin($"Rule{i}")
                    .Where<Product>("P", p => p.Category == "Electronics")
                    .And<Inventory>("I", (t, inv) => inv.ProductId == t.Get<Product>("P").ProductId)
                    .Then(t => rulesFired++);
            }

            // Assert 100 product-inventory pairs
            for (int i = 0; i < 100; i++)
            {
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    ProductId = i,
                    Category = "Electronics"
                };
                var inventory = new Inventory
                {
                    Id = Guid.NewGuid(),
                    ProductId = i,
                    Quantity = 10 + i
                };
                engine.Assert(product, inventory);
            }

            var stopwatch = Stopwatch.StartNew();
            engine.FireAll();
            stopwatch.Stop();

            // Assert: 100 facts × 5 rules = 500 rule fires
            Assert.Equal(500, rulesFired);

            var stats = engine.GetBetaNodeRegistryStatistics();
            Console.WriteLine($"Stress test: {rulesFired} fires in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine("Stress test stats:" + stats);
        }
    }
}