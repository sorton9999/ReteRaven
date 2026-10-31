using System.ComponentModel;
using ReteCore;
using ReteEngine;
using ReteProgram;
using Xunit;

namespace ReteTest.Tests;

public class UpdatePreparationTests
{
    [Fact]
    public void AlphaCondition_RefreshTracksBothMatchTransitionsWithoutDuplicates()
    {
        var memory = new AlphaMemory();
        var node = new AlphaConditionNode<MutableFact>(nameof(MutableFact.Value), fact => fact.Value > 0, memory);
        var fact = new MutableFact { Value = 1 };

        node.Assert(fact);
        Assert.Single(memory.Facts);

        fact.Value = 0;
        node.Refresh(fact, nameof(MutableFact.Value));
        Assert.Empty(memory.Facts);

        fact.Value = 1;
        node.Refresh(fact, nameof(MutableFact.Value));
        node.Refresh(fact, nameof(MutableFact.Value));
        Assert.Single(memory.Facts);

        fact.Value = 0;
        node.Refresh(fact, nameof(MutableFact.Value));
        Assert.Empty(memory.Facts);
    }

    [Fact]
    public void Join_RefreshRetractsStaleMatchAndDoesNotDuplicateRepeatedMatches()
    {
        var leftMemory = new BetaMemory();
        var rightMemory = new AlphaMemory();
        var capture = new RecordingNode();
        var join = new JoinNode(leftMemory, rightMemory, "right",
            (token, right) => token.Get<MutableFact>("left").Value == ((MutableFact)right).Value);
        join.AddSuccessor(capture);

        var left = new MutableFact { Value = 7 };
        var right = new MutableFact { Value = 7 };
        leftMemory.Assert(new Token("left", left));
        rightMemory.Assert(right);
        Assert.Single(capture.Assertions);

        right.Value = 8;
        rightMemory.Refresh(right, nameof(MutableFact.Value));
        Assert.Single(capture.Retractions);
        Assert.Empty(capture.ActiveTokens);

        right.Value = 7;
        rightMemory.Refresh(right, nameof(MutableFact.Value));
        rightMemory.Refresh(right, nameof(MutableFact.Value));
        Assert.Equal(2, capture.Assertions.Count);
        Assert.Single(capture.ActiveTokens);
        Assert.Single(capture.Refreshes);

        right.Name = "refreshed-right";
        rightMemory.Refresh(right, nameof(MutableFact.Name));
        var refreshedOutput = Assert.IsType<Token>(capture.Refreshes.Last().Fact);
        Assert.Equal("refreshed-right", refreshedOutput.Get<MutableFact>("right").Name);
    }

    [Fact]
    public void Join_RefreshReconcilesChangesFromTheLeftInput()
    {
        var leftMemory = new BetaMemory();
        var rightMemory = new AlphaMemory();
        var capture = new RecordingNode();
        var join = new JoinNode(leftMemory, rightMemory, "right",
            (token, right) => token.Get<MutableFact>("left").Value == ((MutableFact)right).Value);
        join.AddSuccessor(capture);

        var left = new MutableFact { Value = 3 };
        var right = new MutableFact { Value = 3 };
        leftMemory.Assert(new Token("left", left));
        rightMemory.Assert(right);
        Assert.Single(capture.Assertions);

        left.Value = 4;
        leftMemory.Refresh(left, nameof(MutableFact.Value));
        Assert.Single(capture.Retractions);
        Assert.Empty(capture.ActiveTokens);

        left.Value = 3;
        leftMemory.Refresh(left, nameof(MutableFact.Value));
        leftMemory.Refresh(left, nameof(MutableFact.Name));
        Assert.Equal(2, capture.Assertions.Count);
        Assert.Single(capture.ActiveTokens);
        Assert.Single(capture.Refreshes);
        var refreshedOutput = Assert.IsType<Token>(capture.Refreshes[0].Fact);
        Assert.Equal(3, refreshedOutput.Get<MutableFact>("left").Value);
    }

    [Fact]
    public void JoinMatchTransitionsCancelAndRecreatePendingActivationsForEitherInput()
    {
        var engine = new ReteEngine.ReteEngine();
        var fireCount = 0;
        engine.Begin("ProductInventory")
            .Where<Product>("product", _ => true)
            .And<Inventory>("inventory", (token, inventory) =>
                inventory.ProductId == token.Get<Product>("product").ProductId)
            .Then(_ => fireCount++);

        var product = new Product { ProductId = 10 };
        var inventory = new Inventory { ProductId = 10 };
        engine.Assert(product, inventory);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        inventory.ProductId = 11;
        Assert.Empty(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(0, fireCount);

        inventory.ProductId = 10;
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        product.ProductId = 11;
        Assert.Empty(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(0, fireCount);

        product.ProductId = 10;
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void NotAndExists_OnlyPropagateWhenMatchCountCrossesZero()
    {
        var left = new MutableFact { Value = 1 };
        var token = new Token("left", left);
        var firstBlocker = new MutableFact { Value = 1 };
        var secondBlocker = new MutableFact { Value = 1 };

        var notNode = new NotNode("not-multiple", (match, right) =>
            match.Get<MutableFact>("left").Value == ((MutableFact)right).Value);
        var notCapture = new RecordingNode();
        notNode.AddSuccessor(notCapture);
        notNode.Assert(token);
        notNode.Assert(firstBlocker);
        notNode.Assert(secondBlocker);
        firstBlocker.Value = 2;
        notNode.Refresh(firstBlocker, nameof(MutableFact.Value));
        Assert.Single(notCapture.Assertions);
        Assert.Single(notCapture.Retractions);
        notNode.Retract(secondBlocker);
        Assert.Equal(2, notCapture.Assertions.Count);

        firstBlocker.Value = 1;
        var existsNode = new ExistsNode("exists-multiple", (match, right) =>
            match.Get<MutableFact>("left").Value == ((MutableFact)right).Value);
        var existsCapture = new RecordingNode();
        existsNode.AddSuccessor(existsCapture);
        existsNode.Assert(token);
        existsNode.Assert(firstBlocker);
        existsNode.Assert(secondBlocker);
        Assert.Single(existsCapture.Assertions);
        secondBlocker.Value = 3;
        existsNode.Refresh(secondBlocker, nameof(MutableFact.Value));
        Assert.Empty(existsCapture.Retractions);
        firstBlocker.Value = 4;
        existsNode.Refresh(firstBlocker, nameof(MutableFact.Value));
        Assert.Single(existsCapture.Retractions);
    }

    [Fact]
    public void NotAndExists_ReconcileTransitionsWhenTheLeftFactChanges()
    {
        var left = new MutableFact { Value = 1 };
        var token = new Token("left", left);
        var right = new MutableFact { Value = 2 };

        var notNode = new NotNode("not-left-change", (match, fact) =>
            match.Get<MutableFact>("left").Value == ((MutableFact)fact).Value);
        var notCapture = new RecordingNode();
        notNode.AddSuccessor(notCapture);
        notNode.Assert(token);
        notNode.Assert(right);
        Assert.Single(notCapture.Assertions);

        left.Value = 2;
        notNode.Refresh(token, nameof(MutableFact.Value));
        Assert.Single(notCapture.Retractions);
        left.Value = 1;
        notNode.Refresh(token, nameof(MutableFact.Value));
        Assert.Equal(2, notCapture.Assertions.Count);

        var existsNode = new ExistsNode("exists-left-change", (match, fact) =>
            match.Get<MutableFact>("left").Value == ((MutableFact)fact).Value);
        var existsCapture = new RecordingNode();
        existsNode.AddSuccessor(existsCapture);
        existsNode.Assert(token);
        existsNode.Assert(right);
        Assert.Empty(existsCapture.Assertions);

        left.Value = 2;
        existsNode.Refresh(token, nameof(MutableFact.Value));
        Assert.Single(existsCapture.Assertions);
        left.Value = 1;
        existsNode.Refresh(token, nameof(MutableFact.Value));
        Assert.Single(existsCapture.Retractions);
    }

    [Fact]
    public void Not_RefreshReconcilesCountsAndOnlyPropagatesZeroTransitions()
    {
        var node = new NotNode("not", (token, right) =>
            token.Get<MutableFact>("left").Value == ((MutableFact)right).Value);
        var capture = new RecordingNode();
        node.AddSuccessor(capture);

        var left = new MutableFact { Value = 1 };
        var token = new Token("left", left);
        var blocker = new MutableFact { Value = 1 };
        node.Assert(token);
        Assert.Single(capture.Assertions);

        node.Assert(blocker);
        Assert.Single(capture.Retractions);

        node.Refresh(blocker, nameof(MutableFact.Value));
        node.Refresh(blocker, nameof(MutableFact.Value));
        Assert.Single(capture.Assertions);
        Assert.Single(capture.Retractions);

        blocker.Value = 2;
        node.Refresh(blocker, nameof(MutableFact.Value));
        node.Refresh(blocker, nameof(MutableFact.Value));
        Assert.Equal(2, capture.Assertions.Count);
        Assert.Single(capture.Retractions);
    }

    [Fact]
    public void Exists_RefreshReconcilesCountsAndOnlyPropagatesExistenceTransitions()
    {
        var node = new ExistsNode("exists", (token, right) =>
            token.Get<MutableFact>("left").Value == ((MutableFact)right).Value);
        var capture = new RecordingNode();
        node.AddSuccessor(capture);

        var left = new MutableFact { Value = 1 };
        var token = new Token("left", left);
        var blocker = new MutableFact { Value = 2 };
        node.Assert(token);
        node.Assert(blocker);
        Assert.Empty(capture.Assertions);

        blocker.Value = 1;
        node.Refresh(blocker, nameof(MutableFact.Value));
        node.Refresh(blocker, nameof(MutableFact.Value));
        Assert.Single(capture.Assertions);

        blocker.Value = 2;
        node.Refresh(blocker, nameof(MutableFact.Value));
        node.Refresh(blocker, nameof(MutableFact.Value));
        Assert.Single(capture.Retractions);
        Assert.Single(capture.Assertions);
    }

    [Fact]
    public void IndexedExists_RefreshReindexesMutableJoinKeysWithoutDuplicateTransitions()
    {
        var node = new IndexedExistsNode("indexed", token => token.Get<MutableFact>("left").Value,
            fact => ((MutableFact)fact).Value);
        var capture = new RecordingNode();
        node.AddSuccessor(capture);

        var left = new MutableFact { Value = 1 };
        var token = new Token("left", left);
        var right = new MutableFact { Value = 2 };
        node.Assert(token);
        node.Assert(right);
        Assert.Empty(capture.Assertions);

        right.Value = 1;
        node.Refresh(right, nameof(MutableFact.Value));
        node.Refresh(right, nameof(MutableFact.Value));
        Assert.Single(capture.Assertions);

        left.Value = 2;
        node.Refresh(token, nameof(MutableFact.Value));
        Assert.Single(capture.Retractions);

        right.Value = 2;
        node.Refresh(right, nameof(MutableFact.Value));
        Assert.Equal(2, capture.Assertions.Count);
        Assert.Single(capture.ActiveTokens);
    }

    [Fact]
    public void PropertyChangedSubscription_IsRemovedOnRetractAndNotDuplicatedOnReassert()
    {
        var engine = new ReteEngine.ReteEngine();
        var evaluations = 0;
        engine.Begin("Subscription")
            .Where<ObservableFact>("fact", fact =>
            {
                evaluations++;
                return fact.IsActive;
            })
            .Then(_ => { });

        var fact = new ObservableFact { IsActive = true };
        engine.Assert(fact);
        Assert.Equal(1, evaluations);

        fact.IsActive = false;
        Assert.Equal(2, evaluations);

        engine.Retract(fact);
        fact.IsActive = true;
        Assert.Equal(2, evaluations);

        engine.Assert(fact);
        Assert.Equal(3, evaluations);
        fact.IsActive = false;
        Assert.Equal(4, evaluations);
    }

    [Fact]
    public void PropertyChangedSubscription_IsRemovedWhenEngineIsDisposed()
    {
        var engine = new ReteEngine.ReteEngine();
        var evaluations = 0;
        engine.Begin("DisposeSubscription")
            .Where<ObservableFact>("fact", fact =>
            {
                evaluations++;
                return fact.IsActive;
            })
            .Then(_ => { });

        var fact = new ObservableFact { IsActive = true };
        engine.Assert(fact);
        engine.Dispose();
        fact.IsActive = false;

        Assert.Equal(1, evaluations);
    }

    [Fact]
    public void PropertyChangedPassesPropertyNameAndMaintainsOneHandlerPerWorkingMemoryFact()
    {
        var engine = new ReteEngine.ReteEngine();
        var observer = new RecordingNode();
        engine.Root.AddSuccessor(observer);
        var fact = new ObservableFact { IsActive = true };
        engine.Assert(fact);

        fact.Name = "first-change";
        Assert.Single(observer.Refreshes);
        Assert.Same(fact, observer.Refreshes[0].Fact);
        Assert.Equal(nameof(ObservableFact.Name), observer.Refreshes[0].PropertyName);

        engine.Update(fact, nameof(ObservableFact.Name));
        Assert.Equal(2, observer.Refreshes.Count);
        Assert.Equal(nameof(ObservableFact.Name), observer.Refreshes[1].PropertyName);

        engine.Retract(fact);
        fact.Name = "after-retract";
        Assert.Equal(2, observer.Refreshes.Count);

        engine.Assert(fact);
        fact.Name = "after-reassert";
        Assert.Equal(3, observer.Refreshes.Count);
    }

    [Fact]
    public void Update_ExistingFactRefreshesInPlaceWithoutAddingAnotherSubscription()
    {
        var engine = new ReteEngine.ReteEngine();
        var evaluations = 0;
        engine.Begin("InPlaceUpdate")
            .Where<ObservableFact>("fact", fact =>
            {
                evaluations++;
                return fact.IsActive;
            })
            .Then(_ => { });

        var fact = new ObservableFact { IsActive = true };
        engine.Assert(fact);
        Assert.Same(engine, engine.Update(fact, "UnspecifiedDependency"));
        Assert.Equal(2, evaluations);

        fact.IsActive = false;
        Assert.Equal(3, evaluations);
    }

    [Fact]
    public void Update_ProductCategoryReconcilesMatchesWithoutDuplicateFactsOrSubscriptions()
    {
        var engine = new ReteEngine.ReteEngine();
        var categoryEvaluations = 0;

        engine.Begin("AllProducts")
            .Where<Product>("all-products", _ => true)
            .Then(_ => { });
        engine.Begin("HardwareProducts")
            .Where<Product>("hardware-products", product =>
            {
                categoryEvaluations++;
                return product.Category == "Hardware";
            })
            .Then(_ => { });

        var allProducts = engine.GetAlphaMemory<Product>("all-products");
        var hardwareProducts = engine.GetAlphaMemory<Product>("hardware-products");
        var product = new Product { Category = "Hardware" };
        engine.Assert(product);

        Assert.Single(allProducts.Facts);
        Assert.Single(hardwareProducts.Facts);
        Assert.Equal(1, categoryEvaluations);

        product.Category = "Books";
        Assert.Empty(hardwareProducts.Facts);
        Assert.Equal(2, categoryEvaluations);

        engine.Update(product);
        engine.Update(product);
        Assert.Single(allProducts.Facts);
        Assert.Empty(hardwareProducts.Facts);
        Assert.Equal(4, categoryEvaluations);

        product.Category = "Hardware";
        Assert.Single(allProducts.Facts);
        Assert.Single(hardwareProducts.Facts);
        Assert.Equal(5, categoryEvaluations);

        product.Category = "Books";
        Assert.Single(allProducts.Facts);
        Assert.Empty(hardwareProducts.Facts);
        Assert.Equal(6, categoryEvaluations);
    }

    [Fact]
    public void ExplicitUpdate_RemovesAndRestoresAnAlphaMatch()
    {
        var engine = new ReteEngine.ReteEngine();
        var fireCount = 0;
        engine.Begin("ElectronicsOnly")
            .Where<PlainProduct>("product", product => product.Category == "Electronics")
            .Then(_ => fireCount++);

        var product = new PlainProduct { Category = "Electronics" };
        engine.Assert(product);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        product.Category = "Food";
        engine.Update(product);
        Assert.Empty(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(0, fireCount);

        product.Category = "Electronics";
        engine.Update(product);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void ExplicitUpdate_RemovesAndRestoresAJoinMatchWhenProductIdChanges()
    {
        var engine = new ReteEngine.ReteEngine();
        var matchedProductIds = new List<int>();
        engine.Begin("ProductInventoryJoin")
            .Where<PlainProduct>("product", _ => true)
            .And<PlainInventory>("inventory", (token, inventory) =>
                inventory.ProductId == token.Get<PlainProduct>("product").ProductId)
            .Then(token => matchedProductIds.Add(token.Get<PlainProduct>("product").ProductId));

        var product = new PlainProduct { ProductId = 42 };
        var inventory = new PlainInventory { ProductId = 42 };
        engine.Assert(product, inventory);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        product.ProductId = 43;
        engine.Update(product);
        Assert.Empty(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Empty(matchedProductIds);

        product.ProductId = 42;
        engine.Update(product);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 42 }, matchedProductIds);
    }

    [Fact]
    public void ExplicitUpdate_NotCancelsAndRestoresActivationAsCriticalRiskChanges()
    {
        var engine = new ReteEngine.ReteEngine();
        var processedProductIds = new List<int>();
        engine.Begin("NoCriticalRisk")
            .Where<PlainProduct>("product", _ => true)
            .Not<PlainRiskFactor>("risk", (token, risk) =>
                risk.ProductId == token.Get<PlainProduct>("product").ProductId && risk.Severity == "Critical")
            .Then(token => processedProductIds.Add(token.Get<PlainProduct>("product").ProductId));

        var product = new PlainProduct { ProductId = 73, Category = "Electronics" };
        var risk = new PlainRiskFactor { ProductId = 73, Severity = "Low" };
        engine.Assert(product, risk);

        // A/B: The product initially passes while its related risk is non-critical.
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        // B/C: Making the risk critical blocks Not and cancels the pending activation.
        risk.Severity = "Critical";
        engine.Update(risk);
        Assert.Empty(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Empty(processedProductIds);

        // B/C: Removing the blocker restores Not and allows one new activation.
        risk.Severity = "Low";
        engine.Update(risk);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 73 }, processedProductIds);
    }

    [Fact]
    public void ExplicitUpdate_ExistsPropagatesOnlyWhenTheRightMatchCountCrossesZero()
    {
        var engine = new ReteEngine.ReteEngine();
        var matchedProductIds = new List<int>();
        engine.Begin("ProductHasInventory")
            .Where<PlainProduct>("product", _ => true)
            .Exists<PlainInventory>("inventory", (token, inventory) =>
                inventory.ProductId == token.Get<PlainProduct>("product").ProductId)
            .Then(token => matchedProductIds.Add(token.Get<PlainProduct>("product").ProductId));

        var product = new PlainProduct { ProductId = 42 };
        var firstInventory = new PlainInventory { ProductId = 42 };
        var secondInventory = new PlainInventory { ProductId = 42 };
        engine.Assert(product, firstInventory, secondInventory);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        // One matching right fact leaves Exists true; it must not duplicate the pending activation.
        firstInventory.ProductId = 99;
        engine.Update(firstInventory);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 42 }, matchedProductIds);

        // Losing the last matching right fact makes Exists false and retracts the downstream match.
        secondInventory.ProductId = 99;
        engine.Update(secondInventory);
        Assert.Empty(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 42 }, matchedProductIds);

        // Restoring one matching right fact makes Exists true and creates a new activation.
        firstInventory.ProductId = 42;
        engine.Update(firstInventory);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 42, 42 }, matchedProductIds);
    }

    [Fact]
    public void Update_UnknownFactAssertsItAndNullFactsAreNoOps()
    {
        var engine = new ReteEngine.ReteEngine();
        var evaluations = 0;
        engine.Begin("UnknownUpdate")
            .Where<ObservableFact>("fact", fact =>
            {
                evaluations++;
                return fact.IsActive;
            })
            .Then(_ => { });

        var fact = new ObservableFact { IsActive = true };
        Assert.Same(engine, engine.Update(fact, nameof(ObservableFact.IsActive)));
        Assert.Equal(1, evaluations);

        fact.IsActive = false;
        Assert.Equal(2, evaluations);

        Assert.Same(engine, engine.Update((object)null!));
        Assert.Same(engine, engine.Update((object[]?)null!));
        Assert.Equal(2, evaluations);
    }

    private sealed class PlainProduct
    {
        public int ProductId { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    private sealed class PlainInventory
    {
        public int ProductId { get; set; }
    }

    private sealed class PlainRiskFactor
    {
        public int ProductId { get; set; }
        public string Severity { get; set; } = string.Empty;
    }

    [Fact]
    public void Update_BatchProcessesEachFactAndReturnsTheEngine()
    {
        var engine = new ReteEngine.ReteEngine();
        var fireCount = 0;
        engine.Begin("BatchUpdate")
            .Where<MutableFact>("fact", fact => fact.Value > 0)
            .Then(_ => fireCount++);

        var first = new MutableFact { Value = 1 };
        var second = new MutableFact { Value = 2 };
        engine.Assert(first, second);
        first.Value = 0;
        second.Value = 0;

        Assert.Same(engine, engine.Update(first, second));
        engine.FireAll();

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void UpdatingMatchAcrossFalseAndTrueTransitionsCancelsThenReactivatesRule()
    {
        var engine = new ReteEngine.ReteEngine();
        var fireCount = 0;
        engine.Begin("Transitions")
            .Where<ObservableFact>("fact", fact => fact.IsActive)
            .Then(_ => fireCount++);

        var fact = new ObservableFact { IsActive = true };
        engine.Assert(fact);
        fact.IsActive = false;
        engine.FireAll();
        Assert.Equal(0, fireCount);

        fact.IsActive = true;
        engine.FireAll();
        Assert.Equal(1, fireCount);

        fact.IsActive = false;
        fact.IsActive = true;
        engine.FireAll();
        Assert.Equal(2, fireCount);
    }

    [Fact]
    public void RefreshingAnUnchangedMatchDoesNotDuplicateItsActivation()
    {
        var engine = new ReteEngine.ReteEngine();
        var fireCount = 0;
        engine.Begin("StableMatch")
            .Where<ObservableFact>("fact", fact => fact.IsActive)
            .Then(_ => fireCount++);

        var fact = new ObservableFact { IsActive = true };
        engine.Assert(fact);
        engine.Refresh(fact, nameof(ObservableFact.IsActive));
        engine.Refresh(fact, nameof(ObservableFact.IsActive));
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        engine.FireAll();
        engine.Refresh(fact, nameof(ObservableFact.IsActive));
        engine.FireAll();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void UpdatingAFiredStillValidMatchSchedulesOneActivationForTheLatestRevision()
    {
        var engine = new ReteEngine.ReteEngine();
        var observedValues = new List<int>();
        engine.Begin("RevisionActivation")
            .Where<MutableFact>("fact", fact => fact.Value > 0)
            .Then(token => observedValues.Add(token.Get<MutableFact>("fact").Value));

        var fact = new MutableFact { Value = 1 };
        engine.Assert(fact);
        engine.FireAll();
        Assert.Equal(new[] { 1 }, observedValues);

        fact.Value = 2;
        engine.Update(fact);
        fact.Value = 3;
        engine.Update(fact);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));

        engine.FireAll();
        Assert.Equal(new[] { 1, 3 }, observedValues);

        fact.Value = 4;
        engine.Update(fact);
        engine.FireAll();
        Assert.Equal(new[] { 1, 3, 4 }, observedValues);
    }

    [Fact]
    public void PendingUpdateUsesLatestValuesAndPostFireUpdateCreatesOneNewRevisionActivation()
    {
        var engine = new ReteEngine.ReteEngine();
        var observedValues = new List<int>();
        engine.Begin("PendingAndFiredRevision")
            .Where<MutableFact>("fact", fact => fact.Value > 0)
            .Then(token => observedValues.Add(token.Get<MutableFact>("fact").Value));

        var fact = new MutableFact { Value = 1 };
        engine.Assert(fact);
        fact.Value = 2;
        engine.Update(fact);
        fact.Value = 3;
        engine.Update(fact);

        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 3 }, observedValues);

        fact.Value = 4;
        engine.Update(fact);
        Assert.Single(engine.Agenda.Activations.Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(new[] { 3, 4 }, observedValues);
    }

    [Fact]
    public void PropertyChangeOnAFiredStillValidMatchCreatesANewRevisionActivation()
    {
        var engine = new ReteEngine.ReteEngine();
        var fireCount = 0;
        engine.Begin("NotifiedRevision")
            .Where<ObservableFact>("fact", fact => fact.IsActive)
            .Then(_ => fireCount++);

        var fact = new ObservableFact { IsActive = true };
        engine.Assert(fact);
        engine.FireAll();
        Assert.Equal(1, fireCount);

        fact.Name = "revision-2";
        engine.FireAll();
        Assert.Equal(2, fireCount);
    }

    private sealed class MutableFact
    {
        public int Value { get; set; }
        public string? Name { get; set; }
    }

    private sealed class ObservableFact : INotifyPropertyChanged
    {
        private bool _isActive;
        private string? _name;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }

        public string? Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class RecordingNode : IReteNode
    {
        public IReteNode? Parent { get; set; }
        public IEnumerable<IReteNode> Successors => Array.Empty<IReteNode>();
        public List<Token> Assertions { get; } = new();
        public List<Token> Retractions { get; } = new();
        public List<Token> ActiveTokens { get; } = new();
        public List<(object Fact, string PropertyName)> Refreshes { get; } = new();

        public void Assert(object fact)
        {
            if (fact is not Token token) return;
            Assertions.Add(token);
            if (!ActiveTokens.Any(active => active.Equals(token))) ActiveTokens.Add(token);
        }

        public void Retract(object fact)
        {
            if (fact is not Token token) return;
            Retractions.Add(token);
            ActiveTokens.RemoveAll(active => active.Equals(token));
        }

        public void Refresh(object fact, string propertyName) => Refreshes.Add((fact, propertyName));
        public void AddSuccessor(IReteNode node) { }
        public void RemoveSuccessor(IReteNode node) { }
        public void DebugPrint(object fact, int level = 0) { }
    }
}
