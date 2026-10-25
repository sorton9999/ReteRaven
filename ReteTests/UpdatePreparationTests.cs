using System.ComponentModel;
using ReteCore;
using ReteEngine;
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

    private sealed class MutableFact
    {
        public int Value { get; set; }
    }

    private sealed class ObservableFact : INotifyPropertyChanged
    {
        private bool _isActive;

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

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class RecordingNode : IReteNode
    {
        public IReteNode? Parent { get; set; }
        public IEnumerable<IReteNode> Successors => Array.Empty<IReteNode>();
        public List<Token> Assertions { get; } = new();
        public List<Token> Retractions { get; } = new();
        public List<Token> ActiveTokens { get; } = new();

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

        public void Refresh(object fact, string propertyName) { }
        public void AddSuccessor(IReteNode node) { }
        public void RemoveSuccessor(IReteNode node) { }
        public void DebugPrint(object fact, int level = 0) { }
    }
}
