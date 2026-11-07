using ReteCore;
using ReteEngine;
using ReteProgram;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ReteTest.Tests;

public class TestRollback
{
    [Fact]
    public void RollbackContract_DefaultsToPublishedFactChangesFailFastAndIdempotent()
    {
        var options = new ReteEngineOptions();
        var engine = new ReteEngine.ReteEngine();

        Assert.Equal(RollbackBoundaryMode.PublishedFactChangesOnly, options.RollbackBoundaryMode);
        Assert.Equal(RollbackConflictPolicy.FailFast, options.RollbackConflictPolicy);
        Assert.Equal(RollbackIdempotencyMode.Idempotent, options.RollbackIdempotencyMode);

        Assert.Equal(RollbackBoundaryMode.PublishedFactChangesOnly, engine.RollbackBoundaryMode);
        Assert.Equal(RollbackConflictPolicy.FailFast, engine.RollbackConflictPolicy);
        Assert.Equal(RollbackIdempotencyMode.Idempotent, engine.RollbackIdempotencyMode);
        Assert.False(engine.SupportsExternalSideEffectRollback);
    }

    [Fact]
    public void RollbackContract_CanBeConfiguredViaEngineOptions()
    {
        var options = new ReteEngineOptions
        {
            RollbackBoundaryMode = RollbackBoundaryMode.PublishedFactChangesOnly,
            RollbackConflictPolicy = RollbackConflictPolicy.FailFast,
            RollbackIdempotencyMode = RollbackIdempotencyMode.Idempotent
        };

        var engine = new ReteEngine.ReteEngine(options);

        Assert.Equal(RollbackBoundaryMode.PublishedFactChangesOnly, engine.RollbackBoundaryMode);
        Assert.Equal(RollbackConflictPolicy.FailFast, engine.RollbackConflictPolicy);
        Assert.Equal(RollbackIdempotencyMode.Idempotent, engine.RollbackIdempotencyMode);
    }

    [Fact]
    public void RollbackContract_BooleanConstructorPathUsesDefaultBoundaryAndPolicies()
    {
        var engine = new ReteEngine.ReteEngine(enableBetaNodeSharing: false);

        Assert.Equal(RollbackBoundaryMode.PublishedFactChangesOnly, engine.RollbackBoundaryMode);
        Assert.Equal(RollbackConflictPolicy.FailFast, engine.RollbackConflictPolicy);
        Assert.Equal(RollbackIdempotencyMode.Idempotent, engine.RollbackIdempotencyMode);
    }

    [Fact]
    public void RollbackContract_IsIndependentFromOtherEngineOptions()
    {
        var options = new ReteEngineOptions
        {
            EnableBetaNodeSharing = false,
            AutoRefreshOnPropertyChanged = false
        };

        var engine = new ReteEngine.ReteEngine(options);

        Assert.Equal(RollbackBoundaryMode.PublishedFactChangesOnly, engine.RollbackBoundaryMode);
        Assert.Equal(RollbackConflictPolicy.FailFast, engine.RollbackConflictPolicy);
        Assert.Equal(RollbackIdempotencyMode.Idempotent, engine.RollbackIdempotencyMode);
    }

    [Fact]
    public void RollbackJournal_RecordsCommittedPublishedBatchWithProvenance()
    {
        var engine = new ReteEngine.ReteEngine();
        engine.Begin("RollbackPublish")
            .Where<Inventory>("inventory")
            .CtxThen(context =>
            {
                context.Assert(new ReteCore.Fact<int>(99));
                context.Publish();
            });

        engine.Assert(new Inventory { Id = Guid.NewGuid(), Quantity = 1 });
        engine.FireAll();

        RollbackJournalEntry entry = Assert.Single(engine.RollbackJournal);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(1, entry.Epoch);
        Assert.Equal("RollbackPublish", entry.RuleName);
        Assert.Equal("MAIN", entry.AgendaGroup);
        Assert.NotNull(entry.ActivationId);

        RollbackJournalChangeEntry change = Assert.Single(entry.Changes);
        Assert.Equal(0, change.Index);
        Assert.Equal(ReteCore.FactChangeTypeEnum.Assert, change.Kind);
        Assert.False(change.ExistedBefore);
        Assert.True(change.ExistsAfter);
        Assert.NotNull(change.PublishedFact);
        Assert.NotNull(change.FactAfter);
    }

    [Fact]
    public void RollbackJournal_CapturesDetachedSnapshotsForPublishedFactChanges()
    {
        var engine = new ReteEngine.ReteEngine();
        var stagedFact = new Inventory { Id = Guid.NewGuid(), Quantity = 5 };

        engine.Begin("DetachSnapshots")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                context.Assert(stagedFact);
                context.Publish();
                stagedFact.Quantity = 777;
            });

        engine.Assert(new ReteCore.Fact<int>(1));
        engine.FireAll();

        RollbackJournalChangeEntry change = Assert.Single(Assert.Single(engine.RollbackJournal).Changes);
        var publishedSnapshot = Assert.IsType<Inventory>(change.PublishedFact);
        var afterSnapshot = Assert.IsType<Inventory>(change.FactAfter);
        Assert.Equal(5, publishedSnapshot.Quantity);
        Assert.Equal(5, afterSnapshot.Quantity);
        Assert.NotSame(stagedFact, publishedSnapshot);
        Assert.NotSame(stagedFact, afterSnapshot);
    }

    [Fact]
    public void RollbackJournal_AssignsMonotonicSequenceAndEpochAcrossPublishCycles()
    {
        var engine = new ReteEngine.ReteEngine();
        engine.Begin("MultiplePublishes")
            .Where<Inventory>("inventory")
            .CtxThen(context =>
            {
                context.Assert(new ReteCore.Fact<int>(1));
                context.Publish();
            });

        engine.Assert(
            new Inventory { Id = Guid.NewGuid(), Quantity = 1 },
            new Inventory { Id = Guid.NewGuid(), Quantity = 2 });
        engine.FireAll();

        Assert.Equal(2, engine.RollbackJournal.Count);
        Assert.Equal(1, engine.RollbackJournal[0].Sequence);
        Assert.Equal(2, engine.RollbackJournal[1].Sequence);
        Assert.Equal(1, engine.RollbackJournal[0].Epoch);
        Assert.Equal(2, engine.RollbackJournal[1].Epoch);
        Assert.True(engine.RollbackJournal[1].CommittedAt >= engine.RollbackJournal[0].CommittedAt);
    }

    [Fact]
    public void RollbackLifecycle_EmitsStartedAppliedAndCompletedWithEpochAndSequence()
    {
        var engine = new ReteEngine.ReteEngine();
        var lifecycle = new List<RollbackLifecycleEventArgs>();
        engine.RollbackLifecycle += (_, eventArgs) => lifecycle.Add(eventArgs);

        engine.Begin("LifecyclePublish")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                context.Assert(new ReteCore.Fact<string>("lifecycle"));
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(1));
        engine.FireAll();

        Assert.Equal(3, lifecycle.Count);
        Assert.Equal(RollbackLifecycleStage.Started, lifecycle[0].Stage);
        Assert.Equal(RollbackLifecycleStage.Applied, lifecycle[1].Stage);
        Assert.Equal(RollbackLifecycleStage.Completed, lifecycle[2].Stage);

        Assert.Equal(lifecycle[0].Epoch, lifecycle[1].Epoch);
        Assert.Equal(lifecycle[1].Epoch, lifecycle[2].Epoch);
        Assert.Null(lifecycle[0].Sequence);
        Assert.Null(lifecycle[1].Sequence);
        Assert.NotNull(lifecycle[2].Sequence);
        Assert.Equal("LifecyclePublish", lifecycle[0].RuleName);
        Assert.Equal("MAIN", lifecycle[0].AgendaGroup);
        Assert.NotNull(lifecycle[0].ActivationId);
        Assert.NotNull(lifecycle[1].FactId);
        Assert.Equal(ReteCore.FactChangeTypeEnum.Assert, lifecycle[1].Kind);

        RollbackJournalEntry journal = Assert.Single(engine.RollbackJournal);
        Assert.Equal(journal.Sequence, lifecycle[2].Sequence);
    }

    [Fact]
    public void RollbackCorrelation_RuleFiredAndActivityLogShareActivationIdWithJournal()
    {
        var engine = new ReteEngine.ReteEngine();
        RuleFiredEventArgs? fired = null;
        engine.RuleFired += (_, eventArgs) => fired = eventArgs;

        engine.Begin("CorrelationPublish")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                context.Assert(new ReteCore.Fact<string>("correlation"));
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(9));
        engine.FireAll();

        Assert.NotNull(fired);
        Assert.NotNull(fired!.ActivationId);

        RollbackJournalEntry journal = Assert.Single(engine.RollbackJournal);
        Assert.Equal(journal.ActivationId, fired.ActivationId);

        AgendaGroupActivityRecord completed = Assert.Single(engine.AgendaGroupActivityLog
            .Where(record => record.Activity == "ActivationCompleted" && record.RuleName == "CorrelationPublish"));
        Assert.Equal(fired.ActivationId, completed.ActivationId);
    }

    [Fact]
    public void RollbackExecution_RollsBackLatestBatchInReverseChangeOrder()
    {
        var engine = new ReteEngine.ReteEngine();
        var lifecycle = new List<RollbackLifecycleEventArgs>();
        engine.RollbackLifecycle += (_, eventArgs) => lifecycle.Add(eventArgs);

        engine.Begin("ReverseRollbackOrder")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                var created = new ReteCore.Fact<string>("v1");
                var updated = created.Snapshot();
                updated.Value = "v2";

                context.Assert(created);
                context.Update(updated, expectedRevision: 0);
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(42));
        engine.FireAll();

        RollbackJournalEntry entry = Assert.Single(engine.RollbackJournal);
        Assert.Equal(2, entry.Changes.Count);

        Assert.True(engine.RollbackLastPublishedBatch());

        var rollbackApplied = lifecycle
            .Where(e => e.Sequence == entry.Sequence && e.Stage == RollbackLifecycleStage.Applied)
            .ToList();

        Assert.Equal(2, rollbackApplied.Count);
        Assert.Equal(1, rollbackApplied[0].ChangeIndex);
        Assert.Equal(0, rollbackApplied[1].ChangeIndex);
    }

    [Fact]
    public void RollbackExecution_PreventsOutOfOrderRollbackAcrossEpochs()
    {
        var engine = new ReteEngine.ReteEngine();

        engine.Begin("EpochRollbackOrder")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                context.Assert(new ReteCore.Fact<string>(Guid.NewGuid().ToString()));
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(1), new ReteCore.Fact<int>(2));
        engine.FireAll();

        Assert.Equal(2, engine.RollbackJournal.Count);
        long firstSequence = engine.RollbackJournal[0].Sequence;
        long latestSequence = engine.RollbackJournal[1].Sequence;

        var ex = Assert.Throws<InvalidOperationException>(() => engine.RollbackPublishedBatch(firstSequence));
        Assert.Contains("Out-of-order rollback", ex.Message);

        Assert.True(engine.RollbackPublishedBatch(latestSequence));
        Assert.True(engine.RollbackPublishedBatch(firstSequence));
    }

    [Fact]
    public void RollbackExecution_FailFastWhenCurrentStateConflictsWithCommittedAfterState()
    {
        var engine = new ReteEngine.ReteEngine();
        var lifecycle = new List<RollbackLifecycleEventArgs>();
        engine.RollbackLifecycle += (_, eventArgs) => lifecycle.Add(eventArgs);

        ReteCore.Fact<string>? projected = null;

        engine.Begin("ConflictRollback")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                projected = new ReteCore.Fact<string>("stable");
                context.Assert(projected);
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(7));
        engine.FireAll();

        RollbackJournalEntry entry = Assert.Single(engine.RollbackJournal);

        projected!.Value = "tampered";
        engine.Update(projected, nameof(ReteCore.Fact<string>.Value));

        Assert.Throws<InvalidOperationException>(() => engine.RollbackLastPublishedBatch());

        Assert.Contains(lifecycle, e =>
            e.Sequence == entry.Sequence &&
            e.Stage == RollbackLifecycleStage.Conflicted);
    }

    [Fact]
    public void RollbackExecution_IdempotentModeSkipsAlreadyRolledBackSequence()
    {
        var engine = new ReteEngine.ReteEngine();
        var lifecycle = new List<RollbackLifecycleEventArgs>();
        engine.RollbackLifecycle += (_, eventArgs) => lifecycle.Add(eventArgs);

        engine.Begin("IdempotentRollback")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                context.Assert(new ReteCore.Fact<string>("once"));
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(11));
        engine.FireAll();

        RollbackJournalEntry entry = Assert.Single(engine.RollbackJournal);
        Assert.True(engine.RollbackPublishedBatch(entry.Sequence));

        lifecycle.Clear();
        Assert.True(engine.RollbackPublishedBatch(entry.Sequence));

        RollbackLifecycleEventArgs skipped = Assert.Single(lifecycle);
        Assert.Equal(RollbackLifecycleStage.Skipped, skipped.Stage);
        Assert.Equal(entry.Sequence, skipped.Sequence);
    }

    [Theory]
    [InlineData("MissingSequence")]
    [InlineData("OutOfOrderSequence")]
    [InlineData("ConflictedCurrentState")]
    public void RollbackFailureModes_MatrixProducesExpectedFailures(string failureMode)
    {
        var engine = new ReteEngine.ReteEngine();

        switch (failureMode)
        {
            case "MissingSequence":
            {
                var missing = Assert.Throws<InvalidOperationException>(() => engine.RollbackPublishedBatch(404));
                Assert.Contains("was not found", missing.Message);
                break;
            }
            case "OutOfOrderSequence":
            {
                engine.Begin("OutOfOrderMatrix")
                    .Where<ReteCore.Fact<int>>("signal")
                    .CtxThen(context =>
                    {
                        context.Assert(new ReteCore.Fact<string>(Guid.NewGuid().ToString()));
                        context.Publish();
                    });

                engine.Assert(new ReteCore.Fact<int>(1), new ReteCore.Fact<int>(2));
                engine.FireAll();

                long first = engine.RollbackJournal[0].Sequence;
                var outOfOrder = Assert.Throws<InvalidOperationException>(() => engine.RollbackPublishedBatch(first));
                Assert.Contains("Out-of-order rollback", outOfOrder.Message);
                break;
            }
            case "ConflictedCurrentState":
            {
                ReteCore.Fact<string>? projected = null;

                engine.Begin("ConflictMatrix")
                    .Where<ReteCore.Fact<int>>("signal")
                    .CtxThen(context =>
                    {
                        projected = new ReteCore.Fact<string>("stable");
                        context.Assert(projected);
                        context.Publish();
                    });

                engine.Assert(new ReteCore.Fact<int>(7));
                engine.FireAll();

                projected!.Value = "tampered";
                engine.Update(projected, nameof(ReteCore.Fact<string>.Value));

                var conflicted = Assert.Throws<InvalidOperationException>(() => engine.RollbackLastPublishedBatch());
                Assert.Contains("Rollback conflict", conflicted.Message);
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported failure-mode test case '{failureMode}'.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollbackFailureModes_ReturnsFalseWhenNoPendingEntry(bool afterRollingBackLatest)
    {
        var engine = new ReteEngine.ReteEngine();

        if (afterRollingBackLatest)
        {
            engine.Begin("NoPendingAfterRollback")
                .Where<ReteCore.Fact<int>>("signal")
                .CtxThen(context =>
                {
                    context.Assert(new ReteCore.Fact<string>("once"));
                    context.Publish();
                });

            engine.Assert(new ReteCore.Fact<int>(1));
            engine.FireAll();

            Assert.True(engine.RollbackLastPublishedBatch());
        }

        Assert.False(engine.RollbackLastPublishedBatch());
    }

    [Fact]
    public void RollbackDeterminism_RepeatedRunsProduceSameJournalAndLifecycleSignature()
    {
        static (string JournalSignature, string LifecycleSignature) RunScenario()
        {
            var engine = new ReteEngine.ReteEngine();
            var lifecycle = new List<RollbackLifecycleEventArgs>();
            engine.RollbackLifecycle += (_, eventArgs) => lifecycle.Add(eventArgs);

            engine.Begin("DeterminismMatrix")
                .Where<ReteCore.Fact<int>>("signal")
                .CtxThen(context =>
                {
                    var created = new ReteCore.Fact<string>("v1");
                    var updated = created.Snapshot();
                    updated.Value = "v2";

                    context.Assert(created);
                    context.Update(updated, expectedRevision: 0);
                    context.Publish();
                });

            engine.Assert(new ReteCore.Fact<int>(99));
            engine.FireAll();

            RollbackJournalEntry entry = Assert.Single(engine.RollbackJournal);
            Assert.True(engine.RollbackPublishedBatch(entry.Sequence));

            var journalSignature = string.Join("|", entry.Changes
                .OrderBy(change => change.Index)
                .Select(change => $"{change.Index}:{change.Kind}:{change.ExistedBefore}->{change.ExistsAfter}:{change.RevisionBefore}->{change.RevisionAfter}"));

            var lifecycleSignature = string.Join(">", lifecycle
                .Where(e => e.Epoch == entry.Epoch)
                .Select(e => $"{e.Stage}:{e.Sequence?.ToString() ?? "-"}:{e.ChangeIndex?.ToString() ?? "-"}:{e.Kind?.ToString() ?? "-"}"));

            return (journalSignature, lifecycleSignature);
        }

        var first = RunScenario();
        var second = RunScenario();
        var third = RunScenario();

        Assert.Equal(first.JournalSignature, second.JournalSignature);
        Assert.Equal(second.JournalSignature, third.JournalSignature);
        Assert.Equal(first.LifecycleSignature, second.LifecycleSignature);
        Assert.Equal(second.LifecycleSignature, third.LifecycleSignature);
    }

    [Fact]
    public void RollbackSideEffects_WithoutCompensatorRemainExternalAndAreAudited()
    {
        var engine = new ReteEngine.ReteEngine();
        var externalCounter = 0;

        engine.Begin("AuditOnlySideEffect")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                externalCounter++;
                context.RegisterSideEffect(
                    category: "Counter",
                    description: "Increment external counter");
                context.Assert(new ReteCore.Fact<string>("audit-only"));
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(1));
        engine.FireAll();

        Assert.Equal(1, externalCounter);
        Assert.True(engine.RollbackLastPublishedBatch());
        Assert.Equal(1, externalCounter);

        RollbackSideEffectAuditEntry sideEffect = Assert.Single(engine.RollbackSideEffectAudit);
        Assert.Equal("Counter", sideEffect.Category);
        Assert.Equal("AuditOnlySideEffect", sideEffect.RuleName);
        Assert.NotNull(sideEffect.Sequence);
        Assert.Equal(RollbackSideEffectDisposition.NonRollbackable, sideEffect.Disposition);
    }

    [Fact]
    public void RollbackSideEffects_WithCompensatorAreCompensatedOnRollback()
    {
        var engine = new ReteEngine.ReteEngine();
        var externalQueue = new List<string>();

        engine.Begin("CompensatedSideEffect")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                externalQueue.Add("sent");
                context.RegisterSideEffect(
                    category: "Notification",
                    description: "Send external notification",
                    compensator: () => externalQueue.RemoveAt(externalQueue.Count - 1),
                    metadata: new Dictionary<string, string>
                    {
                        ["channel"] = "email",
                        ["severity"] = "info"
                    });
                context.Assert(new ReteCore.Fact<string>("compensated"));
                context.Publish();
            });

        engine.Assert(new ReteCore.Fact<int>(2));
        engine.FireAll();

        Assert.Single(externalQueue);
        Assert.True(engine.RollbackLastPublishedBatch());
        Assert.Empty(externalQueue);

        RollbackSideEffectAuditEntry sideEffect = Assert.Single(engine.RollbackSideEffectAudit);
        Assert.True(sideEffect.HasCompensator);
        Assert.Equal(RollbackSideEffectDisposition.Compensated, sideEffect.Disposition);
        Assert.Equal("email", sideEffect.Metadata["channel"]);
        Assert.NotNull(sideEffect.DisposedAt);
    }

    [Fact]
    public void RollbackSideEffects_AreAuditedWithoutSequenceWhenNoPublishOccurs()
    {
        var engine = new ReteEngine.ReteEngine();

        engine.Begin("SideEffectWithoutPublish")
            .Where<ReteCore.Fact<int>>("signal")
            .CtxThen(context =>
            {
                context.RegisterSideEffect(
                    category: "ExternalApi",
                    description: "Called external endpoint without publish");
            });

        engine.Assert(new ReteCore.Fact<int>(3));
        engine.FireAll();

        RollbackSideEffectAuditEntry sideEffect = Assert.Single(engine.RollbackSideEffectAudit);
        Assert.Equal("SideEffectWithoutPublish", sideEffect.RuleName);
        Assert.Null(sideEffect.Sequence);
        Assert.Null(sideEffect.Epoch);
        Assert.Equal(RollbackSideEffectDisposition.NonRollbackable, sideEffect.Disposition);
    }
}
