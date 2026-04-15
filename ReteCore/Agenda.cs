using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        /// The list of pending activations in the agenda. Each activation represents a rule that has been triggered and 
        /// is waiting to be fired. The agenda processes these activations based on their salience (priority) when the 
        /// FireAll method is called, ensuring that higher priority rules are executed before lower priority ones when 
        /// multiple activations are present.
        /// </summary>
        private readonly List<Activation> _activations = new();

        /// <summary>
        /// Does the agenda currently have any pending activations? This property returns true if there are one or more 
        /// activations in the agenda, indicating that there are rules that have been triggered and are waiting to be 
        /// fired. If this property returns false, it means that there are no pending activations and the agenda is 
        /// currently empty.
        /// </summary>
        public bool HasActivations => _activations.Count > 0;

        /// <summary>
        /// Adds a new activation to the agenda. This method is typically called when a rule's conditions are satisfied, 
        /// creating an activation that represents the pending execution of that rule. The activation is added to the 
        /// list of pending activations, which will be processed later during the firing phase.
        /// </summary>
        /// <param name="a">The Activation object to add to the list of activations.</param>
        public void Add(Activation a) => _activations.Add(a);

        /// <summary>
        /// Removes any pending activations from the agenda that are associated with the specified fact. This is typically 
        /// called when a fact is retracted from the working memory, ensuring that any rules that were triggered by that 
        /// fact but have not yet fired are cancelled and will not execute based on outdated information.
        /// </summary>
        /// <param name="fact">The fact object to remove.</param>
        public void RemoveByFact(object fact)
        {
            // Remove any activation where the Match (Token) contains the retracted fact
            if (fact is Token token)
            {
                int removedCount = _activations.RemoveAll(a => a.Match.NamedFacts.Values.Any(f => f == token.Fact));
                if (removedCount > 0)
                {
                    Console.WriteLine($"[AGENDA] Cancelled {removedCount} pending activations.");
                }
            }
        }

        /// <summary>
        /// Fires all pending activations in the agenda, executing their associated actions in order of descending 
        /// salience (priority). After firing, the activations are removed from the agenda. This method is typically 
        /// called during the rule execution phase to process all activated rules based on their priority, ensuring 
        /// that higher salience rules are executed before lower salience ones when multiple activations are present.
        /// </summary>
        public void FireAll()
        {
            if (!HasActivations) { return; }
            var sorted = _activations.OrderByDescending(a => a.Salience).ToList();
            _activations.Clear();
            foreach (var activation in sorted) { activation.Fire(); }
        }
    }
}
