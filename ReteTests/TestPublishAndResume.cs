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
            .Where(activation => activation.State == Activation.ActivationState.Pending));
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
            .Where(activation => activation.State == Activation.ActivationState.Pending));
        engine.FireAll();
        Assert.Equal(0, fireCount);

        engine.Update(inventory);
        Assert.Single(engine.Agenda.Activations
            .Where(activation => activation.State == Activation.ActivationState.Pending));
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

    private sealed class DerivedFact : Fact
    {
        public void NotifyChanged() => OnPropertyChanged(nameof(Name));
    }
}
