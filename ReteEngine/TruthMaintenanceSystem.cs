//-----------------------------------------------------------------------
// <copyright file="TruthMaintenanceSystem.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteEngine
{
    /// <summary>
    /// Manages event-driven logical justifications between rule match <see cref="Token"/>s and logically asserted facts
    /// in working memory, supporting multi-justification reference counting and transitive cascade retractions.
    /// </summary>
    /// <remarks>
    /// Subscribes to <see cref="ReteEngine.FactChangesPublished"/> to record logical assertions and to
    /// <see cref="TerminalNode.TokenRetracted"/> to remove invalidated justifications. When a logically derived fact
    /// loses its final supporting justification, <see cref="LogicalFactOrphaned"/> is raised so the engine
    /// automatically retracts the orphaned fact from working memory without requiring manual cleanup rules.
    /// </remarks>
    public sealed class TruthMaintenanceSystem
    {
        /// <summary>
        /// Uniquely identifies a rule's supporting match tuple in the Beta network.
        /// </summary>
        /// <param name="RuleName">The name of the rule that logically asserted the derived fact.</param>
        /// <param name="Match">The justifying <see cref="Token"/> that triggered the rule.</param>
        private readonly record struct JustificationKey(string RuleName, Token Match);
        /// <summary>
        /// Maps each logically asserted fact's <see cref="Guid"/> to the set of active 
        /// <see cref="JustificationKey"/> supporters currently keeping it alive in working memory.
        /// </summary>
        private readonly Dictionary<Guid, HashSet<JustificationKey>> _factJustifications = new();
        /// <summary>
        /// Reverse index mapping each active <see cref="JustificationKey"/> to the set of derived 
        /// fact <see cref="Guid"/>s it justifies, enabling fast O(1) lookup when a token is retracted.
        /// </summary>
        private readonly Dictionary<JustificationKey, HashSet<Guid>> _matchToFacts = new();

        /// <summary>
        /// Raised when a logically asserted fact loses all supporting justifications and should be retracted.
        /// </summary>
        public event Action<Guid>? LogicalFactOrphaned;

        /// <summary>
        /// Event handler for <see cref="ReteEngine.FactChangesPublished"/>.
        /// Records new logical justifications and forgets explicitly retracted facts.
        /// </summary>
        public void OnFactChangesPublished(IReadOnlyList<FactChange> batch)
        {
            foreach (var change in batch)
            {
                if (change.Kind == FactChangeTypeEnum.Assert &&
                    change.JustifyingMatch is { } match &&
                    change.Origin?.RuleName is { } ruleName)
                {
                    AddJustification(ruleName, match, change.FactId);
                }
                else if (change.Kind == FactChangeTypeEnum.Retract)
                {
                    ForgetFact(change.FactId);
                }
            }
        }

        /// <summary>
        /// Event handler invoked when a <see cref="Token"/> is retracted from a <see cref="TerminalNode"/>.
        /// </summary>
        public void OnTokenRetracted(string ruleName, Token match)
        {
            foreach (Guid orphanedId in RetractMatch(ruleName, match))
            {
                LogicalFactOrphaned?.Invoke(orphanedId);
            }
        }

        /// <summary>
        /// Records a logical justification linking <paramref name="ruleName"/> and <paramref name="match"/> 
        /// to the derived fact identified by <paramref name="factId"/>.
        /// </summary>
        /// <param name="ruleName">The name of the rule performing the logical assertion.</param>
        /// <param name="match">The justifying <see cref="Token"/>.</param>
        /// <param name="factId">The stable <see cref="Guid"/> of the logically asserted fact.</param>
        /// <returns>
        /// <see langword="true"/> if this is the first justification for <paramref name="factId"/>; 
        /// <see langword="false"/> if the fact was already supported by another active justification.
        /// </returns>
        public bool AddJustification(string ruleName, Token match, Guid factId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
            ArgumentNullException.ThrowIfNull(match);

            var key = new JustificationKey(ruleName, match);

            if (!_factJustifications.TryGetValue(factId, out var supporters))
            {
                supporters = new HashSet<JustificationKey>();
                _factJustifications[factId] = supporters;
            }
            bool isFirstJustification = supporters.Count == 0;
            supporters.Add(key);

            if (!_matchToFacts.TryGetValue(key, out var derivedFacts))
            {
                derivedFacts = new HashSet<Guid>();
                _matchToFacts[key] = derivedFacts;
            }
            derivedFacts.Add(factId);

            return isFirstJustification;
        }

        /// <summary>
        /// Removes all logical justifications backed by <paramref name="ruleName"/> and <paramref name="match"/>, 
        /// returning the <see cref="Guid"/>s of any derived facts that now have zero remaining justifications 
        /// and should be retracted from working memory.
        /// </summary>
        /// <param name="ruleName">The name of the rule whose token was retracted.</param>
        /// <param name="match">The retracted <see cref="Token"/>.</param>
        /// <returns>
        /// A list of orphaned fact <see cref="Guid"/>s that lost their final supporting justification.
        /// </returns>
        public IReadOnlyList<Guid> RetractMatch(string ruleName, Token match)
        {
            var key = new JustificationKey(ruleName, match);
            if (!_matchToFacts.Remove(key, out var derivedFactIds))
            {
                return Array.Empty<Guid>();
            }

            var factsToRetract = new List<Guid>();
            foreach (var factId in derivedFactIds)
            {
                if (_factJustifications.TryGetValue(factId, out var supporters))
                {
                    supporters.Remove(key);
                    if (supporters.Count == 0)
                    {
                        _factJustifications.Remove(factId);
                        factsToRetract.Add(factId);
                    }
                }
            }

            return factsToRetract;
        }

        /// <summary>
        /// Removes all justification tracking for a fact that was explicitly retracted outside of 
        /// automatic truth maintenance.
        /// </summary>
        /// <param name="factId">The stable <see cref="Guid"/> of the retracted fact.</param>
        public void ForgetFact(Guid factId)
        {
            if (_factJustifications.Remove(factId, out var supporters))
            {
                foreach (var key in supporters)
                {
                    if (_matchToFacts.TryGetValue(key, out var facts))
                    {
                        facts.Remove(factId);
                        if (facts.Count == 0)
                        {
                            _matchToFacts.Remove(key);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Clears all tracked justifications and reverse indexes.
        /// </summary>
        public void Clear()
        {
            _factJustifications.Clear();
            _matchToFacts.Clear();
        }
    }
}
