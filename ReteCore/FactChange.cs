// <copyright file="FactChange.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;

namespace ReteCore
{
    /// <summary>
    /// Describes the operation represented by a fact change.
    /// </summary>
    public enum FactChangeTypeEnum
    {
        Assert,
        Update,
        Retract
    }

    /// <summary>
    /// Identifies the rule execution responsible for creating a fact change.
    /// </summary>
    /// <param name="AgendaGroup">The agenda group that produced the change, if any.</param>
    /// <param name="RuleName">The rule that produced the change, if any.</param>
    /// <param name="ActivationId">The activation that produced the change, if any.</param>
    public readonly record struct FactChangeOrigin(
        string? AgendaGroup,
        string? RuleName,
        Guid? ActivationId);

    /// <summary>
    /// An immutable fact operation. Assert and update changes capture a snapshot when created;
    /// retract changes carry only the stable identity of the fact to remove.
    /// </summary>
    public sealed record FactChange
    {
        private FactChange(
            FactChangeTypeEnum kind,
            Guid factId,
            IFact? fact,
            long? expectedRevision,
            FactChangeOrigin? origin)
        {
            Kind = kind;
            FactId = factId;
            Fact = fact;
            ExpectedRevision = expectedRevision;
            Origin = origin;
        }

        /// <summary>
        /// Gets the operation represented by this change.
        /// </summary>
        public FactChangeTypeEnum Kind { get; }

        /// <summary>
        /// Gets the stable identity of the affected fact.
        /// </summary>
        public Guid FactId { get; }

        /// <summary>
        /// Gets the detached fact snapshot for an assert or update; null for a retract.
        /// </summary>
        public IFact? Fact { get; }

        /// <summary>
        /// Gets the expected current revision for optimistic validation, if specified.
        /// </summary>
        public long? ExpectedRevision { get; }

        /// <summary>
        /// Gets the optional origin of this change for diagnostics and future rollback.
        /// </summary>
        public FactChangeOrigin? Origin { get; }

        /// <summary>
        /// Creates an assert change from a snapshot of the supplied fact.
        /// </summary>
        /// <param name="fact">The fact to assert.</param>
        /// <param name="origin">The optional origin of the change.</param>
        /// <returns>A change containing a snapshot of the fact.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fact"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The fact snapshot does not preserve the source identity.</exception>
        public static FactChange Assert(IFact fact, FactChangeOrigin? origin = null)
        {
            IFact snapshot = CaptureSnapshot(fact);
            return new FactChange(FactChangeTypeEnum.Assert, snapshot.Id, snapshot, null, origin);
        }

        /// <summary>
        /// Creates an update change containing the replacement state for the same logical fact identity.
        /// </summary>
        /// <param name="replacement">The replacement fact state.</param>
        /// <param name="expectedRevision">The optional expected current revision.</param>
        /// <param name="origin">The optional origin of the change.</param>
        /// <returns>A change containing a snapshot of the replacement fact.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedRevision"/> is negative.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The fact snapshot does not preserve the source identity.</exception>
        public static FactChange Update(
            IFact replacement,
            long? expectedRevision = null,
            FactChangeOrigin? origin = null)
        {
            ValidateRevision(expectedRevision);
            IFact snapshot = CaptureSnapshot(replacement);
            return new FactChange(FactChangeTypeEnum.Update, snapshot.Id, snapshot, expectedRevision, origin);
        }

        /// <summary>
        /// Creates a retract change for the specified fact identity.
        /// </summary>
        /// <param name="factId">The stable identity of the fact to retract.</param>
        /// <param name="expectedRevision">The optional expected current revision.</param>
        /// <param name="origin">The optional origin of the change.</param>
        /// <returns>A retract change for the specified fact identity.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedRevision"/> is negative.</exception>
        public static FactChange Retract(
            Guid factId,
            long? expectedRevision = null,
            FactChangeOrigin? origin = null)
        {
            ValidateRevision(expectedRevision);
            return new FactChange(FactChangeTypeEnum.Retract, factId, null, expectedRevision, origin);
        }

        /// <summary>
        /// Captures a snapshot of the supplied fact, ensuring that the snapshot preserves the source identity. Throws an
        /// exception if the snapshot is null or does not match the source identity.
        /// </summary>
        /// <param name="fact">The fact to capture a snapshot of.</param>
        /// <returns>A snapshot of the supplied fact.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the snapshot is null or does not preserve the source identity.</exception>
        private static IFact CaptureSnapshot(IFact fact)
        {
            ArgumentNullException.ThrowIfNull(fact);
            IFact snapshot = fact.Snapshot();
            if (snapshot == null || snapshot.Id != fact.Id)
            {
                throw new InvalidOperationException("A fact snapshot must preserve the source fact identity.");
            }
            return snapshot;
        }

        /// <summary>
        /// Make sure the expected revision is non-negative, throwing an exception if it is not. This method is used to validate the
        /// expected revision parameter for update and retract operations, ensuring that the revision number is valid before proceeding
        /// with the fact change.
        /// </summary>
        /// <param name="expectedRevision">The expected revision number to validate.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the expected revision is negative.</exception>
        private static void ValidateRevision(long? expectedRevision)
        {
            if (expectedRevision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            }
        }
    }
}
