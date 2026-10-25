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
        private readonly HashSet<string> _firedActivationsRegistry = new();
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
        /// Allows a rule match to activate again after it has left the network and later re-entered.
        /// </summary>
        /// <param name="ruleName">The rule associated with the match.</param>
        /// <param name="match">The match whose fired marker should be cleared.</param>
        public void ForgetActivation(string ruleName, Token match)
        {
            _firedActivationsRegistry.Remove(GenerateActivationKey(ruleName, match));
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
                var snapshotToFire = _activations
                    .Where(a => a.State == Activation.ActivationState.Pending)
                    .ToList();

                foreach (var activation in snapshotToFire)
                {
                    if (activation.State != Activation.ActivationState.Pending)
                    {
                        continue;
                    }

                    string keyFacts = GenerateActivationKey(activation);

                    // Check duplication registry
                    if (_firedActivationsRegistry.Contains(keyFacts))
                    {
                        Console.WriteLine($"[AGENDA] Skipping activation of rule '{activation.RuleName}' with facts [{keyFacts}] as it has already been fired.");
                        activation.State = Activation.ActivationState.Cancelled;
                        continue;
                    }

                    Console.WriteLine($"[AGENDA] Firing activation of rule '{activation.RuleName}' with facts [{keyFacts}] and salience {activation.Salience}.");

                    // Lock the state immediately
                    activation.State = Activation.ActivationState.Fired;
                    _firedActivationsRegistry.Add(keyFacts);

                    // This will safely append to the end of the collection
                    activation.Fire();
                }

                // If executing rule actions appended any brand-new cascading pending work 
                // into the master queue, recursively drain the next execution wave step
                // automatically.
                if (_activations.Any(a => a.State == Activation.ActivationState.Pending))
                {
                    FireAll();
                }
            }
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
                if (activation.State == Activation.ActivationState.Pending)
                {
                    // Inspect the Token's wrapped collection of domain facts.
                    // If this activation contains a reference to the fact being deleted, kill it!
                    if (activation.Match.NamedFacts.Values.Any(f => ReferenceEquals(f, retractedFact) ||
                        f.Equals(retractedFact)))
                    {
                        activation.State = Activation.ActivationState.Cancelled;
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
        /// Generates a stable key from the rule and match fact identities for activation deduplication.
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
            
            return GenerateActivationKey(activation.RuleName, activation.Match);
        }

        /// <summary>
        /// Builds a deduplication key using stable identities for reference-type facts. 
        /// </summary>
        /// <param name="ruleName">The name of the rule.</param>
        /// <param name="match">The token containing named facts.</param>
        /// <returns>A unique key representing the activation.</returns>
        private string GenerateActivationKey(string ruleName, Token match)
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
        /// The ActivationIdentity record is used to assign a unique identity to reference-type objects for the purpose
        /// of generating stable activation keys. Each time a new reference type is encountered, it is assigned a unique
        /// long value that can be used to identify it in the activation key generation process. This ensures that even
        /// if the same object instance is used in multiple activations, it can be uniquely identified and tracked.
        /// </summary>
        /// <param name="Value">The value of the unique identity.</param>
        private sealed record ActivationIdentity(long Value);
    }
}
