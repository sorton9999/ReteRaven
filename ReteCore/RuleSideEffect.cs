//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace ReteCore
{
    /// <summary>
    /// Captures metadata about an external side effect executed during a rule activation.
    /// </summary>
    public sealed record RuleSideEffect(
        string Category,
        string Description,
        FactChangeOrigin Origin,
        Action? Compensator,
        IReadOnlyDictionary<string, string> Metadata,
        DateTimeOffset OccurredAt)
    {
        /// <summary>
        /// Gets whether an explicit compensator was registered for this side effect.
        /// </summary>
        public bool HasCompensator => Compensator != null;
    }
}

