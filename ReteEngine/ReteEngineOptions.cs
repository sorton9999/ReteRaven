// <copyright file="ReteEngineOptions.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
namespace ReteEngine
{
    /// <summary>
    /// Configures behavior of a ReteEngine instance.
    /// </summary>
    public sealed class ReteEngineOptions
    {
        /// <summary>
        /// Gets or sets whether compatible beta nodes may be shared across rules.
        /// </summary>
        public bool EnableBetaNodeSharing { get; init; } = true;

        /// <summary>
        /// Gets or sets whether the engine subscribes to INotifyPropertyChanged facts
        /// and refreshes the network automatically when those facts change.
        /// When disabled, callers must explicitly call Update or Refresh after a change.
        /// This setting does not prevent the fact object itself from being mutated.
        /// </summary>
        public bool AutoRefreshOnPropertyChanged { get; init; } = true;

        /// <summary>
        /// Gets or sets the rollback boundary mode used by the engine.
        /// The current boundary limits rollback eligibility to explicitly published fact changes.
        /// </summary>
        public RollbackBoundaryMode RollbackBoundaryMode { get; init; } = RollbackBoundaryMode.PublishedFactChangesOnly;

        /// <summary>
        /// Gets or sets the conflict policy used during rollback processing.
        /// </summary>
        public RollbackConflictPolicy RollbackConflictPolicy { get; init; } = RollbackConflictPolicy.FailFast;

        /// <summary>
        /// Gets or sets the idempotency guarantee mode for rollback operations.
        /// </summary>
        public RollbackIdempotencyMode RollbackIdempotencyMode { get; init; } = RollbackIdempotencyMode.Idempotent;
    }
}
