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
        /// <summary>
        /// The maximum number of activity records retained by this group.
        /// </summary>
        private const int MaximumActivityRecords = 1000;
        /// <summary>
        /// The group's pending and completed rule activations.
        /// </summary>
        private readonly List<Activation> _activations = new();
        /// <summary>
        /// Readiness strategies that must all permit firing before the group can resume.
        /// </summary>
        private readonly List<IAgendaGroupReadinessStrategy> _readinessStrategies = new();
        /// <summary>
        /// Recent activity records retained for diagnostics.
        /// </summary>
        private readonly Queue<AgendaGroupActivityRecord> _activityLog = new();
        /// <summary>
        /// Runtime counters and timing totals for this group.
        /// </summary>
        private readonly MutableStatistics _statistics = new();
        /// <summary>
        /// The stopwatch timestamp at which the group entered its current wait period, if any.
        /// </summary>
        private long? _waitStartedAt;

        /// <summary>
        /// Creates an agenda group with the specified canonical name.
        /// </summary>
        /// <param name="name">The name assigned to this group.</param>
        internal AgendaGroup(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Clears retained activity records without resetting the group's runtime counters.
        /// </summary>
        internal void ClearActivityLog() => _activityLog.Clear();

        /// <summary>
        /// Gets the canonical name of this agenda group.
        /// </summary>
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

        /// <summary>
        /// Gets the activations owned by this group, ordered by activation priority.
        /// </summary>
        public IReadOnlyList<Activation> Activations => _activations.AsReadOnly();

        /// <summary>
        /// Gets the activity records currently retained by this group.
        /// </summary>
        public IReadOnlyList<AgendaGroupActivityRecord> ActivityLog => Array.AsReadOnly(_activityLog.ToArray());

        /// <summary>
        /// Gets whether this group contains any pending activations.
        /// </summary>
        public bool HasActivations => _activations.Any(activation => activation.State == Activation.ActivationStateEnum.Pending);

        /// <summary>
        /// Registers a strategy that gates the group's ability to resume firing.
        /// </summary>
        /// <param name="strategy">The readiness strategy to register.</param>
        /// <returns>A registration that removes the strategy when disposed.</returns>
        /// <exception cref="ArgumentNullException">The strategy is null.</exception>
        public IDisposable RegisterReadinessStrategy(IAgendaGroupReadinessStrategy strategy)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            _readinessStrategies.Add(strategy);
            RecordActivity("WaitHookRegistered");
            return new ReadinessRegistration(this, strategy);
        }

        /// <summary>
        /// Adds an activation to this group and restores priority ordering.
        /// </summary>
        /// <param name="activation">The activation to add.</param>
        internal void Add(Activation activation)
        {
            _activations.Add(activation);
            _activations.Sort();
        }

        /// <summary>
        /// Determines whether a pending activation satisfies the supplied predicate.
        /// </summary>
        /// <param name="predicate">The condition to test for each pending activation.</param>
        /// <returns>True when a matching pending activation exists; otherwise false.</returns>
        internal bool HasPendingMatch(Func<Activation, bool> predicate) =>
            _activations.Any(activation => activation.State == Activation.ActivationStateEnum.Pending && predicate(activation));

        /// <summary>
        /// Removes activations that satisfy the supplied predicate.
        /// </summary>
        /// <param name="predicate">The condition an activation must satisfy to be removed.</param>
        /// <returns>The number of activations removed.</returns>
        internal int RemoveActivations(Predicate<Activation> predicate) => _activations.RemoveAll(predicate);

        /// <summary>
        /// Updates focus counters and records the corresponding group activity.
        /// </summary>
        /// <param name="activity">The focus activity to record.</param>
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

        /// <summary>
        /// Adds an activity record when activity logging is enabled.
        /// </summary>
        /// <param name="activity">The activity description.</param>
        /// <param name="ruleName">The associated rule name, if any.</param>
        /// <param name="duration">The elapsed time associated with the activity, if any.</param>
        /// <param name="timestamp">The activity timestamp, or the current time when omitted.</param>
        internal void RecordActivity(string activity, string? ruleName = null, TimeSpan? duration = null, DateTimeOffset? timestamp = null)
        {
            if (!RecordActivityLog) return;
            if (_activityLog.Count == MaximumActivityRecords) _activityLog.Dequeue();
            _activityLog.Enqueue(new AgendaGroupActivityRecord(timestamp ?? DateTimeOffset.UtcNow, Name, activity, ruleName, duration));
        }

        /// <summary>
        /// Evaluates each readiness strategy and tracks the group's wait period when firing is paused.
        /// </summary>
        /// <returns>True when all strategies allow firing; otherwise false.</returns>
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

        /// <summary>
        /// Drains pending activations while the group is ready and the mediator permits firing to continue.
        /// </summary>
        /// <param name="fire">The mediator callback that validates and fires an activation.</param>
        /// <param name="shouldContinue">Determines whether the mediator still permits this group to fire.</param>
        /// <returns>False when a readiness strategy pauses firing; otherwise true.</returns>
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

        /// <summary>
        /// Creates a snapshot of this group's current runtime counters and timing data.
        /// </summary>
        /// <returns>An immutable snapshot of the group's runtime statistics.</returns>
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

        /// <summary>
        /// Executes an activation and records its timing and activity when diagnostics are enabled.
        /// </summary>
        /// <param name="activation">The activation to execute.</param>
        /// <returns>The activity record describing the completed activation.</returns>
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

        /// <summary>
        /// Publishes a fresh statistics snapshot to listeners when statistics collection is enabled.
        /// </summary>
        private void NotifyStatisticsUpdated()
        {
            if (StatisticsUpdated is { } handler) handler(GetRuntimeStatistics());
        }

        /// <summary>
        /// Removes a readiness strategy and records its removal when it was registered.
        /// </summary>
        /// <param name="strategy">The strategy to remove.</param>
        private void RemoveStrategy(IAgendaGroupReadinessStrategy strategy)
        {
            if (_readinessStrategies.Remove(strategy)) RecordActivity("WaitHookRemoved");
        }

        /// <summary>
        /// Holds the mutable runtime counters used to create statistics snapshots.
        /// </summary>
        private sealed class MutableStatistics
        {
            /// <summary>
            /// Gets or sets the number of times this group received focus.
            /// </summary>
            public int FocusCount { get; set; }
            /// <summary>
            /// Gets or sets the number of activations executed by this group.
            /// </summary>
            public int ActivationCount { get; set; }
            /// <summary>
            /// Gets or sets the timestamp of the most recent focus.
            /// </summary>
            public DateTimeOffset? LastFocusedAt { get; set; }
            /// <summary>
            /// Gets or sets the timestamp of the most recent activation.
            /// </summary>
            public DateTimeOffset? LastActivationAt { get; set; }
            /// <summary>
            /// Gets or sets the cumulative activation runtime.
            /// </summary>
            public TimeSpan TotalRunTime { get; set; }
            /// <summary>
            /// Gets or sets the number of times firing was paused by readiness strategies.
            /// </summary>
            public int WaitCount { get; set; }
            /// <summary>
            /// Gets or sets the cumulative time spent waiting.
            /// </summary>
            public TimeSpan TotalWaitTime { get; set; }
        }

        /// <summary>
        /// Disposes a readiness registration by removing its strategy from the group.
        /// </summary>
        private sealed class ReadinessRegistration : IDisposable
        {
            /// <summary>
            /// The group from which the strategy will be removed when this registration is disposed.
            /// </summary>
            private AgendaGroup? _group;
            /// <summary>
            /// The readiness strategy associated with this registration.
            /// </summary>
            private readonly IAgendaGroupReadinessStrategy _strategy;

            /// <summary>
            /// Creates a registration for a readiness strategy in an agenda group.
            /// </summary>
            /// <param name="group">The group that owns the strategy.</param>
            /// <param name="strategy">The registered readiness strategy.</param>
            public ReadinessRegistration(AgendaGroup group, IAgendaGroupReadinessStrategy strategy)
            {
                _group = group;
                _strategy = strategy;
            }

            /// <summary>
            /// Removes the strategy from its group. Repeated disposal has no effect.
            /// </summary>
            public void Dispose()
            {
                AgendaGroup? group = _group;
                _group = null;
                group?.RemoveStrategy(_strategy);
            }
        }

        internal sealed class DelegateReadinessStrategy : IAgendaGroupReadinessStrategy
        {
            /// <summary>
            /// The delegate used to determine whether the group can resume.
            /// </summary>
            private readonly Func<bool> _canResume;

            /// <summary>
            /// Creates a readiness strategy backed by a delegate.
            /// </summary>
            /// <param name="canResume">The delegate that checks whether the group can resume.</param>
            public DelegateReadinessStrategy(Func<bool> canResume) => _canResume = canResume;

            /// <summary>
            /// Checks whether the group can resume by invoking the configured delegate.
            /// </summary>
            /// <returns>True when the group can resume; otherwise false.</returns>
            public bool CanResume() => _canResume();
        }
    }
}
