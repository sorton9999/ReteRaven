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
using System.Globalization;
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
        /// currently empty or all activations are in a non-pending state.
        /// </summary>
        public bool HasActivations
        {
            get { return _activations.Count > 0 && 
                    _activations.Any((a) => a.State == Activation.ActivationState.Pending); }
        }
        /// <summary>
        /// Stores a registry of activations that have already been fired, using a unique key generated from the rule name and 
        /// the facts involved in the activation. This registry is used to prevent duplicate activations from firing multiple 
        /// times, ensuring that once an activation has been executed, it will not be triggered again based on the same set of 
        /// facts.
        /// </summary>
        private readonly List<string> _firedActivationsRegistry = new();

        /// <summary>
        /// An accessor for the list of pending activations in the agenda. This property returns a read-only enumerable collection 
        /// of Activation objects.
        /// </summary>
        public IEnumerable<Activation> Activations => _activations.AsReadOnly();

        /// <summary>
        /// Adds a new activation to the agenda. This method is typically called when a rule's conditions are satisfied, 
        /// creating an activation that represents the pending execution of that rule. The activation is added to the 
        /// list of pending activations, which will be processed later during the firing phase.
        /// </summary>
        /// <param name="activation">The Activation object to add to the list of activations.</param>
        public void Add(Activation activation)
        {
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
        /// Fires all pending activations in the agenda, executing their associated actions in order of descending 
        /// salience (priority). The method continues to process activations until there are no more pending 
        /// activations left in the agenda.
        /// </summary>
        public void FireAll()
        {
            // As long as we have activations, look for the next pending one.
            // Remember, activations are pre-sorted by salience and time when added, so the first pending
            // found will always be the highest priority.
            while (HasActivations)
            {
                // Find the first activation that hasn't been processed yet
                var activation = _activations.FirstOrDefault(a => a.State == Activation.ActivationState.Pending);

                // If there are absolutely no Pending activations left, we are officially done forward-chaining
                if (activation == null)
                {
                    break;
                }

                string keyFacts = GenerateActivationKey(activation);

                // Check duplication registry
                if (_firedActivationsRegistry.Contains(keyFacts))
                {
                    Console.WriteLine($"[AGENDA] Skipping activation of rule '{activation.RuleName}' with facts [{keyFacts}] as it has already been fired.");
                    activation.State = Activation.ActivationState.Cancelled; // Mark it so FirstOrDefault skips it next time
                    continue;
                }

                Console.WriteLine($"[AGENDA] Firing activation of rule '{activation.RuleName}' with facts [{keyFacts}] and salience {activation.Salience}.");

                // Lock the state immediately
                activation.State = Activation.ActivationState.Fired;
                _firedActivationsRegistry.Add(keyFacts);

                // This will safely append ApplyShipment as 'Pending' to the end of the collection,
                // and our FirstOrDefault() will seamlessly grab it on the very next loop iteration.
                activation.Fire();
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
        /// Generates a unique key for an activation based on its rule name and the facts involved in the activation. 
        /// Activations are saved , so this key is used to track which activations have already been fired, preventing 
        /// duplicates from executing multiple times.
        /// </summary>
        /// <param name="activation">The activation for which to generate a key.</param>
        /// <returns>A unique key representing the activation.</returns>
        public string GenerateActivationKey(Activation activation)
        {
            string keyFacts = string.Join(", ", activation.Match.NamedFacts.Select(kv => $"{kv.Key}={(kv.Value as Cell)?.Id}"));
            return $"{activation.RuleName}_{keyFacts}";
        }
    }
}
