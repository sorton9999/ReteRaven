// <copyright file="RollbackLifecycleEventArgs.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using ReteCore;

namespace ReteEngine
{
    /// <summary>
    /// Identifies rollback-journal lifecycle stages emitted during publish-resume journaling and
    /// later rollback execution.
    /// </summary>
    public enum RollbackLifecycleStage
    {
        Started,
        Applied,
        Skipped,
        Conflicted,
        Completed
    }

    /// <summary>
    /// Describes conflict disposition for rollback lifecycle events.
    /// </summary>
    public enum RollbackConflictOutcome
    {
        None,
        ConflictDetected,
        FailFastAborted
    }

    /// <summary>
    /// Provides correlation details for rollback lifecycle notifications.
    /// </summary>
    public sealed class RollbackLifecycleEventArgs : EventArgs
    {
        public RollbackLifecycleEventArgs(
            RollbackLifecycleStage stage,
            long epoch,
            long? sequence,
            int? changeIndex,
            Guid? factId,
            string? agendaGroup,
            string? ruleName,
            Guid? activationId,
            FactChangeTypeEnum? kind,
            DateTimeOffset occurredAt,
            RollbackConflictOutcome conflictOutcome = RollbackConflictOutcome.None)
        {
            Stage = stage;
            Epoch = epoch;
            Sequence = sequence;
            ChangeIndex = changeIndex;
            FactId = factId;
            AgendaGroup = agendaGroup;
            RuleName = ruleName;
            ActivationId = activationId;
            Kind = kind;
            OccurredAt = occurredAt;
            ConflictOutcome = conflictOutcome;
        }

        public RollbackLifecycleStage Stage { get; }

        public long Epoch { get; }

        public long? Sequence { get; }

        public int? ChangeIndex { get; }

        public Guid? FactId { get; }

        public string? AgendaGroup { get; }

        public string? RuleName { get; }

        public Guid? ActivationId { get; }

        public FactChangeTypeEnum? Kind { get; }

        public DateTimeOffset OccurredAt { get; }

        public RollbackConflictOutcome ConflictOutcome { get; }
    }
}
