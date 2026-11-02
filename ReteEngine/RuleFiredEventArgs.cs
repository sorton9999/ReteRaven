using System;

namespace ReteEngine
{
    /// <summary>
    /// Provides timing and group details for a successfully fired rule activation.
    /// </summary>
    public sealed class RuleFiredEventArgs : EventArgs
    {
        public RuleFiredEventArgs(string agendaGroup, string ruleName, DateTimeOffset firedAt, TimeSpan duration)
        {
            AgendaGroup = agendaGroup;
            RuleName = ruleName;
            FiredAt = firedAt;
            Duration = duration;
        }

        public string AgendaGroup { get; }

        public string RuleName { get; }

        public DateTimeOffset FiredAt { get; }

        public TimeSpan Duration { get; }
    }
}
