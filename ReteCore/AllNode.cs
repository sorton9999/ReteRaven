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
    /// Aggregates all matching right-side facts for each left token and emits a derived token with 
    /// a named collection under the specified alias.
    /// </summary>
    public class AllNode : IReteNode
    {
        /// <summary>
        /// Stores all facts asserted on the right side of this node. This memory is used to compute the 
        /// aggregated tokens for each left token.
        /// </summary>
        private readonly List<object> _rightMemory = new List<object>();
        /// <summary>
        /// Stores the successor nodes that will receive the aggregated tokens produced by this AllNode. 
        /// When a new successor is added to this AllNode, it will immediately receive all existing 
        /// aggregated tokens through the Assert method, ensuring that the new node is up-to-date with 
        /// the current state of aggregated tokens. The order of successors in the list may affect the 
        /// order in which aggregated tokens are propagated to them, but does not affect the logic of 
        /// the network.
        /// </summary>
        private readonly List<IReteNode> _successors = new List<IReteNode>();
        /// <summary>
        /// This is the join constraint function that determines whether a given left token and right fact 
        /// should be considered a match.
        /// </summary>
        private readonly Func<Token, object, bool> _joinConstraint;
        /// <summary>
        /// The alias under which the aggregated collection of matching right facts will be stored in the 
        /// derived token.
        /// </summary>
        private readonly string _alias;
        /// <summary>
        /// The name of this node, used for debugging purposes. It is derived from the alias to provide 
        /// context when printing the state of the node.
        /// </summary>
        private readonly string _nodeName;
        /// <summary>
        /// Tracks the currently propagated aggregated token for each left token. This allows the node to retract 
        /// the old aggregated token and assert a new one when the right memory changes (e.g., when a new fact is 
        /// added or removed that affects the matches for a left token). By keeping this mapping, the AllNode can 
        /// ensure that it maintains an accurate representation of the current state of matches for each left token 
        /// and can properly update its successors when changes occur in the right memory.
        /// </summary>
        private readonly Dictionary<Token, Token> _propagated = new Dictionary<Token, Token>();
        /// <summary>
        /// Stores the list of matching right facts for each left token. This allows the node to recompute the 
        /// aggregated token for a left token when the right memory changes, without needing to re-evaluate the join 
        /// constraint for all facts. When a new fact is added to the right memory, the node can simply check which 
        /// left tokens are affected by this new fact (i.e., which left tokens have a join constraint that matches 
        /// this fact) and update their corresponding match lists. Similarly, when a fact is removed from the right 
        /// memory, the node can quickly identify which left tokens had this fact in their match list and update 
        /// accordingly. This structure is essential for maintaining the integrity of the aggregated tokens and 
        /// ensuring that the node can efficiently respond to changes in the right memory without needing to reprocess 
        /// all left tokens from scratch.
        /// </summary>
        private readonly Dictionary<Token, List<object>> _leftMatches = new Dictionary<Token, List<object>>();

        /// <summary>
        /// The constructor for the AllNode class. It initializes the node with a specified alias for the aggregated 
        /// collection and an optional join constraint function. The alias is used to identify the collection of matching 
        /// right facts in the derived token that this node produces. The join constraint function is used to determine 
        /// whether a given left token and right fact should be considered a match when computing the aggregated tokens. 
        /// If no join constraint is provided, it defaults to a function that always returns true, meaning that all right 
        /// facts will be considered matches for any left token. The constructor also sets the node name for debugging 
        /// purposes, incorporating the alias to provide context when printing the state of the node.
        /// </summary>
        /// <param name="alias">The alias for the aggregated collection of matching right facts.</param>
        /// <param name="joinConstraint">The optional join constraint function to determine matches between left tokens and right facts.</param>
        /// <exception cref="ArgumentNullException">Thrown when the alias is null.</exception>
        public AllNode(string alias, Func<Token, object, bool> joinConstraint = null)
        {
            _alias = alias ?? throw new ArgumentNullException(nameof(alias));
            _joinConstraint = joinConstraint ?? ((t, f) => true);
            _nodeName = $"All<{alias}>";
        }

        /// <summary>
        /// The list of successor nodes that will receive the aggregated tokens produced by this AllNode.
        /// </summary>
        public IEnumerable<IReteNode> Successors => _successors;

        /// <summary>
        /// The parent node in the Rete network. This property allows for navigation back up the network from this node.
        /// </summary>
        public IReteNode? Parent { get; set; }

        /// <summary>
        /// Adds a successor node to this AllNode. When a new successor is added, it immediately receives all existing 
        /// aggregated tokens through the Assert method, ensuring that the new node is up-to-date with the current state 
        /// of the network.
        /// </summary>
        /// <param name="node">The successor node to add.</param>
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

        /// <summary>
        /// Removes a successor node from this AllNode. This method does not perform any additional cleanup or state 
        /// management for the removed successor.
        /// </summary>
        /// <param name="successor">The successor node to remove.</param>
        public void RemoveSuccessor(IReteNode successor) => _successors.Remove(successor);

        /// <summary>
        /// Adds a fact or token to this AllNode. If the input is a token, it is treated as a left token and the node computes 
        /// the matching right facts from the right memory to create an aggregated token, which is then propagated to the 
        /// successors. If the input is a fact, it is added to the right memory, and the node updates any existing left tokens 
        /// that match this new fact by creating new aggregated tokens and propagating them to the successors. This method 
        /// ensures that the state of the AllNode is consistent with the new information being asserted, and that all relevant 
        /// matches are updated accordingly.
        /// </summary>
        /// <param name="factOrToken">The fact or token to assert.</param>
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

        /// <summary>
        /// This method computes the matching facts from the right memory for the given left token using the join constraint function. 
        /// It then creates an aggregated token that includes the left token and the list of matching right facts under the specified 
        /// alias. Finally, it propagates this aggregated token to all successor nodes. This process ensures that the AllNode correctly 
        /// aggregates and propagates information based on the left tokens and their corresponding matches in the right memory.
        /// </summary>
        /// <param name="token">The left token to assert.</param>
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

        /// <summary>
        /// Asserts a new fact on the right side. This method adds the fact to the right memory and then updates any existing left tokens 
        /// that match this new fact. For each left token that matches the new fact according to the join constraint function, the method 
        /// updates the list of matching facts for that token and creates a new aggregated token. It then retracts the old aggregated 
        /// token (if any) and asserts the new one to the successors. This ensures that the state of the AllNode remains consistent with 
        /// the new information being added to the right memory, and that all relevant matches are updated accordingly.
        /// </summary>
        /// <param name="fact">The fact to assert on the right side.</param>
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

        /// <summary>
        /// Removes a fact or token from this AllNode. If the input is a token, it is treated as a left token and the node retracts any propagated 
        /// aggregated token associated with it, and removes the left token from the matches. If the input is a fact, it is removed from the right 
        /// memory, and the node updates any existing left tokens that included this fact in their matches by creating new aggregated tokens without 
        /// this fact and propagating them to the successors. This method ensures that the state of the AllNode remains consistent with the removal 
        /// of information, and that all relevant matches are updated accordingly.
        /// </summary>
        /// <param name="factOrToken">The fact or token to retract.</param>
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

        /// <summary>
        /// The method for retracting a left token. It checks if the token exists in the left matches, and if so, it retracts any propagated aggregated 
        /// token associated with it and removes the left token from the matches. This ensures that when a left token is retracted, any derived 
        /// information based on that token is also retracted from the successors, maintaining the integrity of the network's state.
        /// </summary>
        /// <param name="token">The left token to retract.</param>
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

        /// <summary>
        /// The method for retracting a right fact. It checks if the fact exists in the right memory, and if so, it removes the fact from the right memory 
        /// and updates any existing left tokens that included this fact in their matches. For each left token that had this fact in its match list, the 
        /// method creates a new aggregated token without this fact and propagates it to the successors. This ensures that when a right fact is retracted, 
        /// any derived information based on that fact is also updated in the successors, maintaining the integrity of the network's state.
        /// </summary>
        /// <param name="fact">The fact to retract.</param>
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

        /// <summary>
        /// This method is used to refresh the state of the AllNode when external changes occur that may affect the join constraints. If the input is a 
        /// token, it is treated as a left token and the node recomputes the matching right facts from the right memory to create a new aggregated token, 
        /// which is then propagated to the successors. If the input is a fact, it is treated as a right fact and the node updates any existing left tokens 
        /// that match this fact by creating new aggregated tokens and propagating them to the successors. This method ensures that the AllNode can respond 
        /// to changes in external state that may affect the join constraints, and that all relevant matches are updated accordingly to maintain the 
        /// integrity of the network's state.
        /// </summary>
        /// <param name="factOrToken">The fact or token to refresh.</param>
        /// <param name="propertyName">The name of the property that changed, if applicable.</param>
        public void Refresh(object factOrToken, string propertyName)
        {
            // For our purposes, simply delegate: if token, refresh left; if fact, refresh right
            if (factOrToken is Token token) RefreshLeft(token);
            else RefreshRight(factOrToken, propertyName);
        }

        /// <summary>
        /// The method for refreshing a left token. It checks if the token exists in the left matches, and if so, it recomputes the matching right facts from 
        /// the right memory using the join constraint function. It then creates a new aggregated token with the updated matches and propagates it to the 
        /// successors, replacing the old aggregated token if one exists. This ensures that when a left token is refreshed, any derived information based on that 
        /// token is also updated in the successors, maintaining the integrity of the network's state in response to changes that may affect the join constraints.
        /// </summary>
        /// <param name="token">The left token to refresh.</param>
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

        /// <summary>
        /// The method for refreshing a right fact. It checks if the fact exists in the right memory, and if so, it updates any existing left tokens that match 
        /// this fact by creating new aggregated tokens and propagating them to the successors. This ensures that when a right fact is refreshed, any derived 
        /// information based on that fact is also updated in the successors, maintaining the integrity of the network's state in response to changes that may 
        /// affect the join constraints. For simplicity, this implementation treats the refresh as a retract followed by an assert, which ensures that all relevant 
        /// matches are updated accordingly without needing to implement more complex logic to determine which left tokens are affected by the change in the right 
        /// fact. This approach guarantees that the state of the AllNode remains consistent with the refreshed information, even if it may be less efficient than a 
        /// more targeted update in cases where only a few left tokens are affected by the change in the right fact.
        /// </summary>
        /// <param name="fact">The fact to refresh.</param>
        /// <param name="propertyName">The name of the property that changed.</param>
        private void RefreshRight(object fact, string propertyName)
        {
            // For simplicity, treat as a retract then assert to ensure aggregated tokens are updated.
            RetractRight(fact);
            AssertRight(fact);
        }

        /// <summary>
        /// Creates an aggregated token that includes the left token and the list of matching right facts under the specified alias. The aggregated token is a new 
        /// token that derives from the left token and contains the collection of matching right facts as a named fact under the alias. This method is used to generate 
        /// the tokens that will be propagated to the successors based on the left tokens and their corresponding matches in the right memory. By creating a new token 
        /// for each aggregation, the AllNode can maintain a clear and consistent representation of the state of matches for each left token, and ensure that any changes 
        /// in the right memory are properly reflected in the derived tokens that are propagated to the successors.
        /// </summary>
        /// <param name="leftToken">The left token to aggregate.</param>
        /// <param name="matches">The list of matching right facts.</param>
        /// <returns>The aggregated token.</returns>
        private Token CreateAggregatedToken(Token leftToken, List<object> matches)
        {
            // Store as a read-only list to avoid external mutation
            var snapshot = matches.ToList().AsReadOnly();
            return new Token(leftToken, _alias, snapshot);
        }

        /// <summary>
        /// Sends the given token to all successor nodes by calling their Assert method. This method is used to propagate the aggregated tokens produced by this AllNode to 
        /// its successors. The token passed to this method is typically an aggregated token that includes a left token and its corresponding matches from the right memory. 
        /// </summary>
        /// <param name="token">The token to propagate.</param>
        private void PropagateAssert(Token token) => _successors.ForEach(s => s.Assert(token));

        /// <summary>
        /// Removes the given token from all successor nodes by calling their Retract method. This method is used to retract the aggregated tokens produced by this AllNode 
        /// from its successors when the underlying matches change (e.g., when a new fact is added or removed from the right memory that affects the matches for a left token). 
        /// </summary>
        /// <param name="token">The token to retract.</param>
        private void PropagateRetract(Token token) => _successors.ForEach(s => s.Retract(token));

        /// <summary>
        /// Prints the current state of the AllNode for debugging purposes. This method outputs the name of the node, the number of left tokens and right facts currently stored, and 
        /// the details of each left token and its corresponding matches.
        /// </summary>
        /// <param name="fact">The fact to debug.</param>
        /// <param name="level">The indentation level for the debug output.</param>
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