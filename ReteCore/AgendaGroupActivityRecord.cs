//-----------------------------------------------------------------------
// <copyright file="AgendaGroupActivityRecord.cs">
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
    /// Records a timestamped agenda-group event, optionally associated with a rule activation and elapsed time.
    /// </summary>
    public sealed record AgendaGroupActivityRecord(
        DateTimeOffset Timestamp,
        string GroupName,
        string Activity,
        string? RuleName = null,
        TimeSpan? Duration = null);
}
