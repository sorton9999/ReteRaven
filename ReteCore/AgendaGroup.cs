//-----------------------------------------------------------------------
// <copyright file="AgendaGroup.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System.Diagnostics;

namespace ReteCore
{
    /// <summary>
    /// Encapsulates pending activations, readiness policies, and runtime diagnostics for one agenda group.
    /// </summary>
    public sealed class AgendaGroup
    {
        private const int MaximumActivityRecords = 1000;
        private readonly List<Activation> _activations = new();
        private readonly List<IAgendaGroupReadinessStrategy> _readinessStrategies = new();
        private readonly Queue<AgendaGroupActivityRecord> _activityLog = new();
        private readonly MutableStatistics _statistics = new();
        private long? _waitStartedAt;

        internal AgendaGroup(string name)
        {
            Name = name;
        }

        internal void ClearActivityLog() => _activityLog.Clear();

        public string Name { get; }

        /// <summary>
        /// Gets or sets whether this group collects timing and activity counters.
        /// </summary>
        public bool CollectStatistics { get; set; } = true;

        /// <summary>
        /// Gets or sets whether this group retains activity-log records.
        /// </summary>
        public bool RecordActivityLog { get; set; } = true;

        /// <summary>
        /// Raised whenever this group's runtime statistics change.
        /// </summary>
        public event Action<AgendaGroupRuntimeStatistics>? StatisticsUpdated;

        public IReadOnlyList<Activation> Activations => _activations.AsReadOnly();

        public IReadOnlyList<AgendaGroupActivityRecord> ActivityLog => Array.AsReadOnly(_activityLog.ToArray());

        public bool HasActivations => _activations.Any(activation => activation.State == Activation.ActivationStateEnum.Pending);

        public IDisposable RegisterReadinessStrategy(IAgendaGroupReadinessStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _readinessStrategies.Add(strategy);
            RecordActivity("WaitHookRegistered");
            return new ReadinessRegistration(this, strategy);
        }

        internal void Add(Activation activation)
        {
            _activations.Add(activation);
            _activations.Sort();
        }

        internal bool HasPendingMatch(Func<Activation, bool> predicate) =>
            _activations.Any(activation => activation.State == Activation.ActivationStateEnum.Pending && predicate(activation));

        internal int RemoveActivations(Predicate<Activation> predicate) => _activations.RemoveAll(predicate);

        internal void RecordFocus(string activity)
        {
            DateTimeOffset? timestamp = CollectStatistics || RecordActivityLog ? DateTimeOffset.UtcNow : null;
            if (CollectStatistics)
            {
                _statistics.FocusCount++;
                _statistics.LastFocusedAt = timestamp;
            }
            RecordActivity(activity, timestamp: timestamp);
            if (CollectStatistics) NotifyStatisticsUpdated();
        }

        internal void RecordActivity(string activity, string? ruleName = null, TimeSpan? duration = null, DateTimeOffset? timestamp = null)
        {
            if (!RecordActivityLog) return;
            if (_activityLog.Count == MaximumActivityRecords) _activityLog.Dequeue();
            _activityLog.Enqueue(new AgendaGroupActivityRecord(timestamp ?? DateTimeOffset.UtcNow, Name, activity, ruleName, duration));
        }

        internal bool CanResume()
        {
            foreach (IAgendaGroupReadinessStrategy strategy in _readinessStrategies.ToArray())
            {
                if (!strategy.CanResume())
                {
                    if (!_waitStartedAt.HasValue && (CollectStatistics || RecordActivityLog))
                    {
                        _waitStartedAt = Stopwatch.GetTimestamp();
                        if (CollectStatistics) _statistics.WaitCount++;
                        RecordActivity("Waiting");
                        if (CollectStatistics) NotifyStatisticsUpdated();
                    }
                    return false;
                }
            }

            if (_waitStartedAt.HasValue)
            {
                TimeSpan waitDuration = Stopwatch.GetElapsedTime(_waitStartedAt.Value);
                _waitStartedAt = null;
                if (CollectStatistics) _statistics.TotalWaitTime += waitDuration;
                RecordActivity("WaitResumed", duration: waitDuration);
                if (CollectStatistics) NotifyStatisticsUpdated();
            }
            return true;
        }

        internal bool Drain(Func<Activation, bool> fire, Func<bool> shouldContinue)
        {
            while (true)
            {
                List<Activation> pending = _activations
                    .Where(activation => activation.State == Activation.ActivationStateEnum.Pending)
                    .ToList();
                if (pending.Count == 0) return true;
                if (!CanResume()) return false;

                foreach (Activation activation in pending)
                {
                    if (!shouldContinue()) return true;
                    if (activation.State == Activation.ActivationStateEnum.Pending) fire(activation);
                }
            }
        }

        internal AgendaGroupRuntimeStatistics GetRuntimeStatistics()
        {
            bool isWaiting = _waitStartedAt.HasValue;
            TimeSpan currentWaitTime = isWaiting ? Stopwatch.GetElapsedTime(_waitStartedAt!.Value) : TimeSpan.Zero;
            return new AgendaGroupRuntimeStatistics(
                Name,
                _statistics.FocusCount,
                _statistics.ActivationCount,
                _statistics.LastFocusedAt,
                _statistics.LastActivationAt,
                _statistics.TotalRunTime,
                _statistics.WaitCount,
                _statistics.TotalWaitTime,
                isWaiting,
                currentWaitTime);
        }

        internal AgendaGroupActivityRecord Execute(Activation activation)
        {
            bool collectStatistics = CollectStatistics;
            bool recordActivityLog = RecordActivityLog;
            DateTimeOffset? startedAt = collectStatistics || recordActivityLog ? DateTimeOffset.UtcNow : null;
            if (collectStatistics)
            {
                _statistics.ActivationCount++;
                _statistics.LastActivationAt = startedAt;
            }
            RecordActivity("ActivationStarted", activation.RuleName, timestamp: startedAt);
            if (collectStatistics) NotifyStatisticsUpdated();
            Stopwatch? stopwatch = collectStatistics || recordActivityLog ? Stopwatch.StartNew() : null;
            try
            {
                activation.Fire();
            }
            catch
            {
                if (stopwatch != null)
                {
                    stopwatch.Stop();
                    if (collectStatistics) _statistics.TotalRunTime += stopwatch.Elapsed;
                    RecordActivity("ActivationFailed", activation.RuleName, stopwatch.Elapsed);
                    if (collectStatistics) NotifyStatisticsUpdated();
                }
                throw;
            }

            stopwatch?.Stop();
            TimeSpan? duration = stopwatch?.Elapsed;
            if (collectStatistics && duration.HasValue) _statistics.TotalRunTime += duration.Value;
            var record = new AgendaGroupActivityRecord(DateTimeOffset.UtcNow, Name, "ActivationCompleted", activation.RuleName, duration);
            RecordActivity(record.Activity, record.RuleName, record.Duration, record.Timestamp);
            if (collectStatistics) NotifyStatisticsUpdated();
            return record;
        }

        private void NotifyStatisticsUpdated()
        {
            if (StatisticsUpdated is { } handler) handler(GetRuntimeStatistics());
        }

        private void RemoveStrategy(IAgendaGroupReadinessStrategy strategy)
        {
            if (_readinessStrategies.Remove(strategy)) RecordActivity("WaitHookRemoved");
        }

        private sealed class MutableStatistics
        {
            public int FocusCount { get; set; }
            public int ActivationCount { get; set; }
            public DateTimeOffset? LastFocusedAt { get; set; }
            public DateTimeOffset? LastActivationAt { get; set; }
            public TimeSpan TotalRunTime { get; set; }
            public int WaitCount { get; set; }
            public TimeSpan TotalWaitTime { get; set; }
        }

        private sealed class ReadinessRegistration : IDisposable
        {
            private AgendaGroup? _group;
            private readonly IAgendaGroupReadinessStrategy _strategy;

            public ReadinessRegistration(AgendaGroup group, IAgendaGroupReadinessStrategy strategy)
            {
                _group = group;
                _strategy = strategy;
            }

            public void Dispose()
            {
                AgendaGroup? group = _group;
                _group = null;
                group?.RemoveStrategy(_strategy);
            }
        }

        internal sealed class DelegateReadinessStrategy : IAgendaGroupReadinessStrategy
        {
            private readonly Func<bool> _canResume;

            public DelegateReadinessStrategy(Func<bool> canResume) => _canResume = canResume;

            public bool CanResume() => _canResume();
        }
    }
}
