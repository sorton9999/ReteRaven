// -----------------------------------------------------------------------
// <copyright file="AllNode.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReteCore
{
    /// <summary>
    /// Aggregates all matching right-side facts for each left token and emits
    /// a derived token with a named collection under the specified alias.
    /// </summary>
    public class AllNode : IReteNode
    {
        private readonly List<object> _rightMemory = new List<object>();
        private readonly List<IReteNode> _successors = new List<IReteNode>();
        private readonly Func<Token, object, bool> _joinConstraint;
        private readonly string _alias;
        private readonly string _nodeName;

        // Track for each left token the current aggregated Token we propagated downstream
        private readonly Dictionary<Token, Token> _propagated = new Dictionary<Token, Token>();
        // Keep the set of left tokens seen (to compute aggregates when right memory changes)
        private readonly Dictionary<Token, List<object>> _leftMatches = new Dictionary<Token, List<object>>();

        public AllNode(string alias, Func<Token, object, bool> joinConstraint = null)
        {
            _alias = alias ?? throw new ArgumentNullException(nameof(alias));
            _joinConstraint = joinConstraint ?? ((t, f) => true);
            _nodeName = $"All<{alias}>";
        }

        public IEnumerable<IReteNode> Successors => _successors;
        public IReteNode? Parent { get; set; }

        public void AddSuccessor(IReteNode node)
        {
            node.Parent = this;
            _successors.Add(node);

            // When a new successor is added refresh with current valid aggregated tokens
            foreach (var keyVal in _leftMatches)
            {
                // Create aggregated token from stored match list
                var aggregated = CreateAggregatedToken(keyVal.Key, keyVal.Value);
                node.Assert(aggregated);
            }
        }

        public void RemoveSuccessor(IReteNode successor) => _successors.Remove(successor);

        public void Assert(object factOrToken)
        {
            if (factOrToken is Token token)
            {
                AssertLeft(token);
            }
            else
            {
                AssertRight(factOrToken);
            }
        }

        private void AssertLeft(Token token)
        {
            // Compute matching facts from right memory
            var matches = new List<object>();
            foreach (var fact in _rightMemory)
            {
                if (_joinConstraint(token, fact)) { matches.Add(fact); }
            }
            _leftMatches[token] = matches;

            // Create aggregated token and propagate
            var aggregated = CreateAggregatedToken(token, matches);
            _propagated[token] = aggregated;
            PropagateAssert(aggregated);
        }

        private void AssertRight(object fact)
        {
            _rightMemory.Add(fact);

            // For each left token that matches this new fact, update its aggregated token
            foreach (var token in new List<Token>(_leftMatches.Keys))
            {
                if (_joinConstraint(token, fact))
                {
                    var list = _leftMatches[token];
                    list.Add(fact);

                    // Replace propagated token with new one (retract old, assert new)
                    if (_propagated.TryGetValue(token, out Token? oldToken))
                    {
                        PropagateRetract(oldToken);
                    }
                    var newToken = CreateAggregatedToken(token, list);
                    _propagated[token] = newToken;
                    PropagateAssert(newToken);
                }
            }
        }

        public void Retract(object factOrToken)
        {
            if (factOrToken is Token token)
            {
                RetractLeft(token);
            }
            else
            {
                RetractRight(factOrToken);
            }
        }

        private void RetractLeft(Token token)
        {
            if (_leftMatches.ContainsKey(token))
            {
                // Retract propagated aggregated token if any
                if (_propagated.TryGetValue(token, out Token? agg))
                {
                    PropagateRetract(agg);
                    _propagated.Remove(token);
                }
                _leftMatches.Remove(token);
            }
        }

        private void RetractRight(object fact)
        {
            if (_rightMemory.Remove(fact))
            {
                // For each left token that included this fact, remove and replace propagated token
                foreach (var token in new List<Token>(_leftMatches.Keys))
                {
                    var list = _leftMatches[token];
                    bool removed = list.Remove(fact);
                    if (removed)
                    {
                        if (_propagated.TryGetValue(token, out Token? oldToken))
                        {
                            PropagateRetract(oldToken);
                        }
                        var newToken = CreateAggregatedToken(token, list);
                        _propagated[token] = newToken;
                        PropagateAssert(newToken);
                    }
                }
            }
        }

        public void Refresh(object factOrToken, string propertyName)
        {
            // For our purposes, simply delegate: if token, refresh left; if fact, refresh right
            if (factOrToken is Token token) RefreshLeft(token);
            else RefreshRight(factOrToken, propertyName);
        }

        private void RefreshLeft(Token token)
        {
            if (!_leftMatches.ContainsKey(token))
            {
                AssertLeft(token);
            }
            else
            {
                // Recompute matches in case join constraint depends on external state
                var matches = new List<object>();
                foreach (var fact in _rightMemory)
                {
                    if (_joinConstraint(token, fact)) { matches.Add(fact); }
                }
                // Replace propagated token
                if (_propagated.TryGetValue(token, out Token? old))
                {
                    PropagateRetract(old);
                }
                var newToken = CreateAggregatedToken(token, matches);
                _leftMatches[token] = matches;
                _propagated[token] = newToken;
                PropagateAssert(newToken);
            }
        }

        private void RefreshRight(object fact, string propertyName)
        {
            // For simplicity, treat as a retract then assert to ensure aggregated tokens are updated.
            RetractRight(fact);
            AssertRight(fact);
        }

        private Token CreateAggregatedToken(Token leftToken, List<object> matches)
        {
            // Store as a read-only list to avoid external mutation
            var snapshot = matches.ToList().AsReadOnly();
            return new Token(leftToken, _alias, snapshot);
        }

        private void PropagateAssert(Token token) => _successors.ForEach(s => s.Assert(token));
        private void PropagateRetract(Token token) => _successors.ForEach(s => s.Retract(token));

        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            Console.WriteLine($"{indent}AllNode:[{_nodeName}] LeftTokens:{_leftMatches.Count}, RightFacts:{_rightMemory.Count}");
            foreach (var kv in _leftMatches)
            {
                Console.WriteLine($"{indent}  LeftToken: {kv.Key}, Matches: {kv.Value.Count}");
            }
        }
    }
}