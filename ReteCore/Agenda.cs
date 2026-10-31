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
        /// An accessor for the list of pending activations in the agenda. This property returns a read-only enumerable collection 
        /// of Activation objects.
        /// </summary>
        public IEnumerable<Activation> Activations => _activations.AsReadOnly();

        /// <summary>
        /// Adds an activation unless the tuple is already pending or has fired at its current fact revision.
        /// </summary>
        /// <param name="activation">The Activation object to add to the list of activations.</param>
        public void Add(Activation activation)
        {
            string tupleKey = GenerateTupleKey(activation.RuleName, activation.Match);
            if (_activations.Any(existing => existing.State == Activation.ActivationState.Pending &&
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
        public int RemoveByRuleAndMatch(string ruleName, Token match)
        {
            string tupleKey = GenerateTupleKey(ruleName, match);
            int removedCount = _activations.RemoveAll(activation =>
                activation.State == Activation.ActivationState.Pending &&
                activation.RuleName == ruleName &&
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
        public int RemoveByRuleAndFact(string ruleName, object fact)
        {
            int removedCount = _activations.RemoveAll(activation =>
                activation.State == Activation.ActivationState.Pending &&
                activation.RuleName == ruleName &&
                activation.Match.NamedFacts.Values.Any(value => ReferenceEquals(value, fact)));

            if (removedCount > 0)
            {
                Console.WriteLine($"[AGENDA] Cancelled {removedCount} pending activations for rule '{ruleName}'.");
            }
            return removedCount;
        }

        /// <summary>
        /// Clears fired-revision markers when a match leaves the network so a later match can activate again.
        /// </summary>
        /// <param name="ruleName">The rule associated with the match.</param>
        /// <param name="match">The match whose fired marker should be cleared.</param>
        public void ForgetActivation(string ruleName, Token match)
        {
            string keyPrefix = $"{GenerateTupleKey(ruleName, match)}|revision:";
            _firedActivationsRegistry.RemoveWhere(key => key.StartsWith(keyPrefix, StringComparison.Ordinal));
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
            string revisionParts = string.Join(",", match.NamedFacts
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={GetRevisionKey(pair.Value)}"));
            return $"{GenerateTupleKey(ruleName, match)}|revision:{revisionParts}";
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
        /// The ActivationIdentity record is used to assign a unique identity to reference-type objects for the purpose
        /// of generating stable activation keys. Each time a new reference type is encountered, it is assigned a unique
        /// long value that can be used to identify it in the activation key generation process. This ensures that even
        /// if the same object instance is used in multiple activations, it can be uniquely identified and tracked.
        /// </summary>
        /// <param name="Value">The value of the unique identity.</param>
        private sealed record ActivationIdentity(long Value);
    }
}
