// <copyright file="RollbackBoundaryMode.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
namespace ReteEngine
{
    /// <summary>
    /// Defines the rollback boundary supported by this engine. Rollback only targets fact-change batches
    /// that are explicitly published from a successful rule execution context.
    /// External side effects executed directly inside a rule action are outside this boundary.
    /// </summary>
    public enum RollbackBoundaryMode
    {
        /// <summary>
        /// Only published fact changes are eligible for rollback tracking and compensation.
        /// </summary>
        PublishedFactChangesOnly
    }

    /// <summary>
    /// Specifies how rollback execution should respond when the current fact state conflicts with
    /// the expected state captured by rollback metadata.
    /// </summary>
    public enum RollbackConflictPolicy
    {
        /// <summary>
        /// Stop rollback immediately when the first conflict is detected.
        /// </summary>
        FailFast
    }

    /// <summary>
    /// Defines the idempotency guarantees required for rollback operations.
    /// </summary>
    public enum RollbackIdempotencyMode
    {
        /// <summary>
        /// Repeating the same rollback request is expected to produce the same final fact-base state.
        /// </summary>
        Idempotent
    }
}
