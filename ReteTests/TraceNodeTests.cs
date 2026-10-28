using System.IO;
using ReteEngine;
using ReteProgram;
using Xunit;
using Xunit.Abstractions;

namespace ReteTest.Tests;

public class TraceNodeTests
{
    private readonly ITestOutputHelper _testOutput;

    public TraceNodeTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
    }

    [Fact]
    public void TraceNodesReportAndPreserveJoinedTokenFlowAcrossAssertRefreshAndRetract()
    {
        var traceReport = new StringWriter();
        var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: false);
        var observedQuantities = new List<int>();

        engine.Begin("TracedProductInventory")
            .Where<Product>("product", product => product.Category == "Electronics")
            .Trace("after-product-filter", traceReport)
            .And<Inventory>("inventory", (token, inventory) =>
                inventory.ProductId == token.Get<Product>("product").ProductId)
            .Trace("after-inventory-join", traceReport)
            .Then(token => observedQuantities.Add(token.Get<Inventory>("inventory").Quantity));

        var product = new Product { ProductId = 27, Category = "Electronics" };
        var inventory = new Inventory { ProductId = 27, Quantity = 8 };
        engine.Assert(product, inventory);
        engine.FireAll();
        Assert.Equal(new[] { 8 }, observedQuantities);

        inventory.Quantity = 12;
        engine.FireAll();
        Assert.Equal(new[] { 8, 12 }, observedQuantities);

        product.Category = "Food";
        engine.FireAll();
        Assert.Equal(new[] { 8, 12 }, observedQuantities);

        product.Category = "Electronics";
        engine.FireAll();
        Assert.Equal(new[] { 8, 12, 12 }, observedQuantities);

        inventory.ProductId = 99;
        Assert.Empty(engine.Agenda.Activations.Where(activation => activation.State == ReteCore.Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 8, 12, 12 }, observedQuantities);

        var report = traceReport.ToString();
        _testOutput.WriteLine("Trace report:\n{0}", report);

        Assert.Contains("[TRACE:after-product-filter] ASSERT", report);
        Assert.Contains("[TRACE:after-product-filter] RETRACT", report);
        Assert.Contains("[TRACE:after-inventory-join] ASSERT", report);
        Assert.Contains("[TRACE:after-inventory-join] REFRESH Property='Quantity'", report);
        Assert.Contains("[TRACE:after-inventory-join] RETRACT", report);
        Assert.Contains("Token(", report);
    }
}
