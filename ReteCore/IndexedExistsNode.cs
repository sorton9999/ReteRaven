//-----------------------------------------------------------------------
// <copyright file="IndexedExistsNode.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The IndexedExistsNode class represents a node in a Rete network that efficiently implements the
    /// "exists" condition by maintaining indexed collections of tokens and facts.  The left side of the
    /// node (tokens) is indexed by a key extracted from the tokens, while the right side (facts) is 
    /// indexed by a key extracted from the facts.
    /// </summary>
    public class IndexedExistsNode : IReteNode
    {
        /// <summary>
        /// Stores tokens from the left side of the node, indexed by a key extracted from each token.  
        /// This allows for O(1) lookups when a fact is asserted on the right side, enabling efficient 
        /// matching of tokens to facts based on the join key.  Each key maps to a list of tokens that 
        /// share the same key value, which is essential for implementing the "exists" condition in the 
        /// Rete algorithm.
        /// </summary>
        private readonly Dictionary<object, List<Token>> leftIndex = new Dictionary<object, List<Token>>();
        /// <summary>
        /// Stores facts from the right side of the node, indexed by a key extracted from each fact.  
        /// This allows for O(1) lookups when a token is asserted on the left side, enabling efficient 
        /// matching of facts to tokens based on the join key.  Each key maps to a list of facts that 
        /// share the same key value, which is essential for implementing the "exists" condition in the 
        /// Rete algorithm.
        /// </summary>
        private readonly Dictionary<object, List<object>> rightIndex = new Dictionary<object, List<object>>();
        /// <summary>
        /// Stores the count of matches for each token on the left side.  This is used to determine when 
        /// to propagate assertions and retractions to successor nodes.  When a token is asserted on the 
        /// left side, it checks for matches on the right side and updates this count accordingly.  If 
        /// the count transitions from 0 to 1, it means the "exists" condition is satisfied for that 
        /// token, and an assertion is propagated to successors.  If the count transitions from 1 to 0, 
        /// it means the "exists" condition is no longer satisfied, and a retraction is propagated to 
        /// successors.
        /// </summary>
        private readonly Dictionary<Token, int> matchCounts = new Dictionary<Token, int>();
        /// <summary>
        /// The list of tokens that have been asserted on the left side of the node.  This list is used to
        /// keep track of all tokens that are currently being processed by the node, allowing for efficient
        /// management of assertions, retractions, and refreshes.
        /// </summary>
        private readonly List<Token> leftTokens = new();
        /// <summary>
        /// The list of facts that have been asserted on the right side of the node.  This list is used to 
        /// keep track of all facts that are currently being processed by the node, allowing for efficient
        /// management of assertions, retractions, and refreshes.
        /// </summary>
        private readonly List<object> rightFacts = new();
        /// <summary>
        /// Stores the successor nodes that will receive propagated assertions and retractions when 
        /// matches are found or lost.  Each successor is an IReteNode that will be affected by 
        /// 
        private readonly List<IReteNode> _successors = new List<IReteNode>();
        /// <summary>
        /// The function used to extract the join key from tokens on the left side of the node.  This 
        /// selector is provided when the node is constructed and is used to determine how tokens are 
        /// indexed and matched against facts on the right side.  The selector takes a Token as input 
        /// and returns an object that represents the key used for indexing and matching.
        /// </summary>
        private readonly Func<Token, object> leftKeySelector;
        /// <summary>
        /// The function used to extract the join key from facts on the right side of the node.  This 
        /// selector is provided when the node is constructed and is used to determine how facts are 
        /// indexed and matched against tokens on the left side.  The selector takes an object (fact) 
        /// as input and returns an object that represents the key used for indexing and matching.
        /// </summary>
        private readonly Func<object, object> rightKeySelector;
        /// <summary>
        /// The name of the node, used for identification and debugging purposes.
        /// </summary>
        private readonly string nodeName;

        /// <summary>
        /// The constructor for the IndexedExistsNode takes a name for identification, and two selector 
        /// functions that extract the join key from tokens and facts.
        /// </summary>
        /// <param name="name">The name of this node as identification.</param>
        /// <param name="leftKey">The left token selector function.</param>
        /// <param name="rightKey">The right fact selector function.</param>
        public IndexedExistsNode(string name, Func<Token, object> leftKey, Func<object, object> rightKey)
        {
            leftKeySelector = leftKey;
            rightKeySelector = rightKey;
            nodeName = name;
        }

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
        /// Adds a successor node to this IndexedExistsNode.  Successors will receive propagated 
        /// assertions and retractions when matches are found or lost.
        /// </summary>
        /// <param name="node">The node to add this node to as a successor.</param>
        public void AddSuccessor(IReteNode node) => _successors.Add(node);

        /// <summary>
        /// The Assert method takes either a Token (from the left side) or a fact (from the right side) 
        /// and updates the internal indexes accordingly.  It then propagates assertions to successors 
        /// if matches are found.
        /// </summary>
        /// <param name="factOrToken">The fact or token this operation is acting upon.</param>
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
        /// Tracks a left token once, indexes its key, and asserts it when a right-side match exists.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void AssertLeft(Token token)
        {
            if (leftTokens.Any(existing => existing.Equals(token))) return;
            leftTokens.Add(token);
            var key = leftKeySelector(token);

            // Store in index
            if (!leftIndex.ContainsKey(key)) leftIndex[key] = new List<Token>();
            leftIndex[key].Add(token);

            // Lookup matches on the Right using the key (O(1) lookup!)
            int count = 0;
            if (rightIndex.TryGetValue(key, out var matchingFacts))
            {
                count = matchingFacts.Count;
            }

            matchCounts[token] = count;
            if (count > 0) { PropagateAssert(token); }
        }

        /// <summary>
        /// Tracks a right fact once and asserts tokens whose match count changes from zero to nonzero.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void AssertRight(object fact)
        {
            if (rightFacts.Any(existing => ReferenceEquals(existing, fact))) return;
            rightFacts.Add(fact);
            var key = rightKeySelector(fact);

            // Store in index
            if (!rightIndex.ContainsKey(key)) rightIndex[key] = new List<object>();
            rightIndex[key].Add(fact);

            // Find only the tokens that care about this specific key
            if (leftIndex.TryGetValue(key, out var matchingTokens))
            {
                foreach (var token in matchingTokens)
                {
                    int oldCount = matchCounts[token];
                    matchCounts[token] = oldCount + 1;

                    if (oldCount == 0) { PropagateAssert(token); }
                }
            }
        }

        /// <summary>
        /// The Retract method takes either a Token (from the left side) or a fact (from the right side)
        /// and updates the internal indexes accordingly.  It then propagates retractions to successors
        /// if matches are lost.
        /// </summary>
        /// <param name="factOrToken">The fact or token this operation is acting upon.</param>
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
        /// Removes a tracked token from its index buckets and retracts it only if it was active. This method ensures that the internal state
        /// of the node remains consistent after a left token has been retracted.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void RetractLeft(Token token)
        {
            var storedToken = leftTokens.FirstOrDefault(existing => ReferenceEquals(existing, token));
            if (storedToken == null) return;
            leftTokens.Remove(storedToken);
            foreach (var list in leftIndex.Values) list.RemoveAll(existing => ReferenceEquals(existing, storedToken));
            RemoveEmptyBuckets(leftIndex);
            if (matchCounts.TryGetValue(token, out var count) && count > 0)
            {
                matchCounts.Remove(token);
                PropagateRetract(token);
            }
            else
            {
                matchCounts.Remove(token);
            }
        }

        /// <summary>
        /// Removes a tracked right fact and reconciles left-side match counts against remaining facts. This method ensures that the internal state
        /// of the node remains consistent after a right fact has been retracted.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void RetractRight(object fact)
        {
            var storedFact = rightFacts.FirstOrDefault(existing => ReferenceEquals(existing, fact));
            if (storedFact == null) return;
            rightFacts.Remove(storedFact);
            foreach (var list in rightIndex.Values) list.RemoveAll(existing => ReferenceEquals(existing, storedFact));
            RemoveEmptyBuckets(rightIndex);
            RebuildIndexesAndReconcile();
        }

        /// <summary>
        /// Rebuilds the indexes from current keys and propagates only changed existence states. This method is called when a
        /// fact or token has been updated, and it ensures that the internal state of the node remains consistent with the
        /// current data. It checks for changes in match counts and propagates assertions or retractions to successor nodes as
        /// needed.
        /// </summary>
        /// <param name="fact">The refreshed fact or left token.</param>
        /// <param name="propertyName">The name of the property in the fact cell that is being updated.</param>
        public void Refresh(object fact, string propertyName)
        {
            if (fact is Token token)
            {
                if (!leftTokens.Any(existing => ReferenceEquals(existing, token)))
                {
                    AssertLeft(token);
                    return;
                }
            }
            else if (!rightFacts.Any(existing => ReferenceEquals(existing, fact)))
            {
                return;
            }

            RebuildIndexesAndReconcile(propertyName);
        }

        /// <summary>
        /// Rebuilds both key indexes and reconciles each token's current match count. This method ensures that the internal state
        /// of the node remains consistent after changes to the left or right input.
        /// </summary>
        /// <param name="propertyName">The name of the property that triggered the refresh, if any.</param>
        private void RebuildIndexesAndReconcile(string? propertyName = null)
        {
            var previousCounts = leftTokens.ToDictionary(token => token, token => matchCounts.TryGetValue(token, out var count) ? count : 0);
            leftIndex.Clear();
            rightIndex.Clear();

            foreach (var token in leftTokens)
            {
                var key = leftKeySelector(token);
                if (!leftIndex.TryGetValue(key, out var tokens)) leftIndex[key] = tokens = new List<Token>();
                tokens.Add(token);
            }

            foreach (var fact in rightFacts)
            {
                var key = rightKeySelector(fact);
                if (!rightIndex.TryGetValue(key, out var facts)) rightIndex[key] = facts = new List<object>();
                facts.Add(fact);
            }

            foreach (var token in leftTokens)
            {
                var key = leftKeySelector(token);
                int oldCount = previousCounts[token];
                int newCount = rightIndex.TryGetValue(key, out var facts) ? facts.Count : 0;
                matchCounts[token] = newCount;

                if (oldCount == 0 && newCount > 0) PropagateAssert(token);
                else if (oldCount > 0 && newCount == 0) PropagateRetract(token);
                else if (propertyName != null && oldCount > 0 && newCount > 0)
                {
                    foreach (var successor in _successors) successor.Refresh(token, propertyName);
                }
            }
        }

        /// <summary>
        /// Removes index buckets that became empty after a retraction. This is important to keep the index dictionaries clean and efficient,
        /// preventing unnecessary memory usage and ensuring that lookups remain fast. The method iterates through the index dictionary,
        /// identifies keys whose associated lists are empty, and removes those keys from the dictionary.
        /// </summary>
        /// <typeparam name="TKey">The type of the keys in the index.</typeparam>
        /// <typeparam name="TValue">The type of the values in the index.</typeparam>
        /// <param name="index">The dictionary representing the index from which empty buckets should be removed.</param>
        private static void RemoveEmptyBuckets<TKey, TValue>(Dictionary<TKey, List<TValue>> index) where TKey : notnull
        {
            foreach (var key in index.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key).ToList())
            {
                index.Remove(key);
            }
        }

        /// <summary>
        /// Removes a successor node from this OrNode. This method is used when a successor node is no longer needed or when the 
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
        }

        /// <summary>
        /// Propagates an assertion to all successor nodes.  This is called when a token on the left 
        /// side finds a match on the right side, indicating that the "exists" condition is satisfied 
        /// for that token.
        /// </summary>
        /// <param name="token">The token to send to each successor.</param>
        private void PropagateAssert(Token token) => _successors.ForEach(s => s.Assert(token));

        /// <summary>
        /// Propagates a retraction to all successor nodes.  This is called when a token on the left 
        /// side loses its last match on the right side, indicating that the "exists" condition is no 
        /// longer satisfied for that token.
        /// </summary>
        /// <param name="token">The token to retract from each successor.</param>
        private void PropagateRetract(Token token) => _successors.ForEach(s => s.Retract(token));

        /// <summary>
        /// Prints the internal state of the node for debugging purposes.  It shows the current tokens
        /// and facts in the indexes, as well as the matches for each token.  This can be useful for 
        /// understanding how the node is processing assertions and retractions.
        /// </summary>
        /// <param name="fact">The fact whose information is to be output.</param>
        /// <param name="level">A level of indentation.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            Console.WriteLine($"{indent}IndexedExistsNode:[{nodeName}] Fact: {fact}");
            foreach (var child in _successors) { child.DebugPrint(fact, level + 1); }
        }
    }
}
