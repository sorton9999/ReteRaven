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
