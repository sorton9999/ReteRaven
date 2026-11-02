//-----------------------------------------------------------------------
// <copyright file="Agenda.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReteCore
{
    /// <summary>
    /// The Agenda class manages pending rule activations in a Rete-based rule engine. It maintains a list of activations 
    /// that have been triggered but not yet executed.
    /// </summary>
    public class Agenda
    {
        /// <summary>
        /// The default group.  All rules without a group assignment belong to this.
        /// </summary>
        public const string MainAgendaGroup = "MAIN";
        /// <summary>
        /// The maximum records to hold for group activity
        /// </summary>
        private const int MaximumGroupActivityRecords = 1000;
        /// <summary>
        /// The list of pending activations in the agenda. Each activation represents a rule that has been triggered and 
        /// is waiting to be fired. The agenda processes these activations based on their salience (priority) when the 
        /// FireAll method is called, ensuring that higher priority rules are executed before lower priority ones when 
        /// multiple activations are present.
        /// </summary>
        private readonly List<Activation> _activations = new();
        /// <summary>
        /// The list of all registered groups. With no other groups registered, the MAIN group is the only active group.
        /// </summary>
        private readonly Dictionary<string, string> _agendaGroups = new(StringComparer.OrdinalIgnoreCase)
        {
            [MainAgendaGroup] = MainAgendaGroup
        };
        /// <summary>
        /// These are the registered readiness hooks. Each of these provide a hook to create a wait situation for groups
        /// when needed.
        /// </summary>
        private readonly Dictionary<string, List<Func<bool>>> _waitForHooks = new(StringComparer.Ordinal);
        /// <summary>
        /// The list of active groups whose rules are firing first.
        /// </summary>
        private readonly List<string> _agendaGroupFocusStack = new() { MainAgendaGroup };
        /// <summary>
        /// Stores statistics for agenda groups, using case-insensitive string comparison for keys.
        /// </summary>
        private readonly Dictionary<string, MutableAgendaGroupStatistics> _groupStatistics = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Stores a log of group activity records in a queue.
        /// </summary>
        private readonly Queue<AgendaGroupActivityRecord> _groupActivityLog = new();
        /// <summary>
        /// Stores the timestamp (in ticks) when each group began waiting, keyed by group name.
        /// </summary>
        private readonly Dictionary<string, long> _groupWaitStartedAt = new(StringComparer.Ordinal);
        /// <summary>
        /// Stores fired activation keys per fact revision, preventing repeats within one revision while permitting reactivation after updates.
        /// </summary>
        private readonly HashSet<string> _firedActivationsRegistry = new();
        /// <summary>Tracks update revisions by fact instance for revision-aware activation refraction.</summary>
        private readonly Dictionary<object, long> _factRevisions = new(ReferenceEqualityComparer.Instance);
        /// <summary>
        /// This is a list that allows the agenda to generate stable keys for activations that involve reference types, ensuring
        /// that even if the same object instance is used in multiple activations, it can be uniquely identified and tracked. The
        /// table automatically cleans up entries when the associated objects are no longer referenced elsewhere, preventing memory
        /// leaks.
        /// </summary>
        private readonly ConditionalWeakTable<object, ActivationIdentity> _objectIdentities = new();
        /// <summary>
        /// Holds the next unique identity value to assign to an object when generating activation keys. This counter is incremented
        /// each time a new reference type is encountered, ensuring that each object can be uniquely identified in the activation key
        /// generation process.
        /// </summary>
        private long _nextObjectIdentity;

        /// <summary>
        /// Constructor
        /// </summary>
        public Agenda()
        {
            _groupStatistics.Add(MainAgendaGroup, new MutableAgendaGroupStatistics());
            RecordActivity(MainAgendaGroup, "GroupRegistered");
            RecordGroupFocus(MainAgendaGroup, "GroupFocused");
        }

        /// <summary>
        /// Does the agenda currently have any pending activations? This property returns true if there are one or more 
        /// activations in the agenda, indicating that there are rules that have been triggered and are waiting to be 
        /// fired. If this property returns false, it means that there are no pending activations and the agenda is 
        /// currently empty or all activations are in a non-pending state.
        /// </summary>
        public bool HasActivations
        {
            get
            {
                return _activations.Count > 0 &&
                    _activations.Any((a) => a.State == Activation.ActivationStateEnum.Pending);
            }
        }

        /// <summary>
        /// An accessor for the list of pending activations in the agenda. This property returns a read-only enumerable collection 
        /// of Activation objects.
        /// </summary>
        public IEnumerable<Activation> Activations => _activations.AsReadOnly();

        /// <summary>
        /// Gets the agenda group currently receiving firing priority.
        /// </summary>
        public string FocusedAgendaGroup => _agendaGroupFocusStack[^1];

        /// <summary>
        /// Gets the current agenda-group focus stack, from the base group to the focused group.
        /// </summary>
        public IReadOnlyList<string> AgendaGroupFocusStack => _agendaGroupFocusStack.AsReadOnly();

        /// <summary>
        /// Gets the registered agenda-group names.
        /// </summary>
        public IReadOnlyCollection<string> AgendaGroups => _agendaGroups.Values.ToArray();

        /// <summary>
        /// Gets the most recent group-activity records. The log retains at most 1,000 records.
        /// </summary>
        public IReadOnlyList<AgendaGroupActivityRecord> GroupActivityLog =>
            Array.AsReadOnly(_groupActivityLog.ToArray());

        /// <summary>
        /// Raised after a rule activation completes successfully.
        /// </summary>
        public event Action<AgendaGroupActivityRecord>? ActivationFired;

        /// <summary>
        /// Gets a snapshot of timing and activity counters for every registered group.
        /// </summary>
        public IReadOnlyList<AgendaGroupRuntimeStatistics> GetAgendaGroupRuntimeStatistics()
        {
            return _groupStatistics
                .Select(pair => CreateStatisticsSnapshot(pair.Key, pair.Value))
                .OrderBy(statistics => statistics.GroupName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Formats group timing counters and recent activity into a diagnostic report.
        /// </summary>
        public string GetAgendaGroupStatistics()
        {
            var report = new StringBuilder();
            report.AppendLine("Agenda Group Statistics:");
            foreach (AgendaGroupRuntimeStatistics statistics in GetAgendaGroupRuntimeStatistics())
            {
                report.AppendLine($"  {statistics.GroupName}:");
                report.AppendLine($"    Focuses: {statistics.FocusCount}");
                report.AppendLine($"    Rule activations: {statistics.ActivationCount}");
                report.AppendLine($"    Last focused (UTC): {FormatTimestamp(statistics.LastFocusedAt)}");
                report.AppendLine($"    Last activation (UTC): {FormatTimestamp(statistics.LastActivationAt)}");
                report.AppendLine($"    Total run time: {statistics.TotalRunTime.TotalMilliseconds:F3} ms");
                report.AppendLine($"    Waits: {statistics.WaitCount}, total wait: {statistics.TotalWaitTime.TotalMilliseconds:F3} ms" +
                    (statistics.IsWaiting ? $", currently waiting: {statistics.CurrentWaitTime.TotalMilliseconds:F3} ms" : string.Empty));
            }

            report.AppendLine($"Recent Group Activity (last {MaximumGroupActivityRecords} records):");
            foreach (AgendaGroupActivityRecord activity in _groupActivityLog)
            {
                string rule = activity.RuleName == null ? string.Empty : $" rule='{activity.RuleName}'";
                string duration = activity.Duration.HasValue ? $" duration={activity.Duration.Value.TotalMilliseconds:F3} ms" : string.Empty;
                report.AppendLine($"  {activity.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture)} " +
                    $"[{activity.GroupName}] {activity.Activity}{rule}{duration}");
            }
            return report.ToString();
        }

        /// <summary>
        /// Clears the retained activity history without resetting cumulative group counters.
        /// </summary>
        public void ClearGroupActivityLog()
        {
            _groupActivityLog.Clear();
        }

        /// <summary>
        /// Registers a new agenda group. Names are unique without regard to case; `MAIN` is registered by default.
        /// </summary>
        public string DefineAgendaGroup(string groupName)
        {
            string normalizedName = NormalizeAgendaGroupName(groupName);
            if (_agendaGroups.ContainsKey(normalizedName))
            {
                throw new InvalidOperationException($"Agenda group '{normalizedName}' is already defined.");
            }

            _agendaGroups.Add(normalizedName, normalizedName);
            _groupStatistics.Add(normalizedName, new MutableAgendaGroupStatistics());
            RecordActivity(normalizedName, "GroupRegistered");
            return normalizedName;
        }

        /// <summary>
        /// Resolves a registered group name to its canonical spelling, or throws if it is unknown.
        /// </summary>
        public string ResolveAgendaGroupName(string groupName)
        {
            string normalizedName = NormalizeAgendaGroupName(groupName);
            if (!_agendaGroups.TryGetValue(normalizedName, out string? registeredName))
            {
                throw new InvalidOperationException($"Agenda group '{normalizedName}' has not been defined.");
            }
            return registeredName;
        }

        /// <summary>
        /// Places an agenda group on top of the focus stack. The focused group fires before groups below it.
        /// </summary>
        public void FocusAgendaGroup(string groupName)
        {
            groupName = ResolveAgendaGroupName(groupName);
            if (!string.Equals(FocusedAgendaGroup, groupName, StringComparison.Ordinal))
            {
                _agendaGroupFocusStack.Add(groupName);
                RecordGroupFocus(groupName, "GroupFocused");
            }
        }

        /// <summary>
        /// Removes the currently focused group, returning focus to the previous group. The base `MAIN` group cannot be removed.
        /// </summary>
        /// <returns>True when a focused group was removed; otherwise false.</returns>
        public bool PopAgendaGroupFocus()
        {
            if (_agendaGroupFocusStack.Count <= 1) return false;
            string poppedGroup = FocusedAgendaGroup;
            _agendaGroupFocusStack.RemoveAt(_agendaGroupFocusStack.Count - 1);
            RecordActivity(poppedGroup, "FocusPopped");
            RecordGroupFocus(FocusedAgendaGroup, "FocusResumed");
            return true;
        }

        /// <summary>
        /// Determines whether the named group contains pending activations.
        /// </summary>
        public bool HasActivationsInGroup(string groupName)
        {
            groupName = ResolveAgendaGroupName(groupName);
            return _activations.Any(activation =>
                activation.State == Activation.ActivationStateEnum.Pending &&
                string.Equals(activation.AgendaGroupName, groupName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Registers a readiness hook for a group. All registered hooks must return true before its pending activations can fire.
        /// </summary>
        /// <param name="groupName">A previously registered agenda group.</param>
        /// <param name="canResume">Returns true when the group is ready to fire.</param>
        /// <returns>A registration that removes the hook when disposed.</returns>
        public IDisposable RegisterWaitForHook(string groupName, Func<bool> canResume)
        {
            ArgumentNullException.ThrowIfNull(canResume);
            groupName = ResolveAgendaGroupName(groupName);
            if (!_waitForHooks.TryGetValue(groupName, out List<Func<bool>>? hooks))
            {
                hooks = new List<Func<bool>>();
                _waitForHooks.Add(groupName, hooks);
            }
            hooks.Add(canResume);
            RecordActivity(groupName, "WaitHookRegistered");
            return new WaitForHookRegistration(this, groupName, canResume);
        }

        /// <summary>
        /// Removes the registered group readiness hook.
        /// </summary>
        /// <param name="groupName">The name associated with the group readiness hook</param>
        /// <param name="canResume">Search and remove hooks matching this state</param>
        private void RemoveWaitForHook(string groupName, Func<bool> canResume)
        {
            if (!_waitForHooks.TryGetValue(groupName, out List<Func<bool>>? hooks)) return;
            if (!hooks.Remove(canResume)) return;
            RecordActivity(groupName, "WaitHookRemoved");
            if (hooks.Count == 0) _waitForHooks.Remove(groupName);
        }

        /// <summary>
        /// Detect whether or not the group can resume.  The hooks associated with the group are obtained
        /// and if it can resume, it return TRUE, otherwise FALSE is returned.
        /// </summary>
        /// <param name="groupName">The group name</param>
        /// <returns>Whether or not the group can resume</returns>
        private bool CanResumeGroup(string groupName)
        {
            if (_waitForHooks.TryGetValue(groupName, out List<Func<bool>>? hooks))
            {
                foreach (Func<bool> canResume in hooks.ToArray())
                {
                    if (!canResume())
                    {
                        if (!_groupWaitStartedAt.ContainsKey(groupName))
                        {
                            _groupWaitStartedAt[groupName] = Stopwatch.GetTimestamp();
                            _groupStatistics[groupName].WaitCount++;
                            RecordActivity(groupName, "Waiting");
                        }
                        return false;
                    }
                }
            }

            if (_groupWaitStartedAt.Remove(groupName, out long waitStartedAt))
            {
                TimeSpan waitDuration = Stopwatch.GetElapsedTime(waitStartedAt);
                _groupStatistics[groupName].TotalWaitTime += waitDuration;
                RecordActivity(groupName, "WaitResumed", duration: waitDuration);
            }
            return true;
        }

        /// <summary>
        /// Adds an activation unless the tuple is already pending or has fired at its current fact revision.
        /// </summary>
        /// <param name="activation">The Activation object to add to the list of activations.</param>
        public void Add(Activation activation)
        {
            activation.AgendaGroupName = ResolveAgendaGroupName(activation.AgendaGroupName);
            string tupleKey = GenerateTupleKey(activation.RuleName, activation.Match);
            if (_activations.Any(existing => existing.State == Activation.ActivationStateEnum.Pending &&
                existing.AgendaGroupName == activation.AgendaGroupName &&
                GenerateTupleKey(existing.RuleName, existing.Match) == tupleKey))
            {
                return;
            }

            string keyFacts = GenerateActivationKey(activation);
            if (_firedActivationsRegistry.Contains(keyFacts))
            {
                Console.WriteLine($"[AGENDA] Skipping activation of rule '{activation.RuleName}' with facts [{keyFacts}] as it has already been fired.");
                return;
            }
            _activations.Add(activation);
            // Ensure activations are sorted by priority
            _activations.Sort();
        }

        /// <summary>
        /// Removes any pending activations from the agenda that are associated with the specified fact. This is typically 
        /// called when a fact is retracted from the working memory, ensuring that any rules that were triggered by that 
        /// fact but have not yet fired are cancelled and will not execute based on outdated information. If a token is
        /// provided, it will remove activations where the token's fact matches the retracted fact.
        /// </summary>
        /// <param name="factOrToken">The fact or token object to remove.</param>
        /// <returns>The number of activations removed from the agenda.</returns>
        public int RemoveByFact(object factOrToken)
        {
            // Remove any activation where the Match (Token) contains the retracted fact
            int removedCount = 0;
            if (factOrToken is Token token)
            {
                removedCount = _activations.RemoveAll(a => a.Match.NamedFacts.Values.Any(f => f == token.Fact));
            }
            else
            {
                removedCount = _activations.RemoveAll(a => a.Match.NamedFacts.Values.Any(f => f == factOrToken));
            }
            if (removedCount > 0)
            {
                Console.WriteLine($"[AGENDA] Cancelled {removedCount} pending activations.");
            }
            return removedCount;
        }

        /// <summary>
        /// Removes pending activations for the specified rule and match.
        /// </summary>
        /// <param name="ruleName">The rule whose activation should be removed.</param>
        /// <param name="match">The match that was retracted.</param>
        /// <returns>The number of activations removed from the agenda.</returns>
        public int RemoveByRuleAndMatch(string ruleName, Token match, string? agendaGroupName = null)
        {
            if (agendaGroupName != null) agendaGroupName = ResolveAgendaGroupName(agendaGroupName);
            string tupleKey = GenerateTupleKey(ruleName, match);
            int removedCount = _activations.RemoveAll(activation =>
                activation.State == Activation.ActivationStateEnum.Pending &&
                activation.RuleName == ruleName &&
                (agendaGroupName == null || activation.AgendaGroupName == agendaGroupName) &&
                GenerateTupleKey(activation.RuleName, activation.Match) == tupleKey);

            if (removedCount > 0)
            {
                Console.WriteLine($"[AGENDA] Cancelled {removedCount} pending activations for rule '{ruleName}'.");
            }
            return removedCount;
        }

        /// <summary>
        /// Removes pending activations for the specified rule that contain the specified fact instance.
        /// </summary>
        /// <param name="ruleName">The rule whose activations should be removed.</param>
        /// <param name="fact">The fact instance associated with the activations.</param>
        /// <returns>The number of activations removed from the agenda.</returns>
        public int RemoveByRuleAndFact(string ruleName, object fact, string? agendaGroupName = null)
        {
            if (agendaGroupName != null) agendaGroupName = ResolveAgendaGroupName(agendaGroupName);
            int removedCount = _activations.RemoveAll(activation =>
                activation.State == Activation.ActivationStateEnum.Pending &&
                activation.RuleName == ruleName &&
                (agendaGroupName == null || activation.AgendaGroupName == agendaGroupName) &&
                activation.Match.NamedFacts.Values.Any(value => ReferenceEquals(value, fact)));

            if (removedCount > 0)
            {
                Console.WriteLine($"[AGENDA] Cancelled {removedCount} pending activations for rule '{ruleName}'.");
            }
            return removedCount;
        }

        /// <summary>
        /// Clears fired-revision markers when a match leaves the network so a later match can activate again.  These are
        /// all detected and removed by group name.
        /// </summary>
        /// <param name="ruleName">The rule associated with the match.</param>
        /// <param name="match">The match whose fired marker should be cleared.</param>
        public void ForgetActivation(string ruleName, Token match, string? agendaGroupName = null)
        {
            if (agendaGroupName != null) agendaGroupName = ResolveAgendaGroupName(agendaGroupName);
            string keyPrefix = $"{GenerateTupleKey(ruleName, match)}|revision:";
            if (agendaGroupName == null)
            {
                _firedActivationsRegistry.RemoveWhere(key =>
                    key.StartsWith(keyPrefix, StringComparison.Ordinal) ||
                    key.StartsWith($"{GenerateTupleKey(ruleName, match)}|group:", StringComparison.Ordinal));
            }
            else if (agendaGroupName == MainAgendaGroup)
            {
                _firedActivationsRegistry.RemoveWhere(key => key.StartsWith(keyPrefix, StringComparison.Ordinal));
            }
            else
            {
                string groupPrefix = $"{GenerateTupleKey(ruleName, match)}|group:{agendaGroupName}|revision:";
                _firedActivationsRegistry.RemoveWhere(key => key.StartsWith(groupPrefix, StringComparison.Ordinal));
            }
        }

        /// <summary>
        /// Advances the revision used to distinguish activations after a fact update. This method is called when a fact
        /// is updated in the working memory, indicating that any previous activations associated with the old state of
        /// the fact should be considered outdated. By incrementing the revision number for the fact, it ensures that
        /// future activations will be treated as new and can be fired again, even if they were previously executed with
        /// an older revision of the fact.
        /// </summary>
        /// <param name="fact">The fact whose revision should be advanced.</param>
        public void AdvanceFactRevision(object fact)
        {
            if (fact == null) return;
            _factRevisions.TryGetValue(fact, out long currentRevision);
            _factRevisions[fact] = currentRevision + 1;
        }

        /// <summary>
        /// Gets the current revision for a fact instance, returning zero when it has not been advanced.
        /// </summary>
        public long GetFactRevision(object fact)
        {
            if (fact == null) return 0;
            return _factRevisions.TryGetValue(fact, out long revision) ? revision : 0;
        }

        /// <summary>
        /// Sets the revision for a fact instance when a logical fact is replaced by a detached snapshot.
        /// </summary>
        public void SetFactRevision(object fact, long revision)
        {
            if (fact == null) return;
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            _factRevisions[fact] = revision;
        }

        /// <summary>
        /// Releases revision state after a fact leaves working memory. This method is called when a fact is retracted from
        /// the working memory, indicating that any revision tracking associated with that fact is no longer needed. By
        /// removing the fact from the revision tracking dictionary, it ensures that future activations will not be
        /// influenced by any previous revisions of the fact, allowing for a clean slate if the fact is later reintroduced
        /// or replaced.
        /// </summary>
        /// <param name="fact">The fact whose revision state should be released.</param>
        public void ReleaseFactRevision(object fact)
        {
            if (fact != null) _factRevisions.Remove(fact);
        }

        /// <summary>
        /// Fires all pending activations in the agenda, executing their associated actions in order of descending 
        /// salience (priority). The method continues to process activations until there are no more pending 
        /// activations left in the agenda.
        /// </summary>
        public void FireAll()
        {
            string groupName;
            do
            {
                groupName = FocusedAgendaGroup;
                bool groupDrained = FireGroupActivations(groupName, honorFocus: true);
                if (!groupDrained) return;
            }
            while (!string.Equals(FocusedAgendaGroup, groupName, StringComparison.Ordinal) ||
                HasActivationsInGroup(groupName) ||
                PopAgendaGroupFocus());
        }

        /// <summary>
        /// Fires pending activations from only the specified group without changing the focus stack.
        /// </summary>
        public void FireGroup(string groupName)
        {
            groupName = ResolveAgendaGroupName(groupName);
            RecordActivity(groupName, "FireGroupRequested");
            FireGroupActivations(groupName, honorFocus: false);
        }

        /// <summary>
        /// The group activation fire method. All pending activations under the given group name will be fired.
        /// The honorFocus flag signifies the group is currently the active group and will fire first.
        /// </summary>
        /// <param name="groupName">The group to fire</param>
        /// <param name="honorFocus">The flag indicating the active group</param>
        /// <returns></returns>
        private bool FireGroupActivations(string groupName, bool honorFocus)
        {
            while (true)
            {
                List<Activation> snapshotToFire = _activations
                    .Where(activation => activation.State == Activation.ActivationStateEnum.Pending &&
                        activation.AgendaGroupName == groupName)
                    .ToList();
                if (snapshotToFire.Count == 0) return true;
                if (!CanResumeGroup(groupName)) return false;

                foreach (Activation activation in snapshotToFire)
                {
                    if (honorFocus && !string.Equals(FocusedAgendaGroup, groupName, StringComparison.Ordinal)) return true;
                    if (activation.State != Activation.ActivationStateEnum.Pending) continue;

                    string keyFacts = GenerateActivationKey(activation);
                    if (_firedActivationsRegistry.Contains(keyFacts))
                    {
                        Console.WriteLine($"[AGENDA] Skipping activation of rule '{activation.RuleName}' with facts [{keyFacts}] as it has already been fired.");
                        activation.State = Activation.ActivationStateEnum.Cancelled;
                        RecordActivity(groupName, "DuplicateActivationSkipped", activation.RuleName);
                        continue;
                    }

                    Console.WriteLine($"[AGENDA] Firing activation of rule '{activation.RuleName}' in group '{groupName}' with facts [{keyFacts}] and salience {activation.Salience}.");
                    activation.State = Activation.ActivationStateEnum.Fired;
                    _firedActivationsRegistry.Add(keyFacts);
                    FireActivation(activation);
                }
            }
        }

        /// <summary>
        /// Fire the given activation collecting statistics along the way.
        /// </summary>
        /// <param name="activation">The activation to fire</param>
        private void FireActivation(Activation activation)
        {
            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            MutableAgendaGroupStatistics statistics = _groupStatistics[activation.AgendaGroupName];
            statistics.ActivationCount++;
            statistics.LastActivationAt = startedAt;
            RecordActivity(activation.AgendaGroupName, "ActivationStarted", activation.RuleName, timestamp: startedAt);

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                activation.Fire();
            }
            catch
            {
                stopwatch.Stop();
                statistics.TotalRunTime += stopwatch.Elapsed;
                RecordActivity(activation.AgendaGroupName, "ActivationFailed", activation.RuleName, stopwatch.Elapsed);
                throw;
            }

            stopwatch.Stop();
            statistics.TotalRunTime += stopwatch.Elapsed;
            AgendaGroupActivityRecord activity = RecordActivity(
                activation.AgendaGroupName,
                "ActivationCompleted",
                activation.RuleName,
                stopwatch.Elapsed);
            ActivationFired?.Invoke(activity);
        }

        /// <summary>
        /// Cancels any activations associated with the given fact.  Any activation that has a state of Pending
        /// is changed to Cancelled if the given fact is associated with it.
        /// </summary>
        /// <param name="retractedFact">Activations associated with this fact are cancelled.</param>
        public void CancelActivationsByFact(object retractedFact)
        {
            if (retractedFact == null) return;

            foreach (var activation in _activations)
            {
                // Only care about activations that haven't fired yet
                if (activation.State == Activation.ActivationStateEnum.Pending)
                {
                    // Inspect the Token's wrapped collection of domain facts.
                    // If this activation contains a reference to the fact being deleted, kill it!
                    if (activation.Match.NamedFacts.Values.Any(f => ReferenceEquals(f, retractedFact) ||
                        f.Equals(retractedFact)))
                    {
                        activation.State = Activation.ActivationStateEnum.Cancelled;
                        Console.WriteLine($"[TM_RETRACT] Automatically cancelled pending activation for rule '{activation.RuleName}' due to fact retraction.");
                    }
                }
            }
        }

        /// <summary>
        /// Removes all pending activations from the agenda that are associated with the specified rule name.
        /// </summary>
        /// <param name="ruleName">The name of the rule whose associated activations should be removed.</param>
        /// <returns>True if any activations were removed; otherwise, false.</returns>
        public bool RemoveActivationsByRule(string ruleName)
        {
            return _activations.RemoveAll(a => a.RuleName == ruleName) > 0;
        }

        /// <summary>
        /// Generates a key from rule, match identity, and the current revisions of its facts.
        /// </summary>
        /// <param name="activation">The activation for which to generate a key.</param>
        /// <returns>A unique key representing the activation.</returns>
        public string GenerateActivationKey(Activation activation)
        {
            // Build a textual representation for each named fact:
            // - If the fact is a Fact, use its Id.
            // - If the fact is a Token, use the token's ToString().
            // - If the fact is an enumerable, join its element representations.
            // - Otherwise fall back to the fact's ToString() (or "null").
            
            return GenerateActivationKey(activation.RuleName, activation.Match, activation.AgendaGroupName);
        }

        /// <summary>
        /// Builds a deduplication key using stable identities for reference-type facts. 
        /// </summary>
        /// <param name="ruleName">The name of the rule.</param>
        /// <param name="match">The token containing named facts.</param>
        /// <returns>A unique key representing the activation.</returns>
        private string GenerateActivationKey(string ruleName, Token match, string agendaGroupName)
        {
            string revisionParts = string.Join(",", match.NamedFacts
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={GetRevisionKey(pair.Value)}"));
            string groupPart = agendaGroupName == MainAgendaGroup ? string.Empty : $"|group:{agendaGroupName}";
            return $"{GenerateTupleKey(ruleName, match)}{groupPart}|revision:{revisionParts}";
        }

        /// <summary>
        /// Generates a stable tuple key for an activation based on the rule name and the named facts in the match token.
        /// </summary>
        /// <param name="ruleName">The name of the rule.</param>
        /// <param name="match">The token containing named facts.</param>
        /// <returns>A unique key representing the activation.</returns>
        private string GenerateTupleKey(string ruleName, Token match)
        {
            string FormatValue(object? value)
            {
                if (value == null) return "null";
                if (value is Token token)
                {
                    return FormatNamedFacts(token.NamedFacts);
                }
                if (value is IFact fact)
                {
                    return $"{value.GetType().FullName}#fact:{fact.Id}";
                }
                if (value is System.Collections.IEnumerable enumerable && value is not string)
                {
                    var parts = new List<string>();
                    foreach (var item in enumerable) parts.Add(FormatValue(item));
                    return $"[{string.Join(";", parts)}]";
                }
                if (value is string || value.GetType().IsValueType)
                {
                    string formatted = value is IFormattable formattable
                        ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
                        : value.ToString() ?? string.Empty;
                    return $"{value.GetType().FullName}:{formatted}";
                }

                var identity = _objectIdentities.GetValue(value,
                    _ => new ActivationIdentity(Interlocked.Increment(ref _nextObjectIdentity)));
                return $"{value.GetType().FullName}#ref:{identity.Value}";
            }

            string FormatNamedFacts(IEnumerable<KeyValuePair<string, object>> namedFacts) => string.Join(",",
                namedFacts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={FormatValue(pair.Value)}"));

            return $"{ruleName}_{FormatNamedFacts(match.NamedFacts)}";
        }

        /// <summary>
        /// Gets a stable revision key for a given value, which can be a fact, token, enumerable, or reference type.
        /// This key is used to track the revision state of facts in the agenda, ensuring that activations are only
        /// fired for the current revision of each fact. For reference types, a unique identity is generated and
        /// stored in a ConditionalWeakTable to maintain stability across activations.
        /// </summary>
        /// <param name="value">The value for which to get the revision key.</param>
        /// <returns>A stable revision key representing the current revision of the value.</returns>
        private string GetRevisionKey(object? value)
        {
            if (value == null || value is string || value.GetType().IsValueType) return "0";
            if (value is Token token)
            {
                return string.Join(",", token.NamedFacts
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={GetRevisionKey(pair.Value)}"));
            }
            if (value is System.Collections.IEnumerable enumerable)
            {
                return $"[{string.Join(";", enumerable.Cast<object?>().Select(GetRevisionKey))}]";
            }

            return _factRevisions.TryGetValue(value, out long revision)
                ? revision.ToString(CultureInfo.InvariantCulture)
                : "0";
        }

        /// <summary>
        /// Records the activity of group focus
        /// </summary>
        /// <param name="groupName">The name of the group to record</param>
        /// <param name="activity">The activity string</param>
        private void RecordGroupFocus(string groupName, string activity)
        {
            DateTimeOffset timestamp = DateTimeOffset.UtcNow;
            MutableAgendaGroupStatistics statistics = _groupStatistics[groupName];
            statistics.FocusCount++;
            statistics.LastFocusedAt = timestamp;
            RecordActivity(groupName, activity, timestamp: timestamp);
        }

        /// <summary>
        /// Records statistics of the activity of the given group by name.
        /// </summary>
        /// <param name="groupName">The name of the group</param>
        /// <param name="activity">The activity string</param>
        /// <param name="ruleName">The name of the rule this activity is attached to</param>
        /// <param name="duration">The duration of the activity</param>
        /// <param name="timestamp">The start time of the activity</param>
        /// <returns></returns>
        private AgendaGroupActivityRecord RecordActivity(
            string groupName,
            string activity,
            string? ruleName = null,
            TimeSpan? duration = null,
            DateTimeOffset? timestamp = null)
        {
            if (_groupActivityLog.Count == MaximumGroupActivityRecords)
            {
                _groupActivityLog.Dequeue();
            }
            var activityRecord = new AgendaGroupActivityRecord(
                timestamp ?? DateTimeOffset.UtcNow,
                groupName,
                activity,
                ruleName,
                duration);
            _groupActivityLog.Enqueue(activityRecord);
            return activityRecord;
        }

        /// <summary>
        /// Gathers the statistics of the given group by name.
        /// </summary>
        /// <param name="groupName">The name of the group</param>
        /// <param name="statistics">The raw statistics</param>
        /// <returns></returns>
        private AgendaGroupRuntimeStatistics CreateStatisticsSnapshot(
            string groupName,
            MutableAgendaGroupStatistics statistics)
        {
            bool isWaiting = _groupWaitStartedAt.TryGetValue(groupName, out long waitStartedAt);
            TimeSpan currentWaitTime = isWaiting ? Stopwatch.GetElapsedTime(waitStartedAt) : TimeSpan.Zero;
            return new AgendaGroupRuntimeStatistics(
                groupName,
                statistics.FocusCount,
                statistics.ActivationCount,
                statistics.LastFocusedAt,
                statistics.LastActivationAt,
                statistics.TotalRunTime,
                statistics.WaitCount,
                statistics.TotalWaitTime,
                isWaiting,
                currentWaitTime);
        }

        /// <summary>
        /// Returns a formatted timestamp string given a DateTimeOffset
        /// </summary>
        /// <param name="timestamp">The DateTimeOffset timestamp</param>
        /// <returns>The new timestamp format</returns>
        private static string FormatTimestamp(DateTimeOffset? timestamp)
        {
            return timestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture) ?? "never";
        }

        /// <summary>
        /// Readys the specified agenda group name for use.
        /// </summary>
        /// <param name="groupName">The agenda group name to normalize.</param>
        /// <returns>The trimmed agenda group name.</returns>
        /// <exception cref="ArgumentException">Thrown when groupName is null or consists only of white-space characters.</exception>
        private static string NormalizeAgendaGroupName(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                throw new ArgumentException("An agenda group name cannot be null or whitespace.", nameof(groupName));
            }
            return groupName.Trim();
        }

        /// <summary>
        /// The agenda group activity statistics 
        /// </summary>
        private sealed class MutableAgendaGroupStatistics
        {
            public int FocusCount { get; set; }
            public int ActivationCount { get; set; }
            public DateTimeOffset? LastFocusedAt { get; set; }
            public DateTimeOffset? LastActivationAt { get; set; }
            public TimeSpan TotalRunTime { get; set; }
            public int WaitCount { get; set; }
            public TimeSpan TotalWaitTime { get; set; }
        }

        /// <summary>
        /// Represents a registration for a wait-for hook in an agenda, ensuring the hook is properly removed when
        /// disposed.
        /// </summary>
        private sealed class WaitForHookRegistration : IDisposable
        {
            /// <summary>
            /// The registered Agenda for this hook
            /// </summary>
            private Agenda? _agenda;
            /// <summary>
            /// The name of the group for this registration
            /// </summary>
            private readonly string _groupName;
            /// <summary>
            /// The registered state of the registered hook
            /// </summary>
            private readonly Func<bool> _canResume;

            /// <summary>
            /// Constructor. Registers the named group with the corresponding Agenda and state
            /// </summary>
            /// <param name="agenda">The Agenda to register</param>
            /// <param name="groupName">The name of the group being registered</param>
            /// <param name="canResume">The state of the registered hook</param>
            public WaitForHookRegistration(Agenda agenda, string groupName, Func<bool> canResume)
            {
                _agenda = agenda;
                _groupName = groupName;
                _canResume = canResume;
            }

            /// <summary>
            /// The dispose method for references to this class.
            /// </summary>
            public void Dispose()
            {
                Agenda? agenda = _agenda;
                _agenda = null;
                agenda?.RemoveWaitForHook(_groupName, _canResume);
            }
        }

        /// <summary>
        /// The ActivationIdentity record is used to assign a unique identity to reference-type objects for the purpose
        /// of generating stable activation keys. Each time a new reference type is encountered, it is assigned a unique
        /// long value that can be used to identify it in the activation key generation process. This ensures that even
        /// if the same object instance is used in multiple activations, it can be uniquely identified and tracked.
        /// </summary>
        /// <param name="Value">The value of the unique identity.</param>
        private sealed record ActivationIdentity(long Value);
    }
}
