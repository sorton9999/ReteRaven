//-----------------------------------------------------------------------
// <copyright file="AgendaGroupRuntimeStatistics.cs">
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
    /// Provides a snapshot of timing and activity counters for one agenda group.
    /// </summary>
    public sealed record AgendaGroupRuntimeStatistics(
        string GroupName,
        int FocusCount,
        int ActivationCount,
        DateTimeOffset? LastFocusedAt,
        DateTimeOffset? LastActivationAt,
        TimeSpan TotalRunTime,
        int WaitCount,
        TimeSpan TotalWaitTime,
        bool IsWaiting,
        TimeSpan CurrentWaitTime);
}
