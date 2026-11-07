// <copyright file="RollbackRecords.cs">
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
    /// Captures per-change provenance and before/after fact state for one committed published fact change.
    /// </summary>
    public sealed record RollbackJournalChangeEntry(
        int Index,
        FactChangeTypeEnum Kind,
        Guid FactId,
        long? ExpectedRevision,
        FactChangeOrigin? Origin,
        IFact? PublishedFact,
        bool ExistedBefore,
        long RevisionBefore,
        IFact? FactBefore,
        bool ExistsAfter,
        long RevisionAfter,
        IFact? FactAfter);

    /// <summary>
    /// Represents one committed publish-resume batch in the rollback journal.
    /// </summary>
    public sealed record RollbackJournalEntry(
        long Sequence,
        long Epoch,
        DateTimeOffset CommittedAt,
        string? AgendaGroup,
        string? RuleName,
        Guid? ActivationId,
        IReadOnlyList<RollbackJournalChangeEntry> Changes);
}
