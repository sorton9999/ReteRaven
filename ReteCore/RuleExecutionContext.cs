using System;
using System.Collections.Generic;

namespace ReteCore
{
    /// <summary>
    /// Provides an activation-scoped buffer for staged fact changes.  The changes are not applied to the fact
    /// base until the context is completed, and can be discarded if the rule execution is aborted. An origin
    /// is stored with each change to identify the rule execution that produced it.  The context also allows 
    /// for controlled publication of the staged changes.
    /// </summary>
    public sealed class RuleExecutionContext
    {
        /// <summary>
        /// The origin of the fact changes produced by this context, which identifies the rule execution that produced them.
        /// </summary>
        private readonly FactChangeOrigin _origin;
        /// <summary>
        /// The list of fact changes staged in this context.  Changes are not applied to the fact base until the context is completed.
        /// </summary>
        private readonly List<FactChange> _changes = new();
        /// <summary>
        /// A private flag indicating whether publication of the staged changes has been requested.  Once publication is requested,
        /// no further changes can be staged.
        /// </summary>
        private bool _publishRequested;
        /// <summary>
        /// A private flag indicating whether the context has been completed or discarded.  Once the context is completed or discarded,
        /// no further changes can be staged.
        /// </summary>
        private bool _completed;

        /// <summary>
        /// The constructor for the RuleExecutionContext class.  It initializes a new instance of the class with the specified match,
        /// rule name, and activation ID.
        /// </summary>
        /// <param name="match">The token that triggered the rule activation.</param>
        /// <param name="ruleName">The name of the rule being executed.</param>
        /// <param name="activationId">The unique identifier for this rule activation.</param>
        /// <param name="agendaGroupName">The agenda group that scheduled this rule activation.</param>
        internal RuleExecutionContext(Token match, string ruleName, Guid activationId, string agendaGroupName)
        {
            Match = match;
            RuleName = ruleName;
            ActivationId = activationId;
            AgendaGroupName = agendaGroupName;
            _origin = new FactChangeOrigin(agendaGroupName, ruleName, activationId);
        }

        /// <summary>
        /// The token that triggered the rule activation.  This token contains the facts that matched the rule's conditions.
        /// </summary>
        public Token Match { get; }

        /// <summary>
        /// The name of the rule being executed.  This is used to identify the origin of any fact changes produced by this context.
        /// </summary>
        public string RuleName { get; }

        /// <summary>
        /// The unique identifier for this rule activation.  This is used to identify the origin of any fact changes produced by this context.
        /// </summary>
        public Guid ActivationId { get; }

        /// <summary>
        /// The agenda group that scheduled the rule activation.
        /// </summary>
        public string AgendaGroupName { get; }

        /// <summary>
        /// The origin of the fact changes produced by this context, which identifies the rule execution that produced them.
        /// </summary>
        public FactChangeOrigin Origin => _origin;

        /// <summary>
        /// Asserts a new fact into the context. The fact is staged for insertion when the context is completed.
        /// </summary>
        /// <param name="fact">The fact to assert.</param>
        public void Assert(IFact fact)
        {
            Stage(() => FactChange.Assert(fact, _origin));
        }

        /// <summary>
        /// Updates an existing fact in the context. The fact is staged for update when the context is completed.
        /// </summary>
        /// <param name="replacement">The fact to update.</param>
        /// <param name="expectedRevision">The expected revision of the fact.</param>
        public void Update(IFact replacement, long? expectedRevision = null)
        {
            Stage(() => FactChange.Update(replacement, expectedRevision, _origin));
        }

        /// <summary>
        /// Retracts an existing fact from the context. The fact is staged for removal when the context is completed.
        /// </summary>
        /// <param name="factId">The unique identifier of the fact to retract.</param>
        /// <param name="expectedRevision">The expected revision of the fact.</param>
        public void Retract(Guid factId, long? expectedRevision = null)
        {
            Stage(() => FactChange.Retract(factId, expectedRevision, _origin));
        }

        /// <summary>
        /// Publishes the staged fact changes.
        /// </summary>
        public void Publish()
        {
            EnsureCanStage();
            _publishRequested = true;
        }

        /// <summary>
        /// A private method that stages a fact change for later publication.  It ensures that the context is still active and can
        /// accept staged changes.
        /// </summary>
        /// <param name="createChange">A function that creates the fact change to be staged.</param>
        private void Stage(Func<FactChange> createChange)
        {
            EnsureCanStage();
            _changes.Add(createChange());
        }

        /// <summary>
        /// Indicates whether publication of the staged fact changes has been requested.  Once publication is requested, no further
        /// changes can be staged.
        /// </summary>
        /// <returns>The list of fact changes if publication was requested and there are changes; otherwise, null.</returns>
        internal IReadOnlyList<FactChange>? Complete()
        {
            _completed = true;
            return _publishRequested && _changes.Count > 0
                ? Array.AsReadOnly(_changes.ToArray())
                : null;
        }

        /// <summary>
        /// Discard the context, clearing any staged fact changes and marking the context as completed.  Once the context is discarded,
        /// no further changes can be staged.
        /// </summary>
        internal void Discard()
        {
            _completed = true;
            _changes.Clear();
        }

        /// <summary>
        /// Detects whether the context is still active and can accept staged fact changes.  If the context has been completed or
        /// publication has been requested, an InvalidOperationException is thrown.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the context has been completed or publication has been requested.</exception>
        private void EnsureCanStage()
        {
            if (_completed)
            {
                throw new InvalidOperationException("The rule execution context is no longer active.");
            }
            if (_publishRequested)
            {
                throw new InvalidOperationException("No changes can be staged after publication is requested.");
            }
        }
    }
}
