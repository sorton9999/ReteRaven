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
        /// currently empty.
        /// </summary>
        public bool HasActivations => _activations.Count > 0;

        /// <summary>
        /// Adds a new activation to the agenda. This method is typically called when a rule's conditions are satisfied, 
        /// creating an activation that represents the pending execution of that rule. The activation is added to the 
        /// list of pending activations, which will be processed later during the firing phase.
        /// </summary>
        /// <param name="activation">The Activation object to add to the list of activations.</param>
        public void Add(Activation activation)
        {
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
        /// salience (priority). The activations are removed from the agenda as they are fired. Activated rules are 
        /// fired based on their priority, ensuring that higher salience rules are executed before lower salience 
        /// ones when multiple activations are present.
        /// </summary>
        public void FireAll()
        {
            if (!HasActivations) { return; }
            var activationToFire = PopNext();
            activationToFire?.Fire();
        }

        /// <summary>
        /// This method retrieves and removes the next activation to fire from the agenda based on descending salience (priority).
        /// </summary>
        /// <returns>The next activation to fire, or null if no activations are available.</returns>
        private Activation PopNext()
        {
            var sorted =  _activations.OrderByDescending(a => a.Salience).ToList();
            if (sorted.Count == 0) return null;
            var next = sorted[0];
            _activations.Remove(next);
            return next;
        }
    }
}
