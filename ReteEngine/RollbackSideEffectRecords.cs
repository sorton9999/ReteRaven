//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using ReteCore;

namespace ReteEngine
{
    /// <summary>
    /// Describes compensation state for an externally observed side effect.
    /// </summary>
    public enum RollbackSideEffectDisposition
    {
        NonRollbackable,
        PendingCompensation,
        Compensated,
        CompensationFailed
    }

    /// <summary>
    /// Captures audited metadata for side effects observed during rule execution.
    /// </summary>
    public sealed class RollbackSideEffectAuditEntry
    {
        internal RollbackSideEffectAuditEntry(
            Guid sideEffectId,
            long? epoch,
            long? sequence,
            string? agendaGroup,
            string? ruleName,
            Guid? activationId,
            string category,
            string description,
            bool hasCompensator,
            IReadOnlyDictionary<string, string> metadata,
            DateTimeOffset occurredAt,
            RollbackSideEffectDisposition disposition)
        {
            SideEffectId = sideEffectId;
            Epoch = epoch;
            Sequence = sequence;
            AgendaGroup = agendaGroup;
            RuleName = ruleName;
            ActivationId = activationId;
            Category = category;
            Description = description;
            HasCompensator = hasCompensator;
            Metadata = metadata;
            OccurredAt = occurredAt;
            Disposition = disposition;
        }

        public Guid SideEffectId { get; }

        public long? Epoch { get; internal set; }

        public long? Sequence { get; internal set; }

        public string? AgendaGroup { get; }

        public string? RuleName { get; }

        public Guid? ActivationId { get; }

        public string Category { get; }

        public string Description { get; }

        public bool HasCompensator { get; }

        public IReadOnlyDictionary<string, string> Metadata { get; }

        public DateTimeOffset OccurredAt { get; }

        public RollbackSideEffectDisposition Disposition { get; internal set; }

        public DateTimeOffset? DisposedAt { get; internal set; }

        public string? DispositionDetail { get; internal set; }
    }

    /// <summary>
    /// Lifecycle payload for side-effect audit and compensation events.
    /// </summary>
    public sealed class RollbackSideEffectEventArgs : EventArgs
    {
        public RollbackSideEffectEventArgs(RollbackSideEffectAuditEntry entry)
        {
            Entry = entry;
        }

        public RollbackSideEffectAuditEntry Entry { get; }
    }
}
