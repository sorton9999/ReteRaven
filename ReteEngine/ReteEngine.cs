//-----------------------------------------------------------------------
// <copyright file="ReteEngine.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReteCore;

namespace ReteEngine
{
    /// <summary>
    /// The ReteEngine class represents the core of a Rete-based rule engine, responsible for managing the Rete network, working memory, 
    /// and agenda. It provides methods for asserting, retracting, and refreshing facts, as well as firing rules based on the current 
    /// state of the network. The ReteEngine maintains a root node for the Rete network, an agenda for pending activations, and a working 
    /// memory to store asserted facts. It also allows for dynamic modification of the network, such as adding terminal nodes and removing 
    /// rules. The ReteEngine serves as the main interface for interacting with the Rete-based rule engine, enabling users to define rules,
    /// manage facts, and execute the inference process effectively.
    /// </summary>
    public class ReteEngine : IDisposable
    {
        /// <summary>
        /// The root node of the Rete network. This node serves as the entry point for all facts asserted into the network. It is responsible 
        /// for propagating facts to its successor nodes, which may include ObjectTypeNodes, AlphaCondition Nodes, and ultimately TerminalNodes 
        /// that represent the conditions of rules. The RootNode is a special type of node that does not perform any specific processing on the 
        /// facts but simply serves as a starting point for the flow of facts through the network.
        /// </summary>
        private readonly RootNode _root = new();
        /// <summary>
        /// The agenda manages pending rule activations in the Rete-based rule engine. It maintains a list of activations that have been triggered
        /// but not yet executed. The agenda processes these activations based on their salience (priority) when the FireAll method is called, 
        /// ensuring that higher priority rules are executed before lower priority ones when multiple activations are present. The agenda allows 
        /// for efficient management of rule execution, enabling the engine to prioritize and execute rules based on the current state of the 
        /// network and the facts that have been asserted.
        /// </summary>
        private readonly Agenda _agenda = new();
        /// <summary>
        /// The storage for all facts that have been asserted into the Rete network. This working memory allows the engine to keep track of the 
        /// current state of knowledge and ensures that facts are only asserted once. When a fact is asserted, it is added to this list if it is not 
        /// already present, and when a fact is retracted, it is removed from the list. The working memory serves as a central repository for all 
        /// facts that are currently active in the system, allowing the Rete network to efficiently manage and propagate facts to successor nodes as 
        /// needed. Additionally, if a fact implements the INotifyPropertyChanged interface, the engine subscribes to property change notifications to 
        /// automatically refresh the fact in the network when its properties change, ensuring that the engine remains up-to-date with the latest 
        /// information and can trigger rules based on changes to facts over time.
        /// </summary>
        private readonly List<object> _workingMemory = new();
        /// <summary>
        /// Retains property-change handlers so they can be detached when facts leave working memory or when the engine is disposed.
        /// </summary>
        private readonly Dictionary<object, (INotifyPropertyChanged Observable, PropertyChangedEventHandler Handler)> _propertyChangedHandlers =
            new(ReferenceEqualityComparer.Instance);
        /// <summary>
        /// The list of terminal nodes in the Rete network. Terminal nodes represent the conditions of rules and are responsible for creating activations 
        /// when their conditions are met. These activations are then managed by the agenda, which determines the order of rule execution based on salience.
        /// Terminal nodes are stored in this list to allow for easy management and potential removal of rules from the network.
        /// </summary>
        private readonly List<TerminalNode> _terminalNodes = new();
        /// <summary>
        /// A list of fact change batches that have been published by successful staged rule actions. Each batch represents a set of changes to facts that
        /// were made during the execution of a rule action and were published to the working memory after the action completed successfully.
        /// </summary>
        private readonly List<IReadOnlyList<FactChange>> _publishedChangeBatches = new();
        /// <summary>
        /// A FIFO list if fact change batches that are pending publication. This queue is used to manage the order of fact changes that need to be applied
        /// to the working memory and network proper.
        /// </summary>
        private readonly Queue<IReadOnlyList<FactChange>> _pendingChangeBatches = new();
        /// <summary>
        /// The alpha registry is a dictionary that maps a tuple of type and name to an AlphaMemory instance. This allows for efficient retrieval of 
        /// alpha memories based on the type and name of the facts they are associated with.
        /// </summary>
        private readonly Dictionary<(Type Type, string Name), object> _alphaRegistry = new();
        /// <summary>
        /// The beta node registry manages caching and sharing of beta memory nodes and join nodes
        /// across multiple rules. This enables significant memory savings when multiple rules share
        /// common prefixes in their condition chains.
        /// </summary>
        private readonly BetaNodeRegistry _betaNodeRegistry = new();
        /// <summary>
        /// Flag to enable or disable beta-node sharing at runtime (for debugging or legacy behavior).
        /// Default is true (sharing enabled).
        /// </summary>
        private bool _enableBetaNodeSharing = true;
        /// <summary>
        /// The flag to control whether the engine automatically subscribes to INotifyPropertyChanged facts and refreshes the network when those facts change.
        /// </summary>
        private bool _autoRefreshOnPropertyChanged = true;
        /// <summary>
        /// The flag to track whether the working memory needs to be replayed through the network. This is set to true when a terminal node is added after
        /// facts have already been asserted, ensuring that all facts are properly evaluated against the new rule conditions.
        /// </summary>
        private bool _needsWorkingMemoryReplay;
        /// <summary>
        /// The flag to track whether the engine is currently applying fact change batches. This is used to prevent re-entrant calls to FireAll while a batch
        /// is being applied, which could lead to inconsistent state or unexpected behavior. When this flag is set to true, any attempts to fire rules will
        /// result in an exception, ensuring that the engine maintains a consistent state during batch application.
        /// </summary>
        private bool _isApplyingChangeBatches;
        /// <summary>
        /// Gets or sets whether per-evaluation diagnostic trace output (e.g. "[DEBUG:...] Result: ...") is written to the
        /// console for conditions and joins built through the fluent API. This is disabled by default: the fluent builder
        /// overloads use <see cref="System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"/> to automatically
        /// capture a human-readable label for every condition/join lambda, but that label capture alone should not cause
        /// console output on every single rule evaluation. Enable this only for interactive debugging, since it adds
        /// significant overhead (console I/O on every fact/token evaluation). Setting this also toggles diagnostic
        /// console tracing in the agenda and beta-node registry.
        /// </summary>
        public bool EnableConditionTraceLogging
        {
            get => _enableConditionTraceLogging;
            set
            {
                _enableConditionTraceLogging = value;
                _agenda.EnableConsoleTraceLogging = value;
                _betaNodeRegistry.EnableConsoleTraceLogging = value;
            }
        }
        private bool _enableConditionTraceLogging;
        /// <summary>
        /// Captures the rollback boundary mode configured for this engine instance.
        /// </summary>
        private readonly RollbackBoundaryMode _rollbackBoundaryMode = RollbackBoundaryMode.PublishedFactChangesOnly;
        /// <summary>
        /// Captures the rollback conflict policy configured for this engine instance.
        /// </summary>
        private readonly RollbackConflictPolicy _rollbackConflictPolicy = RollbackConflictPolicy.FailFast;
        /// <summary>
        /// Captures the rollback idempotency mode configured for this engine instance.
        /// </summary>
        private readonly RollbackIdempotencyMode _rollbackIdempotencyMode = RollbackIdempotencyMode.Idempotent;
        /// <summary>
        /// Stores committed rollback journal entries for published fact-change batches.
        /// </summary>
        private readonly List<RollbackJournalEntry> _rollbackJournal = new();
        /// <summary>
        /// Stores committed metadata for each published fact-change batch.
        /// </summary>
        private readonly List<PublishedBatchMetadataEntry> _publishedBatchMetadata = new();
        /// <summary>
        /// The next rollback epoch identifier assigned to a publish-resume drain cycle.
        /// </summary>
        private long _nextRollbackEpoch;
        /// <summary>
        /// The next rollback sequence value assigned to a committed rollback journal entry.
        /// </summary>
        private long _nextRollbackSequence;
        /// <summary>
        /// Tracks rollback journal sequences that have already been rolled back.
        /// </summary>
        private readonly HashSet<long> _rolledBackSequences = new();
        /// <summary>
        /// Stores side-effect audit entries emitted by context actions.
        /// </summary>
        private readonly List<RollbackSideEffectAuditEntry> _rollbackSideEffectAudit = new();
        /// <summary>
        /// Stores compensator delegates for side effects that explicitly registered one.
        /// </summary>
        private readonly Dictionary<Guid, Action> _sideEffectCompensators = new();
        /// <summary>
        /// A private flag to track whether the ReteEngine has been disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// The accessor for the root node of the Rete network. This property allows external code to access the root node, which serves as the entry point 
        /// for all facts asserted into the network.
        /// </summary>
        public IReteNode Root { get { return _root; } }
        /// <summary>
        /// The accessor for the agenda of the Rete engine. This property allows external code to access the agenda, which manages pending rule activations 
        /// in the rule engine. The agenda maintains a list of activations that have been triggered but not yet executed, and it processes these activations 
        /// based on their salience (priority) when the FireAll method is called.
        /// </summary>
        public Agenda Agenda
        {
            get { return _agenda; }
        }

        /// <summary>
        /// Gets the rollback boundary mode configured for this engine instance.
        /// </summary>
        public RollbackBoundaryMode RollbackBoundaryMode => _rollbackBoundaryMode;

        /// <summary>
        /// Gets the rollback conflict policy configured for this engine instance.
        /// </summary>
        public RollbackConflictPolicy RollbackConflictPolicy => _rollbackConflictPolicy;

        /// <summary>
        /// Gets the rollback idempotency mode configured for this engine instance.
        /// </summary>
        public RollbackIdempotencyMode RollbackIdempotencyMode => _rollbackIdempotencyMode;

        /// <summary>
        /// Gets whether rollback supports compensating external side effects executed inside rule actions.
        /// This value is false because rollback scope is limited to published fact-change batches.
        /// </summary>
        public bool SupportsExternalSideEffectRollback => false;

        /// <summary>
        /// Registers an agenda group for later use by rules and firing operations. `MAIN` is registered by default.
        /// </summary>
        public ReteEngine DefineAgendaGroup(String groupName)
        {
            _agenda.DefineAgendaGroup(groupName);
            return this;
        }

        /// <summary>
        /// Registers a readiness hook for a defined agenda group. Returning false pauses that group's firing until the hook allows it to resume.
        /// </summary>
        public IDisposable RegisterWaitForHook(String groupName, Func<bool> canResume)
        {
            return _agenda.RegisterWaitForHook(groupName, canResume);
        }

        /// <summary>
        /// Gets the change batches published by successful staged rule actions.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<FactChange>> PublishedChangeBatches => _publishedChangeBatches.AsReadOnly();

        /// <summary>
        /// Gets the committed rollback journal entries for published fact-change batches.
        /// </summary>
        public IReadOnlyList<RollbackJournalEntry> RollbackJournal => _rollbackJournal.AsReadOnly();

        /// <summary>
        /// Gets committed metadata for published fact-change batches.
        /// </summary>
        public IReadOnlyList<PublishedBatchMetadataEntry> PublishedBatchMetadata => _publishedBatchMetadata.AsReadOnly();

        /// <summary>
        /// Gets side-effect audit entries observed during context-action execution.
        /// </summary>
        public IReadOnlyList<RollbackSideEffectAuditEntry> RollbackSideEffectAudit => _rollbackSideEffectAudit.AsReadOnly();
        /// <summary>
        /// Raised when a staged rule action successfully publishes a change batch.
        /// </summary>
        public event Action<IReadOnlyList<FactChange>>? FactChangesPublished;

        /// <summary>
        /// Raised when published-batch metadata is committed for audit correlation.
        /// </summary>
        public event Action<PublishedBatchMetadataEntry>? PublishedBatchMetadataCommitted;

        /// <summary>
        /// Raised for rollback lifecycle activity during publish-resume journaling and rollback execution.
        /// </summary>
        public event EventHandler<RollbackLifecycleEventArgs>? RollbackLifecycle;

        /// <summary>
        /// Raised after a rule action completes successfully, including its agenda group and execution duration.
        /// </summary>
        public event EventHandler<RuleFiredEventArgs>? RuleFired;

        /// <summary>
        /// Raised when side effects are observed or compensation state changes.
        /// </summary>
        public event EventHandler<RollbackSideEffectEventArgs>? RollbackSideEffect;

        /// <summary>
        /// Initializes a new instance of the ReteEngine with optional beta-node sharing.
        /// </summary>
        /// <param name="enableBetaNodeSharing">If true (default), rules with identical prefixes share nodes.</param>
        public ReteEngine(bool enableBetaNodeSharing = true)
            : this(new ReteEngineOptions { EnableBetaNodeSharing = enableBetaNodeSharing })
        {
        }

        /// <summary>
        /// Initializes a new instance of the ReteEngine with the specified options.
        /// </summary>
        /// <param name="options">Options controlling engine behavior.</param>
        public ReteEngine(ReteEngineOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _enableBetaNodeSharing = options.EnableBetaNodeSharing;
            _autoRefreshOnPropertyChanged = options.AutoRefreshOnPropertyChanged;
            _rollbackBoundaryMode = options.RollbackBoundaryMode;
            _rollbackConflictPolicy = options.RollbackConflictPolicy;
            _rollbackIdempotencyMode = options.RollbackIdempotencyMode;
            _agenda.ActivationFired += activity => RuleFired?.Invoke(this, new RuleFiredEventArgs(
                activity.GroupName,
                activity.RuleName ?? string.Empty,
                activity.Timestamp,
                activity.Duration ?? TimeSpan.Zero,
                activity.ActivationId));
        }

        /// <summary>
        /// Gets the beta node registry. Returns null if sharing is disabled.
        /// </summary>
        public BetaNodeRegistry? BetaNodeRegistry => _enableBetaNodeSharing ? _betaNodeRegistry : null;

        /// <summary>
        /// Gets diagnostic statistics about beta-node cache performance.
        /// </summary>
        public string GetBetaNodeRegistryStatistics()
        {
            return _betaNodeRegistry.GetStatistics();
        }

        /// <summary>
        /// Gets the agenda-group timing summary and recent group activity log.
        /// </summary>
        public string GetAgendaGroupStatistics()
        {
            return _agenda.GetAgendaGroupStatistics();
        }

        /// <summary>
        /// Gets the most recent agenda-group activity records.
        /// </summary>
        public IReadOnlyList<AgendaGroupActivityRecord> AgendaGroupActivityLog => _agenda.GroupActivityLog;

        /// <summary>
        /// Gets a snapshot of timing and activity counters for each registered agenda group.
        /// </summary>
        public IReadOnlyList<AgendaGroupRuntimeStatistics> GetAgendaGroupRuntimeStatistics()
        {
            return _agenda.GetAgendaGroupRuntimeStatistics();
        }

        /// <summary>
        /// Clears retained agenda-group activity records without resetting cumulative timing counters.
        /// </summary>
        public void ClearAgendaGroupActivityLog()
        {
            _agenda.ClearGroupActivityLog();
        }

        /// <summary>
        /// Clears the beta-node cache (useful for testing or resetting engine state).
        /// </summary>
        public void ClearBetaNodeCache()
        {
            _betaNodeRegistry.Clear();
        }

        /// <summary>
        /// Adds a terminal node to the Rete network. Terminal nodes represent the conditions of rules and are responsible for creating activations when 
        /// their conditions are met. When a terminal node is added, it is stored in the list of terminal nodes for management purposes. 
        /// </summary>
        /// <param name="node">The terminal node to add to the Rete network.</param>
        public void AddTerminalNode(TerminalNode node)
        {
            _terminalNodes.Add(node);
            if (_workingMemory.Count > 0)
            {
                _needsWorkingMemoryReplay = true;
            }
        }

        /// <summary>
        /// The public fact insertion method that introduces facts as a batch Assert.  The private working Assert method is called for each fact in the array.
        /// </summary>
        /// <param name="facts">The array of facts to assert.</param>
        /// <returns>The current instance of the ReteEngine.</returns>
        public ReteEngine Assert(params object[] facts)
        {
            foreach (var fact in facts)
            {
                Assert(fact);
            }
            return this;
        }

        /// <summary>
        /// Adds a fact once, retains its property-change handler, and asserts it into the network.
        /// </summary>
        /// <param name="fact">The fact to assert into the Rete network.</param>
        private void Assert(object fact)
        {
            bool alreadyExists = _workingMemory.Any((f) => ReferenceEquals(f, fact));
            if (!alreadyExists)
            {
                _workingMemory.Add(fact);
                if (!_disposed && _autoRefreshOnPropertyChanged && fact is INotifyPropertyChanged observable)
                {
                    PropertyChangedEventHandler handler = (sender, args) =>
                    {
                        if (sender != null)
                        {
                            _agenda.AdvanceFactRevision(sender);
                            _root.Refresh(sender, args.PropertyName ?? string.Empty);
                        }
                    };
                    _propertyChangedHandlers[fact] = (observable, handler);
                    observable.PropertyChanged += handler;
                }
                _root.Assert(fact);
            }
        }

        /// <summary>
        /// Refreshes a fact in the Rete network, which may trigger re-evaluation of the fact and its propagation to successor nodes if necessary. This 
        /// method is typically called when a property of a fact changes, and it allows the Rete network to update its state based on the new information. 
        /// The Refresh method is crucial for ensuring that the Rete network remains accurate and responsive to changes in facts over time.
        /// </summary>
        /// <param name="fact">The fact to refresh in the Rete network.</param>
        /// <param name="propertyName">The name of the property that changed, if applicable.</param>
        public void Refresh(object fact, string propertyName = null)
        {
            if (fact == null) { return; }
            _root.Refresh(fact, propertyName);
        }

        /// <summary>
        /// The public fact retraction method that removes facts as a batch Retract.  The private working Retract method is called for each fact in the array.
        /// </summary>
        /// <param name="facts">The array of facts to retract.</param>
        /// <returns>The current instance of the ReteEngine.</returns>
        public ReteEngine Retract(params object[] facts)
        {
            foreach (var fact in facts)
            {
                Retract(fact);
            }
            return this;
        }

        /// <summary>
        /// Removes the exact fact instance, detaches its change handler, and retracts it from the network.
        /// </summary>
        /// <param name="fact">The fact to retract from the Rete network.</param>
        private void Retract(object fact)
        {
            int index = _workingMemory.FindIndex(item => ReferenceEquals(item, fact));
            if (index >= 0)
            {
                _workingMemory.RemoveAt(index);
                if (_propertyChangedHandlers.Remove(fact, out var subscription))
                {
                    subscription.Observable.PropertyChanged -= subscription.Handler;
                }
                _agenda.CancelActivationsByFact(fact);
                _agenda.RemoveByFact(fact);
                _root.Retract(fact);
                _agenda.ReleaseFactRevision(fact);
            }
        }

        /// <summary>
        /// Detaches all retained property-change handlers; repeated disposal is harmless.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            foreach (var subscription in _propertyChangedHandlers.Values)
            {
                subscription.Observable.PropertyChanged -= subscription.Handler;
            }

            _propertyChangedHandlers.Clear();
            _disposed = true;
        }

        /// <summary>
        /// Fires all pending activations in the agenda. This method processes the list of activations that have been triggered but not yet executed. The 
        /// method continues to fire activations until the agenda is empty, ensuring that all rules that have been triggered are executed. This is a 
        /// crucial part of the inference process in a the rule engine, allowing it to respond to changes in facts and execute the appropriate rules based 
        /// on the current state of the network. Additionally, before firing activations from the agenda, this method processes any pre-loaded facts in the
        /// working memory to ensure that all facts are properly asserted and can trigger any relevant rules before activations are fired.
        /// </summary>
        /// <returns>The current instance of the ReteEngine.</returns>
        public ReteEngine FireAll()
        {
            EnsureRulesCanFire();
            ReplayWorkingMemoryIfNeeded();
            _agenda.FireAll();
            return this;
        }

        /// <summary>
        /// Places an agenda group on top of the focus stack so it fires before previously focused groups.
        /// </summary>
        public ReteEngine FocusAgendaGroup(string groupName)
        {
            _agenda.FocusAgendaGroup(groupName);
            return this;
        }

        /// <summary>
        /// Fires pending activations from only the specified agenda group, leaving other groups pending.
        /// </summary>
        public ReteEngine FireGroup(string groupName)
        {
            EnsureRulesCanFire();
            ReplayWorkingMemoryIfNeeded();
            _agenda.FireGroup(groupName);
            return this;
        }

        /// <summary>
        /// Make sure the rule is ready before trying to fire.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if not ready</exception>
        private void EnsureRulesCanFire()
        {
            if (_isApplyingChangeBatches)
            {
                throw new InvalidOperationException("Rules cannot be fired while a fact-change batch is being applied.");
            }
        }

        /// <summary>
        /// Detects whether or not the facts need to be worked through the network before rules are asserted.
        /// </summary>
        private void ReplayWorkingMemoryIfNeeded()
        {
            if (!_needsWorkingMemoryReplay) return;
            _workingMemory.ForEach(f => _root.Assert(f));
            _needsWorkingMemoryReplay = false;
        }

        /// <summary>
        /// Updates each fact independently and returns this engine for fluent chaining.
        /// A still-valid match may fire again once for a new fact revision; updates coalesce while its activation is pending.
        /// </summary>
        /// <remarks>A rule action may therefore run more than once during a fact's lifetime, once per fired revision.</remarks>
        /// <param name="facts">Facts to update; null entries are ignored.</param>
        /// <returns>The current engine.</returns>
        public ReteEngine Update(params object[] facts)
        {
            if (facts == null) return this;

            foreach (var fact in facts)
            {
                UpdateFact(fact, null);
            }
            return this;
        }

        /// <summary>
        /// Updates one fact, optionally providing the changed property name for dependency-aware reevaluation.
        /// A still-valid match may fire again once for this new revision; repeated updates while pending keep one activation.
        /// </summary>
        /// <remarks>A rule action may run again for a still-valid tuple after this update, even when the fact identity is unchanged.</remarks>
        /// <param name="fact">The fact to update; null is ignored.</param>
        /// <param name="propertyName">The changed property name, if known.</param>
        /// <returns>The current engine.</returns>
        public ReteEngine Update(object fact, string? propertyName)
        {
            UpdateFact(fact, propertyName);
            return this;
        }

        /// <summary>
        /// Update the fact in the network, asserting it if it is not already present. If the fact is already in working memory,
        /// it will be refreshed with the provided property name (or an empty string if none is provided). If the fact is not in
        /// working memory, it will be asserted into the network. This method ensures that the Rete network remains up to date
        /// with the latest information and can trigger rules based on changes to facts over time.
        /// </summary>
        /// <param name="fact">The fact to update; null is ignored.</param>
        /// <param name="propertyName">The changed property name, if known.</param>
        private void UpdateFact(object fact, string? propertyName)
        {
            if (fact == null) return;

            bool isInWorkingMemory = _workingMemory.Any(existing => ReferenceEquals(existing, fact));
            if (isInWorkingMemory)
            {
                _agenda.AdvanceFactRevision(fact);
                _root.Refresh(fact, propertyName ?? string.Empty);
                return;
            }

            // The exact instance is not in working memory, but a published batch may have committed a detached
            // snapshot under the same logical identity (IFact.Id). In that case, replace the tracked snapshot
            // with this instance rather than inserting a duplicate fact with the same identity.
            if (fact is IFact identityFact)
            {
                IFact? existingById = FindFactById(identityFact.Id);
                if (existingById != null)
                {
                    long nextRevision = _agenda.GetFactRevision(existingById) + 1;
                    Retract(existingById);
                    Assert(fact);
                    _agenda.SetFactRevision(fact, nextRevision);
                    return;
                }
            }

            Assert(fact);
        }

        /// <summary>
        /// Records side-effect metadata produced by a successful context action. This method is called after a staged
        /// rule action completes successfully and produces side effects that need to be tracked for potential rollback
        /// or auditing purposes. The side effects are stored in the rollback side-effect audit list, and if any of the
        /// side effects have associated compensators, they are also recorded for later execution if a rollback occurs.
        /// This allows the engine to maintain a history of side effects and their associated metadata, enabling better
        /// tracking and management of external actions taken during rule execution.
        /// </summary>
        /// <param name="sideEffects">The list of side effects to record.</param>
        internal void RecordSideEffects(IReadOnlyList<ReteCore.RuleSideEffect> sideEffects)
        {
            if (sideEffects.Count == 0)
            {
                return;
            }

            foreach (var sideEffect in sideEffects)
            {
                Guid sideEffectId = Guid.NewGuid();
                var metadata = new Dictionary<string, string>(sideEffect.Metadata);
                var entry = new RollbackSideEffectAuditEntry(
                    sideEffectId,
                    null,
                    null,
                    sideEffect.Origin.AgendaGroup,
                    sideEffect.Origin.RuleName,
                    sideEffect.Origin.ActivationId,
                    sideEffect.Category,
                    sideEffect.Description,
                    sideEffect.HasCompensator,
                    metadata,
                    sideEffect.OccurredAt,
                    sideEffect.HasCompensator
                        ? RollbackSideEffectDisposition.PendingCompensation
                        : RollbackSideEffectDisposition.NonRollbackable);

                _rollbackSideEffectAudit.Add(entry);
                if (sideEffect.Compensator != null)
                {
                    _sideEffectCompensators[sideEffectId] = sideEffect.Compensator;
                }

                RollbackSideEffect?.Invoke(this, new RollbackSideEffectEventArgs(entry));
            }
        }

        /// <summary>
        /// Publishes a batch of fact changes to the working memory and raises the FactChangesPublished event. This method is called after a staged
        /// rule action successfully completes.
        /// </summary>
        /// <param name="changes">The batch of fact changes to publish.</param>
        internal void PublishChanges(IReadOnlyList<FactChange> changes)
        {
            RollbackJournalEntry CreateRollbackEntry(
                long epoch,
                IReadOnlyList<FactChange> batch,
                IReadOnlyList<RollbackJournalChangeEntry> journalChanges,
                DateTimeOffset committedAt)
            {
                FactChangeOrigin? sharedOrigin = GetSharedOrigin(batch);

                return new RollbackJournalEntry(
                    Interlocked.Increment(ref _nextRollbackSequence),
                    epoch,
                    committedAt,
                    sharedOrigin?.AgendaGroup,
                    sharedOrigin?.RuleName,
                    sharedOrigin?.ActivationId,
                    Array.AsReadOnly(journalChanges.ToArray()));
            }

            _pendingChangeBatches.Enqueue(changes);
            if (_isApplyingChangeBatches) return;

            _isApplyingChangeBatches = true;
            long epoch = Interlocked.Increment(ref _nextRollbackEpoch);
            try
            {
                while (_pendingChangeBatches.Count > 0)
                {
                    IReadOnlyList<FactChange> batch = _pendingChangeBatches.Dequeue();
                    ValidateChangeBatch(batch);

                    FactChangeOrigin? sharedOrigin = GetSharedOrigin(batch);
                    DateTimeOffset startedAt = DateTimeOffset.UtcNow;
                    EmitRollbackLifecycle(
                        RollbackLifecycleStage.Started,
                        epoch,
                        null,
                        null,
                        null,
                        sharedOrigin?.AgendaGroup,
                        sharedOrigin?.RuleName,
                        sharedOrigin?.ActivationId,
                        null,
                        startedAt);

                    var journalChanges = new List<RollbackJournalChangeEntry>(batch.Count);
                    for (int index = 0; index < batch.Count; index++)
                    {
                        FactChange change = batch[index];
                        var before = CaptureFactState(change.FactId);
                        ApplyChange(change);
                        var after = CaptureFactState(change.FactId);

                        var journalChange = new RollbackJournalChangeEntry(
                            index,
                            change.Kind,
                            change.FactId,
                            change.ExpectedRevision,
                            change.Origin,
                            SnapshotFact(change.Fact),
                            before.Exists,
                            before.Revision,
                            SnapshotFact(before.Fact),
                            after.Exists,
                            after.Revision,
                            SnapshotFact(after.Fact));
                        journalChanges.Add(journalChange);
                        EmitRollbackLifecycle(
                            RollbackLifecycleStage.Applied,
                            epoch,
                            null,
                            index,
                            change.FactId,
                            change.Origin?.AgendaGroup,
                            change.Origin?.RuleName,
                            change.Origin?.ActivationId,
                            change.Kind,
                            DateTimeOffset.UtcNow);
                    }

                    _publishedChangeBatches.Add(batch);
                    RollbackJournalEntry entry = CreateRollbackEntry(epoch, batch, journalChanges, DateTimeOffset.UtcNow);
                    _rollbackJournal.Add(entry);
                    CorrelateSideEffects(entry);
                    var metadataEntry = new PublishedBatchMetadataEntry(
                        entry.Sequence,
                        entry.Epoch,
                        entry.CommittedAt,
                        entry.AgendaGroup,
                        entry.RuleName,
                        entry.ActivationId,
                        batch.Count);
                    _publishedBatchMetadata.Add(metadataEntry);
                    PublishedBatchMetadataCommitted?.Invoke(metadataEntry);
                    EmitRollbackLifecycle(
                        RollbackLifecycleStage.Completed,
                        epoch,
                        entry.Sequence,
                        null,
                        null,
                        entry.AgendaGroup,
                        entry.RuleName,
                        entry.ActivationId,
                        null,
                        entry.CommittedAt);
                    FactChangesPublished?.Invoke(batch);
                }
            }
            finally
            {
                _isApplyingChangeBatches = false;
            }
        }

        /// <summary>
        /// Rolls back the most recently committed and not-yet-rolled-back published batch.
        /// </summary>
        /// <returns>True when a rollback was attempted; otherwise false when no pending rollback entry exists.</returns>
        public bool RollbackLastPublishedBatch()
        {
            RollbackJournalEntry? latestPending = GetLatestPendingRollbackEntry();
            if (latestPending == null)
            {
                return false;
            }

            return RollbackPublishedBatch(latestPending.Sequence);
        }

        /// <summary>
        /// Rolls back a committed published batch by sequence. Only strict reverse commit order is supported.
        /// </summary>
        /// <param name="sequence">The rollback journal sequence to reverse.</param>
        /// <returns>True when rollback work was applied or confirmed idempotently; otherwise false when nothing is pending.</returns>
        /// <exception cref="InvalidOperationException">Thrown when sequence is missing, already rolled back under non-idempotent policy, or out-of-order.</exception>
        public bool RollbackPublishedBatch(long sequence)
        {
            RollbackJournalEntry? entry = _rollbackJournal.FirstOrDefault(journalEntry => journalEntry.Sequence == sequence);
            if (entry == null)
            {
                throw new InvalidOperationException($"Rollback sequence '{sequence}' was not found.");
            }

            if (_rolledBackSequences.Contains(sequence))
            {
                if (_rollbackIdempotencyMode == RollbackIdempotencyMode.Idempotent)
                {
                    EmitRollbackLifecycle(
                        RollbackLifecycleStage.Skipped,
                        entry.Epoch,
                        entry.Sequence,
                        null,
                        null,
                        entry.AgendaGroup,
                        entry.RuleName,
                        entry.ActivationId,
                        null,
                        DateTimeOffset.UtcNow);
                    return true;
                }

                throw new InvalidOperationException($"Rollback sequence '{sequence}' has already been applied.");
            }

            RollbackJournalEntry? latestPending = GetLatestPendingRollbackEntry();
            if (latestPending == null)
            {
                return false;
            }

            if (latestPending.Sequence != sequence)
            {
                throw new InvalidOperationException(
                    $"Out-of-order rollback is not supported. Latest rollbackable sequence is '{latestPending.Sequence}' in epoch '{latestPending.Epoch}'.");
            }

            return ExecuteRollback(entry);
        }

        /// <summary>
        /// Execute rollback for a single committed journal entry. This method applies the rollback changes in reverse order, restoring each
        /// fact to its pre-change state. It also handles side-effect compensation and emits lifecycle events for each stage of the rollback
        /// process. If any conflicts are detected during the rollback (e.g., the current state of a fact does not match the expected state),
        /// the method will either skip the conflicting change or throw an exception based on the configured rollback conflict policy. After
        /// successfully applying all changes, the sequence is marked as rolled back.
        /// </summary>
        /// <param name="entry">The rollback journal entry to be applied.</param>
        /// <returns>True if the rollback was successfully applied; otherwise, false.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the rollback cannot be applied due to conflicts or invalid state.</exception>
        private bool ExecuteRollback(RollbackJournalEntry entry)
        {
            _isApplyingChangeBatches = true;
            try
            {
                EmitRollbackLifecycle(
                    RollbackLifecycleStage.Started,
                    entry.Epoch,
                    entry.Sequence,
                    null,
                    null,
                    entry.AgendaGroup,
                    entry.RuleName,
                    entry.ActivationId,
                    null,
                    DateTimeOffset.UtcNow);

                for (int i = entry.Changes.Count - 1; i >= 0; i--)
                {
                    RollbackJournalChangeEntry change = entry.Changes[i];
                    var current = CaptureFactState(change.FactId);
                    bool matchesBefore = current.Exists == change.ExistedBefore && current.Revision == change.RevisionBefore;
                    bool matchesAfter = current.Exists == change.ExistsAfter && current.Revision == change.RevisionAfter;

                    if (matchesBefore)
                    {
                        EmitRollbackLifecycle(
                            RollbackLifecycleStage.Skipped,
                            entry.Epoch,
                            entry.Sequence,
                            change.Index,
                            change.FactId,
                            change.Origin?.AgendaGroup,
                            change.Origin?.RuleName,
                            change.Origin?.ActivationId,
                            change.Kind,
                            DateTimeOffset.UtcNow);
                        continue;
                    }

                    if (!matchesAfter)
                    {
                        EmitRollbackLifecycle(
                            RollbackLifecycleStage.Conflicted,
                            entry.Epoch,
                            entry.Sequence,
                            change.Index,
                            change.FactId,
                            change.Origin?.AgendaGroup,
                            change.Origin?.RuleName,
                            change.Origin?.ActivationId,
                            change.Kind,
                            DateTimeOffset.UtcNow,
                            _rollbackConflictPolicy == RollbackConflictPolicy.FailFast
                                ? RollbackConflictOutcome.FailFastAborted
                                : RollbackConflictOutcome.ConflictDetected);

                        if (_rollbackConflictPolicy == RollbackConflictPolicy.FailFast)
                        {
                            throw new InvalidOperationException(
                                $"Rollback conflict for fact '{change.FactId}' in sequence '{entry.Sequence}': current state does not match committed state.");
                        }

                        continue;
                    }

                    RestoreFactState(change);
                    EmitRollbackLifecycle(
                        RollbackLifecycleStage.Applied,
                        entry.Epoch,
                        entry.Sequence,
                        change.Index,
                        change.FactId,
                        change.Origin?.AgendaGroup,
                        change.Origin?.RuleName,
                        change.Origin?.ActivationId,
                        change.Kind,
                        DateTimeOffset.UtcNow);
                }

                CompensateSideEffects(entry);
                _rolledBackSequences.Add(entry.Sequence);
                EmitRollbackLifecycle(
                    RollbackLifecycleStage.Completed,
                    entry.Epoch,
                    entry.Sequence,
                    null,
                    null,
                    entry.AgendaGroup,
                    entry.RuleName,
                    entry.ActivationId,
                    null,
                    DateTimeOffset.UtcNow);
                return true;
            }
            finally
            {
                _isApplyingChangeBatches = false;
            }
        }

        /// <summary>
        /// Correlates previously observed side effects to the committed journal entry for the same activation. This method updates
        /// the epoch and sequence of any side-effect audit entries that match the activation ID of the provided rollback journal
        /// entry. It ensures that side effects are properly associated with the corresponding journal entry, allowing for accurate
        /// tracking and potential compensation during rollback operations. After updating the side-effect entries, it raises the
        /// RollbackSideEffect event to notify subscribers of the correlation.
        /// </summary>
        /// <param name="entry">The rollback journal entry to correlate side effects with.</param>
        private void CorrelateSideEffects(RollbackJournalEntry entry)
        {
            if (entry.ActivationId == null)
            {
                return;
            }

            foreach (var sideEffect in _rollbackSideEffectAudit.Where(audit =>
                         audit.ActivationId == entry.ActivationId &&
                         audit.Sequence == null))
            {
                sideEffect.Epoch = entry.Epoch;
                sideEffect.Sequence = entry.Sequence;
                RollbackSideEffect?.Invoke(this, new RollbackSideEffectEventArgs(sideEffect));
            }
        }

        /// <summary>
        /// Attempts compensation for side effects associated with the supplied rollback entry. This method iterates through the
        /// side-effect audit entries that match the activation ID of the provided rollback journal entry. For each side effect that
        /// has a compensator and has not yet been compensated, it attempts to execute the compensator. If the compensator is not
        /// available or fails, it updates the disposition of the side effect accordingly and raises the RollbackSideEffect event to
        /// notify subscribers of the outcome. The method respects the configured rollback conflict policy, throwing exceptions for
        /// failures if the policy is set to FailFast.
        /// </summary>
        /// <param name="entry">The journal entry to process a side effect for.</param>
        /// <exception cref="InvalidOperationException">Thrown if the rollback conflict policy is set to FailFast and a compensator
        /// is not available or fails.</exception>
        private void CompensateSideEffects(RollbackJournalEntry entry)
        {
            if (entry.ActivationId == null)
            {
                return;
            }

            var candidates = _rollbackSideEffectAudit
                .Where(audit => audit.ActivationId == entry.ActivationId)
                .OrderByDescending(audit => audit.OccurredAt)
                .ToList();

            foreach (var sideEffect in candidates)
            {
                if (!sideEffect.HasCompensator || sideEffect.Disposition == RollbackSideEffectDisposition.Compensated)
                {
                    continue;
                }

                if (!_sideEffectCompensators.TryGetValue(sideEffect.SideEffectId, out var compensator))
                {
                    sideEffect.Disposition = RollbackSideEffectDisposition.CompensationFailed;
                    sideEffect.DisposedAt = DateTimeOffset.UtcNow;
                    sideEffect.DispositionDetail = "Compensator was not available.";
                    RollbackSideEffect?.Invoke(this, new RollbackSideEffectEventArgs(sideEffect));

                    if (_rollbackConflictPolicy == RollbackConflictPolicy.FailFast)
                    {
                        throw new InvalidOperationException(
                            $"Rollback compensation for side effect '{sideEffect.SideEffectId}' could not run because no compensator was available.");
                    }

                    continue;
                }

                try
                {
                    compensator();
                    sideEffect.Disposition = RollbackSideEffectDisposition.Compensated;
                    sideEffect.DisposedAt = DateTimeOffset.UtcNow;
                    sideEffect.DispositionDetail = "Compensation applied during rollback.";
                    RollbackSideEffect?.Invoke(this, new RollbackSideEffectEventArgs(sideEffect));
                }
                catch (Exception ex)
                {
                    sideEffect.Disposition = RollbackSideEffectDisposition.CompensationFailed;
                    sideEffect.DisposedAt = DateTimeOffset.UtcNow;
                    sideEffect.DispositionDetail = ex.Message;
                    RollbackSideEffect?.Invoke(this, new RollbackSideEffectEventArgs(sideEffect));

                    if (_rollbackConflictPolicy == RollbackConflictPolicy.FailFast)
                    {
                        throw new InvalidOperationException(
                            $"Rollback compensation failed for side effect '{sideEffect.SideEffectId}'.", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the latest rollback journal entry that has not yet been rolled back. This method iterates through the rollback
        /// journal in reverse order, checking each entry's sequence against the set of rolled-back sequences.
        /// </summary>
        private RollbackJournalEntry? GetLatestPendingRollbackEntry()
        {
            for (int i = _rollbackJournal.Count - 1; i >= 0; i--)
            {
                if (!_rolledBackSequences.Contains(_rollbackJournal[i].Sequence))
                {
                    return _rollbackJournal[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Restores a fact to the journaled pre-change state. This method is called during rollback to revert a fact to its state
        /// before a change was applied. It checks whether the fact existed before the change and whether a snapshot of the fact's
        /// state is available. If the fact did not exist before, it retracts the current instance if it exists. If the fact did
        /// exist, it retracts the current instance (if any) and asserts a new instance based on the pre-change snapshot, restoring
        /// its revision to the recorded value. This ensures that the working memory accurately reflects the state of facts as they
        /// were before the changes were applied.
        /// </summary>
        /// <param name="change">The journal change entry containing the pre-change state.</param>
        private void RestoreFactState(RollbackJournalChangeEntry change)
        {
            IFact? existing = FindFactById(change.FactId);
            if (!change.ExistedBefore)
            {
                if (existing != null)
                {
                    Retract(existing);
                }

                return;
            }

            if (change.FactBefore == null)
            {
                throw new InvalidOperationException(
                    $"Rollback journal entry for fact '{change.FactId}' does not include a pre-change snapshot.");
            }

            if (existing != null)
            {
                Retract(existing);
            }

            IFact restored = change.FactBefore.Snapshot();
            Assert(restored);
            _agenda.SetFactRevision(restored, change.RevisionBefore);
        }

        /// <summary>
        /// Captures the current fact existence/revision/snapshot state for rollback bookkeeping. This method retrieves the current
        /// state of a fact in working memory, including whether it exists, its revision number, and a snapshot of its state. It is
        /// used to record the state of facts before changes are applied, allowing for accurate rollback if needed. If the fact does
        /// not exist in working memory, it returns a tuple indicating that the fact does not exist, with a revision of 0 and a null
        /// snapshot.
        /// </summary>
        /// <param name="factId">The ID of the fact to capture the state for.</param>
        /// <returns>A tuple containing whether the fact exists, its revision number, and a snapshot of its state.</returns>
        private (bool Exists, long Revision, IFact? Fact) CaptureFactState(Guid factId)
        {
            IFact? fact = FindFactById(factId);
            if (fact == null)
            {
                return (false, 0, null);
            }

            return (true, _agenda.GetFactRevision(fact), SnapshotFact(fact));
        }

        /// <summary>
        /// Emits a rollback lifecycle notification. This method raises the RollbackLifecycle event to notify subscribers of the current
        /// stage of the rollback process. It provides detailed information about the rollback operation, including the epoch, sequence,
        /// change index, fact ID, agenda group, rule name, activation ID, change kind, and the timestamp of when the event occurred. 
        /// This allows for tracking and monitoring of the rollback process in a granular manner.
        /// </summary>
        /// <param name="stage">The current stage of the rollback process.</param>
        /// <param name="epoch">The epoch of the rollback operation.</param>
        /// <param name="sequence">The sequence number of the rollback operation.</param>
        /// <param name="changeIndex">The index of the change within the rollback batch.</param>
        /// <param name="factId">The ID of the fact involved in the rollback.</param>
        /// <param name="agendaGroup">The agenda group associated with the rollback.</param>
        /// <param name="ruleName">The name of the rule associated with the rollback.</param>
        /// <param name="activationId">The ID of the activation associated with the rollback.</param>
        /// <param name="kind">The kind of fact change.</param>
        /// <param name="occurredAt">The timestamp of when the rollback event occurred.</param>
        /// <param name="conflictOutcome">The outcome of the conflict resolution, if applicable.</param>
        private void EmitRollbackLifecycle(
            RollbackLifecycleStage stage,
            long epoch,
            long? sequence,
            int? changeIndex,
            Guid? factId,
            string? agendaGroup,
            string? ruleName,
            Guid? activationId,
            FactChangeTypeEnum? kind,
            DateTimeOffset occurredAt,
            RollbackConflictOutcome conflictOutcome = RollbackConflictOutcome.None)
        {
            RollbackLifecycle?.Invoke(this, new RollbackLifecycleEventArgs(
                stage,
                epoch,
                sequence,
                changeIndex,
                factId,
                agendaGroup,
                ruleName,
                activationId,
                kind,
                occurredAt,
                conflictOutcome));
        }

        /// <summary>
        /// Gets a shared origin when all changes in the batch were produced by the same activation. If the batch contains changes from multiple
        /// activations, this method returns null.
        /// </summary>
        /// <param name="batch">The batch of fact changes to evaluate.</param>
        /// <returns>The shared origin if all changes have the same origin; otherwise, null.</returns>
        private static FactChangeOrigin? GetSharedOrigin(IReadOnlyList<FactChange> batch)
        {
            if (batch.Count == 0)
            {
                return null;
            }

            FactChangeOrigin? firstOrigin = batch[0].Origin;
            return batch.All(change => EqualityComparer<FactChangeOrigin?>.Default.Equals(change.Origin, firstOrigin))
                ? firstOrigin
                : null;
        }

        /// <summary>
        /// Creates a detached snapshot for journal persistence. This method is used to create a snapshot of a fact's state that can be stored in
        /// the rollback journal. It ensures that the snapshot is independent of the original fact instance, allowing for accurate restoration
        /// during rollback operations. If the fact is null, it returns null; otherwise, it calls the Snapshot method on the fact to create a
        /// detached copy.
        /// </summary>
        /// <param name="fact">The fact to create a snapshot of.</param>
        /// <returns>A detached snapshot of the fact, or null if the fact is null.</returns>
        private static IFact? SnapshotFact(IFact? fact)
        {
            return fact?.Snapshot();
        }

        /// <summary>
        /// Validates a batch of fact changes to ensure that they are consistent and can be applied to the working memory. This method checks for null changes, 
        /// duplicate fact IDs, and expected revisions.
        /// </summary>
        /// <param name="changes">The batch of fact changes to validate.</param>
        /// <exception cref="InvalidOperationException">Thrown if the batch contains invalid changes.</exception>
        private void ValidateChangeBatch(IReadOnlyList<FactChange> changes)
        {
            var states = new Dictionary<Guid, (bool Exists, long Revision)>();
            foreach (FactChange change in changes)
            {
                if (change == null)
                {
                    throw new InvalidOperationException("A change batch cannot contain a null change.");
                }

                if (!states.TryGetValue(change.FactId, out var state))
                {
                    IFact? current = FindFactById(change.FactId);
                    state = (current != null, current == null ? 0 : _agenda.GetFactRevision(current));
                }

                switch (change.Kind)
                {
                    case FactChangeTypeEnum.Assert:
                        ValidateFactPayload(change);
                        if (!state.Exists) state = (true, 0);
                        break;
                    case FactChangeTypeEnum.Update:
                        ValidateFactPayload(change);
                        ValidateExpectedRevision(change, state);
                        state = state.Exists ? (true, state.Revision + 1) : (true, 0);
                        break;
                    case FactChangeTypeEnum.Retract:
                        ValidateExpectedRevision(change, state);
                        if (state.Exists) state = (false, 0);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported fact change kind '{change.Kind}'.");
                }

                states[change.FactId] = state;
            }
        }

        /// <summary>
        /// Validate the payload of a fact change to ensure that it contains a valid fact with the matching identity. This method checks that the fact is not null
        /// and that its ID matches the expected FactId in the change.
        /// </summary>
        /// <param name="change">The fact change to validate.</param>
        /// <exception cref="InvalidOperationException">Thrown if the fact change payload is invalid.</exception>
        private static void ValidateFactPayload(FactChange change)
        {
            if (change.Fact == null || change.Fact.Id != change.FactId)
            {
                throw new InvalidOperationException("An assert or update change must contain a fact with the matching identity.");
            }
        }

        /// <summary>
        /// Validate the expected revision of a fact change against the current state of the fact in working memory. This method checks that the expected revision 
        /// matches the current revision of the fact, if it exists.
        /// </summary>
        /// <param name="change">The fact change to validate.</param>
        /// <param name="state">The current state of the fact in working memory.</param>
        /// <exception cref="InvalidOperationException">Thrown if the expected revision does not match the current revision.</exception>
        private static void ValidateExpectedRevision(FactChange change, (bool Exists, long Revision) state)
        {
            if (change.ExpectedRevision.HasValue &&
                (!state.Exists || state.Revision != change.ExpectedRevision.Value))
            {
                throw new InvalidOperationException(
                    $"Fact '{change.FactId}' has revision {(state.Exists ? state.Revision.ToString() : "missing")}; " +
                    $"expected revision {change.ExpectedRevision.Value}.");
            }
        }

        /// <summary>
        /// Get the fact in working memory with the specified identity, or null if no such fact exists. Throws an exception if more than one fact with the same
        /// identity is found.
        /// </summary>
        /// <param name="factId">The identity of the fact to find.</param>
        /// <returns>The fact with the specified identity, or null if no such fact exists.</returns>
        /// <exception cref="InvalidOperationException">Thrown if more than one fact with the same identity is found.</exception>
        private IFact? FindFactById(Guid factId)
        {
            IFact[] matches = _workingMemory.OfType<IFact>()
                .Where(fact => fact.Id == factId)
                .Take(2)
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidOperationException($"Working memory contains more than one fact with identity '{factId}'.");
            }
            return matches.SingleOrDefault();
        }

        /// <summary>
        /// Apply the specified fact change to the working memory and Rete network. This method handles asserting, updating, and retracting facts based on the kind of change.
        /// </summary>
        /// <param name="change">The fact change to apply.</param>
        /// <exception cref="InvalidOperationException">Thrown if the change cannot be applied.</exception>
        private void ApplyChange(FactChange change)
        {
            IFact? existing = FindFactById(change.FactId);
            switch (change.Kind)
            {
                case FactChangeTypeEnum.Assert:
                    if (existing == null) Assert(change.Fact!);
                    break;
                case FactChangeTypeEnum.Update:
                    IFact replacement = change.Fact!;
                    if (existing == null)
                    {
                        Assert(replacement);
                    }
                    else if (ReferenceEquals(existing, replacement))
                    {
                        UpdateFact(replacement, null);
                    }
                    else
                    {
                        long nextRevision = _agenda.GetFactRevision(existing) + 1;
                        Retract(existing);
                        Assert(replacement);
                        _agenda.SetFactRevision(replacement, nextRevision);
                    }
                    break;
                case FactChangeTypeEnum.Retract:
                    if (existing != null) Retract(existing);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported fact change kind '{change.Kind}'.");
            }
        }

        /// <summary>
        /// Starts the definition of a new rule in the Rete engine. This method initializes a new ReteBuilder instance for the specified rule name, allowing 
        /// users to define the conditions and actions of the rule using a fluent interface. The ReteBuilder provides methods for specifying the conditions 
        /// of the rule, as well as the actions to be executed when the rule is triggered. 
        /// By calling the Begin method, users can start building a new rule and ultimately add it to the Rete network, where it will be evaluated against 
        /// asserted facts and can trigger activations based on its conditions. The rule name provided to the Begin method is used for identification and 
        /// management purposes, allowing users to easily reference and potentially remove the rule from the network in the future if needed.
        /// </summary>
        /// <param name="ruleName">The name of the rule to begin defining.</param>
        /// <returns>The fluent interface for defining the start of a rule.</returns>
        public IRuleSetup Begin(string ruleName) => new FluentConverter<Fact>(this, ruleName);

        /// <summary>
        /// Returns an AlphaMemory for the specified type and optional name. This method retrieves an existing AlphaMemory from the alpha registry if it 
        /// exists, or creates a new one if it does not. The AlphaMemory is associated with a specific type and an optional name, allowing for efficient 
        /// retrieval and reuse of alpha memories based on the type and name of the facts they are associated with. If an initial condition is provided, an 
        /// AlphaConditionNode is created to evaluate the condition against facts of the specified type, and the AlphaMemory is added as a successor to the 
        /// ObjectTypeNode for that type. This method allows for dynamic creation and management of alpha memories in the Rete network, enabling users to 
        /// define conditions for specific types of facts and manage the flow of facts through the network based on those conditions. The use of a registry 
        /// ensures that alpha memories are reused when possible, preventing unnecessary duplication and improving the efficiency of the network.
        /// </summary>
        /// <typeparam name="T">The type of the facts for which the AlphaMemory is being retrieved or created.</typeparam>
        /// <param name="name">An optional name for the AlphaMemory, allowing for differentiation between multiple alpha memories of the same type.</param>
        /// <param name="initialCondition">An optional initial condition to be applied to the AlphaMemory, represented as a function that takes a fact of 
        /// type T and returns a boolean.</param>
        /// <returns>The AlphaMemory associated with the specified type and name.</returns>
        public AlphaMemory GetAlphaMemory<T>(string? name = null, Func<T, bool>? initialCondition = null)
        {
            var type = typeof(T);

            // Get or create the ObjectTypeNode for type T
            var typeNode = _root.GetSuccessor<T>();
            if (typeNode == null)
            {
                typeNode = new ObjectTypeNode<T>();
                _root.AddSuccessor(typeNode);
            }

            // Use a combination of type and name as the key for the alpha registry.
            // We want to reuse the same alpha memory for the same type and name,
            // but allow different conditions to create separate alpha memories if needed.
            var registryKey = (type, name ?? "default");
            if (!_alphaRegistry.ContainsKey(registryKey))
            {
                AlphaConditionNode<T>? alphaConditionNode = null;
                var alpha = new AlphaMemory();
                if (initialCondition != null)
                {
                    alphaConditionNode = new AlphaConditionNode<T>(name ?? "", initialCondition, alpha);
                }
                typeNode.AddSuccessor(alphaConditionNode != null ? alphaConditionNode : alpha);

                _alphaRegistry[registryKey] = alpha;
            }
            return (AlphaMemory)_alphaRegistry[registryKey];
        }

        /// <summary>
        /// Removes a rule from the Rete network based on its name. This method removes any alpha memories associated with the specified rule name from 
        /// the alpha registry, removes any activations associated with the rule from the agenda, and removes the terminal node for the rule from the 
        /// network. The method also prunes any orphaned nodes that may result from the removal of the terminal node, ensuring that the network remains 
        /// clean and efficient. This allows for dynamic modification of the Rete network by removing rules that are no longer needed or relevant, while 
        /// maintaining the integrity of the network. 
        /// </summary>
        /// <param name="ruleName">The name of the rule to remove.</param>
        public void RemoveRule(string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName))
            {
                return;
            }
            if (_alphaRegistry.Keys.Any(k => k.Name == ruleName))
            {
                var keysToRemove = _alphaRegistry.Keys.Where(k => k.Name == ruleName).ToList();
                foreach (var key in keysToRemove)
                {
                    _alphaRegistry.Remove(key);
                    if (EnableConditionTraceLogging)
                    {
                        Console.WriteLine($"[ReteEngine] Removed alpha memory for rule: {ruleName} (Type: {key.Type.Name})");
                    }
                }
            }
            if (_agenda.RemoveActivationsByRule(ruleName))
            {
                if (EnableConditionTraceLogging)
                {
                    Console.WriteLine($"[ReteEngine] Removed activations for rule: {ruleName}");
                }
            }
            else
            {
                if (EnableConditionTraceLogging)
                {
                    Console.WriteLine($"[ReteEngine] No activations found for rule: {ruleName}");
                }
            }

            var terminalNode = _terminalNodes.FirstOrDefault(t => t.RuleMetadata.Name == ruleName);
            if (terminalNode != null)
            {
                terminalNode.RemoveFromParentNode();
                if (EnableConditionTraceLogging)
                {
                    Console.WriteLine($"[ReteEngine] Removed terminal node for rule: {ruleName}");
                }
                _terminalNodes.Remove(terminalNode);
                PruneOrphanedNodes(terminalNode.Parent);
            }
            else
            {
                if (EnableConditionTraceLogging)
                {
                    Console.WriteLine($"[ReteEngine] No terminal node found for rule: {ruleName}");
                }
            }
        }

        /// <summary>
        /// A static convenience method to recursively prune orphaned nodes from the Rete network. This method is called after a terminal node is removed 
        /// from the network to ensure that any nodes that no longer have successors (i.e., orphaned nodes) are also removed from the network. The method 
        /// checks if the given node has any successors, and if it does not and is not the root node, it removes the node from its parent and then 
        /// recursively calls itself on the parent node. This process continues up the network until it reaches a node that has no successors or the root 
        /// node.
        /// </summary>
        /// <param name="node">The node to start pruning from.</param>
        private static void PruneOrphanedNodes(IReteNode? node)
        {
            if (node != null && node.Successors.Count() == 0 && node is not RootNode)
            {
                var parent = node.Parent;
                parent?.RemoveSuccessor(node);
                if (parent != null) PruneOrphanedNodes(parent);
            }
        }

        /// <summary>
        /// A debugging method to visualize the flow of a fact through the Rete network. This method prints out the structure of the network and how the 
        /// specified fact is processed at each node.
        /// </summary>
        /// <param name="fact">The fact to print information from</param>
        public void DebugPrintNetwork(object fact)
        {
            Console.WriteLine($"\n--- Rete Trace for Fact {fact} ---");
            _root.DebugPrint(fact, 0);
            Console.WriteLine("--- End Trace ---");
        }

        /// <summary>
        /// A transparent tracing node that records active tokens, reports propagation events, and forwards them to successors.
        /// </summary>
        public class TraceNode : IReteNode, ILatentMemory
        {
            /// <summary>
            /// A label for the TraceNode, used to identify it in debug output. This label can be set when the TraceNode is created and is included in the 
            /// messages printed when facts are asserted, retracted, or refreshed through this node.
            /// </summary>
            private readonly string _label;
            /// <summary>
            /// Keeps track of successor nodes that will receive facts asserted, retracted, or refreshed through this TraceNode. Each successor is 
            /// an IReteNode that will be affected by operations performed on this node. The collection is initialized as an empty list and can be 
            /// modified by adding new successor nodes using the AddSuccessor method. The order of successors in the list may affect the order in 
            /// which facts are propagated to them, but does not affect the logic of the network.
            /// </summary>
            private readonly List<IReteNode> _successors = new();
            /// <summary>
            /// Tracks the tokens currently passing through this transparent node.
            /// </summary>
            private readonly List<Token> _tokens = new();
            /// <summary>
            /// A TextWriter used for outputting trace information. This writer can be set when the TraceNode is created, allowing for flexibility in where
            /// trace information is written. By default, it writes to the console.
            /// </summary>
            private readonly TextWriter _output;

            /// <summary>
            /// Initializes a labeled trace checkpoint with an optional output writer.
            /// </summary>
            /// <param name="label">The label included in trace reports.</param>
            /// <param name="output">Optional destination for reports; defaults to the current console writer.</param>
            public TraceNode(string label, TextWriter? output = null)
            {
                _label = string.IsNullOrWhiteSpace(label)
                    ? throw new ArgumentException("A trace label is required.", nameof(label))
                    : label;
                _output = output ?? Console.Out;
            }

            /// <summary>
            /// Returns the live token memory used by downstream joins.
            /// </summary>
            public IEnumerable<Token> Tokens => _tokens.AsReadOnly();

            /// <summary>
            /// Returns an enumerable of successor nodes. In this implementation, there is typically one successor node that receives 
            /// facts that satisfy the condition defined by the predicate.
            /// </summary>
            public IEnumerable<IReteNode> Successors => _successors;

            /// <summary>
            /// The parent node in the Rete network. This property allows for navigation back up the network from this node.
            /// </summary>
            public IReteNode? Parent { get; set; }

            /// <summary>
            /// Connects a successor and seeds it with tokens already held by this trace node.
            /// </summary>
            /// <param name="node">The node to add as a successor</param>
            public void AddSuccessor(IReteNode node)
            {
                if (_successors.Contains(node)) return;
                node.Parent = this;
                _successors.Add(node);
                foreach (var token in _tokens.ToList()) node.Assert(token);
            }

            /// <summary>
            /// Initializes the trace node with a set of tokens, ensuring that each token is unique and forwarding them to successors.
            /// </summary>
            /// <param name="tokens">The set of tokens to initialize the trace node with.</param>
            internal void InitializeTokens(IEnumerable<Token> tokens)
            {
                foreach (var token in tokens)
                {
                    if (_tokens.Any(existing => existing.Equals(token))) continue;
                    _tokens.Add(token);
                    WriteTrace("SNAPSHOT", token);
                }
            }

            /// <summary>
            /// Tracks and reports an assertion, then forwards a new token to successors once.
            /// </summary>
            /// <param name="fact">The fact to pass on through the network</param>
            public void Assert(object fact)
            {
                bool isNewToken = true;
                if (fact is Token token)
                {
                    int existingIndex = _tokens.FindIndex(existing => existing.Equals(token));
                    if (existingIndex >= 0)
                    {
                        _tokens[existingIndex] = token;
                        isNewToken = false;
                    }
                    else
                    {
                        _tokens.Add(token);
                    }
                }

                WriteTrace("ASSERT", fact);
                if (isNewToken)
                {
                    foreach (var successor in _successors.ToList()) successor.Assert(fact);
                }
            }

            /// <summary>
            /// Removes and forwards the retracted token, or tokens containing a retracted fact.
            /// </summary>
            /// <param name="fact">The fact to retract</param>
            public void Retract(object fact)
            {
                WriteTrace("RETRACT", fact);
                if (fact is Token token)
                {
                    _tokens.RemoveAll(existing => existing.Equals(token));
                    foreach (var successor in _successors.ToList()) successor.Retract(token);
                    return;
                }

                var affectedTokens = _tokens
                    .Where(existing => existing.NamedFacts.Values.Any(value => ReferenceEquals(value, fact)))
                    .ToList();
                foreach (var affectedToken in affectedTokens)
                {
                    _tokens.Remove(affectedToken);
                    foreach (var successor in _successors.ToList()) successor.Retract(affectedToken);
                }
                if (affectedTokens.Count == 0)
                {
                    foreach (var successor in _successors.ToList()) successor.Retract(fact);
                }
            }

            /// <summary>
            /// Reports a refresh and forwards it with the property name to affected successors.
            /// </summary>
            /// <param name="fact">The fact to refresh</param>
            /// <param name="prop">The property of the fact that has changed</param>
            public void Refresh(object fact, string propertyName)
            {
                WriteTrace("REFRESH", fact, propertyName);
                if (fact is Token token)
                {
                    int tokenIndex = _tokens.FindIndex(existing => existing.Equals(token));
                    if (tokenIndex >= 0) _tokens[tokenIndex] = token;
                    foreach (var successor in _successors.ToList()) successor.Refresh(token, propertyName);
                    return;
                }

                var affectedTokens = _tokens
                    .Where(existing => existing.NamedFacts.Values.Any(value => ReferenceEquals(value, fact)))
                    .ToList();
                if (affectedTokens.Count == 0)
                {
                    foreach (var successor in _successors.ToList()) successor.Refresh(fact, propertyName);
                    return;
                }

                foreach (var affectedToken in affectedTokens)
                {
                    foreach (var successor in _successors.ToList()) successor.Refresh(affectedToken, propertyName);
                }
            }

            /// <summary>
            /// Removes a successor node from this RootNode. This method is used when a successor node is no longer needed or when the 
            /// structure of the Rete network changes. When a successor is removed, it will no longer receive any tokens asserted, 
            /// retracted, or refreshed through this node. The method simply removes the specified child node from the list of successors, 
            /// ensuring that it is no longer part of the propagation path for tokens. This allows for dynamic modification of the Rete network 
            /// as it evolves over time and ensures that nodes can be added or removed as needed without affecting the overall functionality of 
            /// the system.
            /// </summary>
            /// <param name="successor">The successor node to remove. Cannot be null.</param>
            public void RemoveSuccessor(IReteNode successor)
            {
                _successors.Remove(successor);
                if (ReferenceEquals(successor.Parent, this)) successor.Parent = null;
            }

            /// <summary>
            /// Reports the current token memory and recursively prints downstream node diagnostics.
            /// </summary>
            /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
            /// printed.</param>
            /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
            /// Defaults to 0.</param>
            public void DebugPrint(object fact, int level = 0)
            {
                string indent = new string(' ', level * 2);
                _output.WriteLine($"{indent}[TraceNode:{_label}] {_tokens.Count} active token(s)");
                foreach (var token in _tokens)
                {
                    _output.WriteLine($"{indent}  {FormatTraceValue(token)}");
                }
                foreach (var successor in _successors)
                {
                    successor.DebugPrint(fact, level + 1);
                }
            }

            /// <summary>
            /// Outputs a trace message to the configured output, including the operation type, fact information, and optional property name.
            /// </summary>
            /// <param name="operation">The type of operation being traced (e.g., "ASSERT", "RETRACT").</param>
            /// <param name="fact">The fact object involved in the operation.</param>
            /// <param name="propertyName">An optional property name associated with the fact.</param>
            private void WriteTrace(string operation, object fact, string? propertyName = null)
            {
                string propertySuffix = propertyName == null ? string.Empty : $" Property='{propertyName}'";
                _output.WriteLine($"[TRACE:{_label}] {operation}{propertySuffix}: {FormatTraceValue(fact)}");
            }

            /// <summary>
            /// Formats a fact or token for trace output. This method generates a string representation of the provided value, which can be a fact,\
            /// token, or any other object.
            /// </summary>
            /// <param name="value">The value to format for trace output.</param>
            /// <returns>A string representation of the value suitable for trace output.</returns>
            private static string FormatTraceValue(object? value)
            {
                if (value == null) return "null";
                if (value is Token token)
                {
                    var namedFacts = token.NamedFacts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{pair.Key}={FormatTraceValue(pair.Value)}");
                    return $"Token({string.Join(", ", namedFacts)})";
                }
                if (value is IFact fact)
                {
                    return $"{value.GetType().Name}#{fact.Id}:{fact.UnderlyingObject}";
                }
                return $"{value.GetType().Name}:{value}";
            }
        }

    }
}
