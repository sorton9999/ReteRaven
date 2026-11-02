using ReteCore;
using ReteEngine;
using ReteProgram;
using Xunit;

namespace ReteTest.Tests;

public class TestPublishAndResume
{
    [Fact]
    public void AutoRefreshOnPropertyChanged_DefaultsToEnabled()
    {
        var engine = new ReteEngine.ReteEngine();
        var fireCount = 0;
        engine.Begin("AutoRefresh")
            .Where<Inventory>("inventory", inventory => inventory.Quantity > 0)
            .Then(_ => fireCount++);

        var inventory = new Inventory { Id = Guid.NewGuid(), Quantity = 0 };
        engine.Assert(inventory);

        inventory.Quantity = 5;

        Assert.Single(engine.Agenda.Activations
            .Where(activation => activation.State == Activation.ActivationStateEnum.Pending));
        engine.FireAll();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void AutoRefreshOnPropertyChanged_CanBeDisabledAndUpdatedExplicitly()
    {
        var engine = new ReteEngine.ReteEngine(new ReteEngineOptions
        {
            AutoRefreshOnPropertyChanged = false
        });
        var fireCount = 0;
        engine.Begin("ExplicitRefresh")
            .Where<Inventory>("inventory", inventory => inventory.Quantity > 0)
            .Then(_ => fireCount++);

        var inventory = new Inventory { Id = Guid.NewGuid(), Quantity = 0 };
        engine.Assert(inventory);

        inventory.Quantity = 5;
        Assert.Empty(engine.Agenda.Activations
            .Where(activation => activation.State == Activation.ActivationStateEnum.Pending));
        engine.FireAll();
        Assert.Equal(0, fireCount);

        engine.Update(inventory);
        Assert.Single(engine.Agenda.Activations
            .Where(activation => activation.State == Activation.ActivationStateEnum.Pending));
        engine.FireAll();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void FactBase_ImplementsIFactForDerivedFacts()
    {
        var payload = new object();
        IFact fact = new DerivedFact
        {
            Id = Guid.NewGuid(),
            Value = payload
        };

        Assert.Equal(typeof(object), fact.DataType);
        Assert.Same(payload, fact.UnderlyingObject);
    }

    [Fact]
    public void FactSnapshot_PreservesStateAndDetachesPropertyChangedSubscribers()
    {
        var original = new DerivedFact
        {
            Id = Guid.NewGuid(),
            Name = "original",
            Value = 42
        };
        var originalNotifications = 0;
        original.PropertyChanged += (_, _) => originalNotifications++;

        var snapshot = Assert.IsType<DerivedFact>(original.Snapshot());
        var snapshotNotifications = 0;
        snapshot.PropertyChanged += (_, _) => snapshotNotifications++;

        snapshot.NotifyChanged();

        Assert.NotSame(original, snapshot);
        Assert.Equal(original.Id, snapshot.Id);
        Assert.Equal(original.Name, snapshot.Name);
        Assert.Equal(original.Value, snapshot.Value);
        Assert.Equal(0, originalNotifications);
        Assert.Equal(1, snapshotNotifications);
    }

    [Fact]
    public void FactChange_AssertCapturesFactSnapshotAndOrigin()
    {
        var inventory = new Inventory { Id = Guid.NewGuid(), Quantity = 12 };
        var origin = new FactChangeOrigin("Inventory", "AssertStock", Guid.NewGuid());

        var change = FactChange.Assert(inventory, origin);
        inventory.Quantity = 0;

        var snapshot = Assert.IsType<Inventory>(change.Fact);
        Assert.Equal(FactChangeTypeEnum.Assert, change.Kind);
        Assert.Equal(inventory.Id, change.FactId);
        Assert.Equal(12, snapshot.Quantity);
        Assert.Equal(origin, change.Origin);
        Assert.Null(change.ExpectedRevision);
    }

    [Fact]
    public void FactChange_UpdateCapturesReplacementAndExpectedRevision()
    {
        var replacement = new Inventory { Id = Guid.NewGuid(), Quantity = 25 };

        var change = FactChange.Update(replacement, expectedRevision: 4);
        replacement.Quantity = 0;

        var snapshot = Assert.IsType<Inventory>(change.Fact);
        Assert.Equal(FactChangeTypeEnum.Update, change.Kind);
        Assert.Equal(replacement.Id, change.FactId);
        Assert.Equal(25, snapshot.Quantity);
        Assert.Equal(4, change.ExpectedRevision);
    }

    [Fact]
    public void FactChange_AssertCapturesGenericFactSnapshot()
    {
        var fact = new Fact<int>(11);
        var change = FactChange.Assert(fact);

        fact.Value = 18;

        var snapshot = Assert.IsType<Fact<int>>(change.Fact);
        Assert.Equal(11, snapshot.Value);
        Assert.Equal(fact.Id, change.FactId);
    }

    [Fact]
    public void FactChange_RetractContainsIdentityAndNoFactSnapshot()
    {
        var factId = Guid.NewGuid();
        var change = FactChange.Retract(factId, expectedRevision: 2);

        Assert.Equal(FactChangeTypeEnum.Retract, change.Kind);
        Assert.Equal(factId, change.FactId);
        Assert.Null(change.Fact);
        Assert.Equal(2, change.ExpectedRevision);
    }

    [Fact]
    public void ThenWithChanges_PublishesBatchAfterSuccessfulAction()
    {
        var engine = new ReteEngine.ReteEngine();
        var input = new Inventory { Id = Guid.NewGuid(), Quantity = 1 };
        var secondInput = new Inventory { Id = Guid.NewGuid(), Quantity = 2 };
        var actionCount = 0;
        var observedFacts = 0;
        engine.Begin("StageChange")
            .Where<Inventory>("inventory")
            .CtxThen(context =>
            {
                Assert.Equal(actionCount, engine.PublishedChangeBatches.Count);
                actionCount++;
                context.Assert(new Fact<int>(42));
                context.Publish();
            });
        engine.Begin("ObserveChange")
            .Where<Fact<int>>("fact", fact => fact.Value == 42)
            .Then(_ => observedFacts++);
        IReadOnlyList<FactChange>? deliveredBatch = null;
        engine.FactChangesPublished += batch => deliveredBatch = batch;

        engine.Assert(input, secondInput);
        Assert.Empty(engine.PublishedChangeBatches);

        engine.FireAll();

        Assert.Equal(2, engine.PublishedChangeBatches.Count);
        Assert.Same(engine.PublishedChangeBatches[^1], deliveredBatch);
        var changes = engine.PublishedChangeBatches.Select(batch => Assert.Single(batch)).ToArray();
        Assert.All(changes, change =>
        {
            Assert.Equal(FactChangeTypeEnum.Assert, change.Kind);
            Assert.Equal("StageChange", change.Origin?.RuleName);
            Assert.NotNull(change.Origin?.ActivationId);
        });
        Assert.NotEqual(changes[0].Origin?.ActivationId, changes[1].Origin?.ActivationId);
        Assert.Equal(2, observedFacts);
    }

    [Fact]
    public void PublishedBatch_AppliesAssertUpdateAndRetractInOrder()
    {
        var engine = new ReteEngine.ReteEngine();
        var input = new Inventory { Id = Guid.NewGuid(), Quantity = 1 };
        var original = new Fact<int>(1);
        var replacement = original.Snapshot();
        replacement.Value = 2;
        var secondReplacement = replacement.Snapshot();
        secondReplacement.Value = 3;
        var observedValues = new List<int>();

        engine.Begin("ApplySequence")
            .Where<Inventory>("inventory")
            .CtxThen(context =>
            {
                context.Assert(original);
                context.Update(replacement, expectedRevision: 0);
                context.Update(secondReplacement, expectedRevision: 1);
                context.Retract(original.Id, expectedRevision: 2);
                context.Publish();
            });
        engine.Begin("ObserveFirstState")
            .Where<Fact<int>>("fact", fact => fact.Value == 1)
            .Then(token => observedValues.Add(token.Get<Fact<int>>("fact").Value));
        engine.Begin("ObserveUpdatedState")
            .Where<Fact<int>>("fact", fact => fact.Value == 2)
            .Then(token => observedValues.Add(token.Get<Fact<int>>("fact").Value));
        engine.Begin("ObserveSecondUpdatedState")
            .Where<Fact<int>>("fact", fact => fact.Value == 3)
            .Then(token => observedValues.Add(token.Get<Fact<int>>("fact").Value));

        engine.Assert(input);
        engine.FireAll();

        Assert.Empty(observedValues);
        var batch = Assert.Single(engine.PublishedChangeBatches);
        Assert.Collection(batch,
            change => Assert.Equal(FactChangeTypeEnum.Assert, change.Kind),
            change => Assert.Equal(FactChangeTypeEnum.Update, change.Kind),
            change => Assert.Equal(FactChangeTypeEnum.Update, change.Kind),
            change => Assert.Equal(FactChangeTypeEnum.Retract, change.Kind));
    }

    [Fact]
    public void PublishedBatch_RejectsStaleRevisionBeforeApplyingChanges()
    {
        var engine = new ReteEngine.ReteEngine();
        var current = new Fact<int>(1);
        var replacement = current.Snapshot();
        replacement.Value = 2;
        var input = new Inventory { Id = Guid.NewGuid(), Quantity = 1 };

        engine.Begin("RejectStaleChange")
            .Where<Inventory>("inventory")
            .CtxThen(context =>
            {
                context.Update(replacement, expectedRevision: 4);
                context.Publish();
            });
        engine.Assert(current, input);

        Assert.Throws<InvalidOperationException>(() => engine.FireAll());

        Assert.Empty(engine.PublishedChangeBatches);
    }

    [Fact]
    public void ThenWithChanges_DiscardsBatchWhenActionThrows()
    {
        var engine = new ReteEngine.ReteEngine();
        var input = new Inventory { Id = Guid.NewGuid(), Quantity = 1 };
        engine.Begin("FailingStage")
            .Where<Inventory>("inventory")
            .CtxThen(context =>
            {
                context.Assert(new Fact<int>(42));
                context.Publish();
                throw new InvalidOperationException("Rule action failed.");
            });
        var publishedEvents = 0;
        engine.FactChangesPublished += _ => publishedEvents++;
        engine.Assert(input);

        Assert.Throws<InvalidOperationException>(() => engine.FireAll());

        Assert.Empty(engine.PublishedChangeBatches);
        Assert.Equal(0, publishedEvents);
    }

    [Fact]
    public void AgendaGroups_DefaultToMainAndFireGroupLeavesOtherGroupsPending()
    {
        var engine = new ReteEngine.ReteEngine();
        engine.DefineAgendaGroup("OTHER");
        var input = new Inventory { Id = Guid.NewGuid(), Quantity = 1 };
        var firedRules = new List<string>();

        engine.Begin("MainRule")
            .Where<Inventory>("inventory")
            .Then(_ => firedRules.Add("MAIN"));
        engine.Begin("OtherRule")
            .AgendaGroup("OTHER")
            .Where<Inventory>("inventory")
            .Then(_ => firedRules.Add("OTHER"));
        engine.Assert(input);

        Assert.All(engine.Agenda.Activations, activation =>
        {
            Assert.Equal(activation.RuleName == "MainRule" ? "MAIN" : "OTHER", activation.AgendaGroupName);
        });

        engine.FireGroup("OTHER");

        Assert.Equal(new[] { "OTHER" }, firedRules);
        Assert.True(engine.Agenda.HasActivationsInGroup("MAIN"));

        engine.FireAll();

        Assert.Equal(new[] { "OTHER", "MAIN" }, firedRules);
    }

    [Fact]
    public void AgendaGroups_FocusPrioritizesGroupAndPreservesPriorityWithinGroup()
    {
        var engine = new ReteEngine.ReteEngine();
        engine.DefineAgendaGroup("URGENT");
        var input = new Inventory { Id = Guid.NewGuid(), Quantity = 1 };
        var firedRules = new List<string>();
        var publishedOrigin = default(FactChangeOrigin?);

        engine.Begin("MainRule")
            .Priority(100)
            .Where<Inventory>("inventory")
            .Then(_ => firedRules.Add("MAIN"));
        engine.Begin("UrgentLowPriority")
            .AgendaGroup("URGENT")
            .Priority(1)
            .Where<Inventory>("inventory")
            .Then(_ => firedRules.Add("URGENT-LOW"));
        engine.Begin("UrgentHighPriority")
            .AgendaGroup("URGENT")
            .Priority(10)
            .Where<Inventory>("inventory")
            .CtxThen(context =>
            {
                firedRules.Add("URGENT-HIGH");
                context.Assert(new Fact<int>(7));
                context.Publish();
            });
        engine.FactChangesPublished += batch => publishedOrigin = Assert.Single(batch).Origin;
        engine.Assert(input);

        engine.FocusAgendaGroup("URGENT").FireAll();

        Assert.Equal(new[] { "URGENT-HIGH", "URGENT-LOW", "MAIN" }, firedRules);
        Assert.Equal("URGENT", publishedOrigin?.AgendaGroup);
        Assert.Equal("MAIN", engine.Agenda.FocusedAgendaGroup);
    }

    [Fact]
    public void AgendaGroups_RejectDuplicateDefinitionsAndUnknownReferences()
    {
        var engine = new ReteEngine.ReteEngine();
        engine.DefineAgendaGroup("Review");

        Assert.Equal("Review", engine.Agenda.ResolveAgendaGroupName("review"));
        Assert.Throws<InvalidOperationException>(() => engine.DefineAgendaGroup("review"));
        Assert.Throws<InvalidOperationException>(() => engine.FocusAgendaGroup("Unknown"));
        Assert.Throws<InvalidOperationException>(() => engine.FireGroup("Unknown"));
        Assert.Throws<InvalidOperationException>(() => engine.RegisterWaitForHook("Unknown", () => true));
        Assert.Throws<InvalidOperationException>(() => engine.Begin("UnknownGroupRule").AgendaGroup("Unknown"));
    }

    [Fact]
    public void WaitForHook_PausesGroupAndResumesWhenReady()
    {
        var engine = new ReteEngine.ReteEngine();
        engine.DefineAgendaGroup("EXTERNAL");
        var input = new Inventory { Id = Guid.NewGuid(), Quantity = 1 };
        var ready = false;
        var fireCount = 0;
        string? firedEventGroup = null;
        TimeSpan? firedEventDuration = null;
        engine.RuleFired += (_, eventArgs) =>
        {
            firedEventGroup = eventArgs.AgendaGroup;
            firedEventDuration = eventArgs.Duration;
        };
        using IDisposable waitHook = engine.RegisterWaitForHook("EXTERNAL", () => ready);

        engine.Begin("WaitForExternal")
            .AgendaGroup("EXTERNAL")
            .Where<Inventory>("inventory")
            .Then(_ => fireCount++);
        engine.Assert(input);

        engine.FireGroup("EXTERNAL");
        Assert.Equal(0, fireCount);
        Assert.True(engine.Agenda.HasActivationsInGroup("EXTERNAL"));

        engine.FocusAgendaGroup("EXTERNAL").FireAll();
        Assert.Equal(0, fireCount);
        Assert.Equal("EXTERNAL", engine.Agenda.FocusedAgendaGroup);

        ready = true;
        engine.FireAll();

        Assert.Equal(1, fireCount);
        Assert.Equal("MAIN", engine.Agenda.FocusedAgendaGroup);
        Assert.False(engine.Agenda.HasActivationsInGroup("EXTERNAL"));

        AgendaGroupRuntimeStatistics statistics = Assert.Single(
            engine.GetAgendaGroupRuntimeStatistics().Where(group => group.GroupName == "EXTERNAL"));
        Assert.Equal(1, statistics.ActivationCount);
        Assert.Equal(1, statistics.WaitCount);
        Assert.NotNull(statistics.LastFocusedAt);
        Assert.NotNull(statistics.LastActivationAt);
        Assert.False(statistics.IsWaiting);
        Assert.Equal("EXTERNAL", firedEventGroup);
        Assert.NotNull(firedEventDuration);
        Assert.True(firedEventDuration.Value >= TimeSpan.Zero);
        Assert.Contains(engine.AgendaGroupActivityLog, activity =>
            activity.GroupName == "EXTERNAL" && activity.Activity == "Waiting");
        Assert.Contains(engine.AgendaGroupActivityLog, activity =>
            activity.GroupName == "EXTERNAL" && activity.Activity == "ActivationCompleted");
        Assert.Contains("Total run time:", engine.GetAgendaGroupStatistics());
    }

    private sealed class DerivedFact : Fact
    {
        public void NotifyChanged() => OnPropertyChanged(nameof(Name));
    }
}
