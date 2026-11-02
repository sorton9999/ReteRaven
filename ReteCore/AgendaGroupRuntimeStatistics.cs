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
