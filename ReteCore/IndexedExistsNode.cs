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
        /// Stores the successor nodes that will receive propagated assertions and retractions when 
        /// matches are found or lost.  Each successor is an IReteNode that will be affected by 
        /// 
        private readonly List<IReteNode> successors = new List<IReteNode>();
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
        /// Adds a successor node to this IndexedExistsNode.  Successors will receive propagated 
        /// assertions and retractions when matches are found or lost.
        /// </summary>
        /// <param name="node">The node to add this node to as a successor.</param>
        public void AddSuccessor(IReteNode node) => successors.Add(node);

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
        /// Asserts a token on the left side of the node.  It updates the left index and checks for 
        /// matches on the right side using the extracted key.  If matches are found, it propagates 
        /// assertions to successors.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void AssertLeft(Token token)
        {
            var key = leftKeySelector(token);

            // 1. Store in index
            if (!leftIndex.ContainsKey(key)) leftIndex[key] = new List<Token>();
            leftIndex[key].Add(token);

            // 2. Lookup matches on the Right using the key (O(1) lookup!)
            int count = 0;
            if (rightIndex.TryGetValue(key, out var matchingFacts))
            {
                count = matchingFacts.Count;
            }

            matchCounts[token] = count;
            if (count > 0) { PropagateAssert(token); }
        }

        /// <summary>
        /// Asserts a fact on the right side of the node.  It updates the right index and checks for 
        /// matches on the left side using the extracted key.  If matches are found, it propagates 
        /// assertions to successors.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void AssertRight(object fact)
        {
            var key = rightKeySelector(fact);

            // 1. Store in index
            if (!rightIndex.ContainsKey(key)) rightIndex[key] = new List<object>();
            rightIndex[key].Add(fact);

            // 2. Find only the tokens that care about this specific key
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
        /// Retracts a token from the left side of the node.  It updates the left index and checks for 
        /// matches on the right side using the extracted key.  If matches are lost, it propagates 
        /// retractions to successors.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void RetractLeft(Token token)
        {
            var key = leftKeySelector(token);
            if (leftIndex.TryGetValue(key, out var list)) list.Remove(token);
            if (matchCounts.TryGetValue(token, out var count) && count > 0)
            {
                matchCounts.Remove(token);
                PropagateRetract(token);
            }
        }

        /// <summary>
        /// Retracts a fact from the right side of the node.  It updates the right index and checks for 
        /// matches on the left side using the extracted key.  If matches are lost, it propagates 
        /// retractions to successors.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void RetractRight(object fact)
        {
            var key = rightKeySelector(fact);
            if (rightIndex.TryGetValue(key, out var list)) list.Remove(fact);

            if (leftIndex.TryGetValue(key, out var matchingTokens))
            {
                foreach (var token in matchingTokens)
                {
                    int oldCount = matchCounts[token];
                    matchCounts[token] = oldCount - 1;
                    if (oldCount == 1) { PropagateRetract(token); }
                }
            }
        }

        /// <summary>
        /// The Refresh method is used to update the state of a fact or token in the node without 
        /// changing its presence.  For simplicity, we will treat Refresh as a Retract followed by an 
        /// Assert, which will trigger the necessary updates to successor nodes based on the current 
        /// state of matches.
        /// </summary>
        /// <param name="factOrToken">The fact or token this operation is acting upon.</param>
        /// <param name="propertyName">The name of the property in the fact cell that is being updated.</param>
        public void Refresh(object fact, string propertyName)
        {
            // For simplicity, we will treat Refresh as a Retract followed by an Assert
            Retract(fact);
            Assert(fact);
        }

        /// <summary>
        /// Propagates an assertion to all successor nodes.  This is called when a token on the left 
        /// side finds a match on the right side, indicating that the "exists" condition is satisfied 
        /// for that token.
        /// </summary>
        /// <param name="token">The token to send to each successor.</param>
        private void PropagateAssert(Token token) => successors.ForEach(s => s.Assert(token));

        /// <summary>
        /// Propagates a retraction to all successor nodes.  This is called when a token on the left 
        /// side loses its last match on the right side, indicating that the "exists" condition is no 
        /// longer satisfied for that token.
        /// </summary>
        /// <param name="token">The token to retract from each successor.</param>
        private void PropagateRetract(Token token) => successors.ForEach(s => s.Retract(token));

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
            foreach (var child in successors) { child.DebugPrint(fact, level + 1); }
        }
    }
}
