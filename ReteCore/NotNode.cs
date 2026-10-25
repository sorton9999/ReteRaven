//-----------------------------------------------------------------------
// <copyright file="NotNode.cs">
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

    /// <summary>
    /// This class represents a NOT node in a Rete network, which implements the logical negation.
    /// It tracks partial matches from the left (Beta side) and facts from the right (Alpha side) 
    /// to determine when the NOT condition is satisfied. When a new left token is asserted, it 
    /// checks against all right facts to see if any block it. If none block it, the token is 
    /// propagated downstream. When a new right fact is asserted, it checks if it blocks any 
    /// existing left tokens and updates their state accordingly. The node also supportsretractions 
    /// and refreshes to maintain correct state as facts and tokens change over time.
    /// <Remark>
    /// As a side note, in a rete network, the NotNode and the ExistsNode are very similar in structure
    /// and logic, with the main difference being that the ExistsNode propagates tokens when at least 
    /// one match exists, while the NotNode propagates tokens only when no matches exist. This means that
    /// the ExistsNode will assert tokens when the count of blocking facts is greater than zero, whereas
    /// the NotNode will assert tokens when the count is zero. Both nodes must carefully manage their 
    /// internal state to ensure correct propagation of tokens based on changes in the right memory.</Remark>
    /// </summary>
    public class NotNode : IReteNode
    {
        /// <summary>
        /// Store each left token and how many right facts currently block it. A token is blocked if 
        /// there is at least one right fact that satisfies the join constraint with it.
        /// </summary>
        private readonly Dictionary<Token, int> leftMatches = new Dictionary<Token, int>();
        /// <summary>
        /// Store all facts that have been asserted on the right side. This allows the NOT node to check
        /// incoming left tokens against all existing right facts to determine if they are blocked. When
        /// a new right fact is asserted, it is added to this list, and the node checks if it blocks any 
        /// existing left tokens, updating their block counts accordingly. This memory of right facts is 
        /// essential for maintaining the correct state of matches in the NOT node and ensuring that 
        /// assertions and retractions are propagated correctly to successor nodes based on the current 
        /// state of matches.
        /// </summary>
        private readonly List<object> rightMemory = new List<object>();
        /// <summary>
        /// Store the successor nodes that will receive assertions and retractions from this NOT node. 
        /// When a new successor is added, it is immediately refreshed with the current valid state of 
        /// left tokens that are not blocked by any right facts
        /// </summary>
        private readonly List<IReteNode> _successors = new List<IReteNode>();
        /// <summary>
        /// The join constraint function that defines how to determine if a right fact blocks a left 
        /// token. This function takes a Token and an object (representing a right fact) as input and 
        /// returns a boolean indicating whether the right fact satisfies the join condition with the 
        /// left token. This function is crucial for the logic of the NOT node, as it is used to evaluate
        /// whether incoming left tokens are blocked by existing right facts and whether new right facts 
        /// block any existing left tokens. The join constraint allows for flexible and dynamic 
        /// definitions of the conditions under which the NOT node operates, enabling it to be used in a 
        /// variety of scenarios within the Rete network based on the specific rules and patterns being 
        /// evaluated.
        /// </summary>
        private readonly Func<Token, object, bool> joinConstraint;
        /// <summary>
        /// The name of the NOT node, used for identification and debugging purposes.
        /// </summary>
        private readonly string nodeName;

        /// <summary>
        /// The constructor for the NotNode takes a name for identification and a join constraint 
        /// function that defines how to determine if a right fact blocks a left token.
        /// </summary>
        /// <param name="name">The name of this node as identification.</param>
        /// <param name="constraint">The condition to be used as the 'NOT' blocking constraint.</param>
        public NotNode(string name, Func<Token, object, bool> constraint)
        {
            joinConstraint = constraint;
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
        /// Adds a successor node to this NOT node. When a new successor is added, it is immediately 
        /// refreshed with the current valid state of left tokens that are not blocked by any right 
        /// facts, ensuring that the new node is up-to-date with the current state of matches in this 
        /// NOT node. This allows for dynamic construction of the Rete network while maintaining correct 
        /// propagation of matches to successor nodes.
        /// </summary>
        /// <param name="node">The node to add this node to as a successor.</param>
        public void AddSuccessor(IReteNode node)
        {
            node.Parent = this;
            _successors.Add(node);
            // When a new node is added, we must "Refresh" it with current valid state
            foreach (var entry in leftMatches)
            {
                if (entry.Value == 0)
                {
                    node.Assert(entry.Key);
                }
            }
        }

        /// <summary>
        /// The Assert method is the main entry point for incoming facts or tokens. It determines whether
        /// the input is a left token or a right fact and delegates to the appropriate method (AssertLeft 
        /// or AssertRight) to handle the logic specific to that side of the NOT node. This design allows 
        /// for a clean separation of concerns and ensures that the correct processing logic is applied 
        /// based on the type of input received.
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
        /// Recounts right-side blockers and propagates only a zero/nonzero Not-state transition.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void AssertLeft(Token token)
        {
            bool wasSatisfied = leftMatches.TryGetValue(token, out int oldCount) && oldCount == 0;
            int count = CountMatches(token);
            leftMatches[token] = count;

            if (count == 0 && !wasSatisfied) PropagateAssert(token);
            else if (count > 0 && wasSatisfied) PropagateRetract(token);
        }

        /// <summary>
        /// Adds a right fact once and retracts left tokens only when their first blocker appears.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void AssertRight(object fact)
        {
            if (rightMemory.Any(existing => ReferenceEquals(existing, fact))) return;
            rightMemory.Add(fact);

            // Check if this new fact "kills" any currently active left tokens
            foreach (var token in new List<Token>(leftMatches.Keys))
            {
                if (joinConstraint(token, fact))
                {
                    int oldCount = leftMatches[token];
                    int newCount = oldCount + 1;
                    leftMatches[token] = newCount;

                    if (oldCount == 0)
                    {
                        PropagateRetract(token);
                    }
                }
            }
        }

        /// <summary>
        /// The Retract method is the main entry point for retractions of facts or tokens. Similar to 
        /// Assert, it determines whether the input is a left token or a right fact and delegates to the 
        /// appropriate method (Retract or RetractRight) to handle the logic specific to that side of 
        /// the NOT node. This allows for proper handling of retractions and ensures that the state of 
        /// the NOT node is correctly updated when facts or tokens are removed from the network.
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
        /// Handles retractions of left tokens. It removes the token from the left matches and, if it 
        /// was not blocked by any right facts, it propagates a retract downstream to indicate that the 
        /// NOT condition is now satisfied again. This ensures that any successor nodes that were 
        /// previously blocked by this token will now receive an assertion for it, allowing the Rete 
        /// network to correctly reflect the change in state caused by the retraction of the token.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void RetractLeft(Token token)
        {
            if (leftMatches.TryGetValue(token, out int count))
            {
                leftMatches.Remove(token);
                if (count == 0) PropagateRetract(token);
            }
        }

        /// <summary>
        /// Removes the right fact and recomputes blocker counts before propagating Not-state transitions.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void RetractRight(object fact)
        {
            int index = rightMemory.FindIndex(existing => ReferenceEquals(existing, fact));
            if (index < 0) return;
            rightMemory.RemoveAt(index);

            foreach (var token in leftMatches.Keys.ToList()) ReconcileLeft(token);
        }

        /// <summary>
        /// Reconciles a refreshed left token or right fact against the current blocker counts.
        /// </summary>
        /// <param name="factOrToken">The fact or token this operation is acting upon.</param>
        /// <param name="propertyName">The name of the property in the fact cell that is being updated.</param>
        public void Refresh(object factOrToken, string propertyName)
        {
            if (factOrToken is Token token)
            {
                RefreshLeft(token, propertyName);
            }
            else
            {
                RefreshRight(factOrToken, propertyName);
            }
        }

        /// <summary>
        /// Reconciles a left token and forwards refreshes while it remains unblocked.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void RefreshLeft(Token token, string propertyName = "")
        {
            if (!leftMatches.ContainsKey(token))
            {
                AssertLeft(token);
                return;
            }
            int oldCount = leftMatches[token];
            ReconcileLeft(token);
            if (oldCount == 0 && leftMatches[token] == 0)
            {
                foreach (var successor in _successors) successor.Refresh(token, propertyName);
            }
        }

        /// <summary>
        /// Recomputes all blocker counts from current right memory to keep repeated refreshes idempotent.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        /// <param name="propertyName">The name of the property in the fact cell that is being updated.</param>
        public void RefreshRight(object fact, string propertyName)
        {
            if (!rightMemory.Any(existing => ReferenceEquals(existing, fact))) return;
            foreach (var token in leftMatches.Keys.ToList()) ReconcileLeft(token);
        }

        /// <summary>
        /// Counts the current right-memory blockers for a left token. This method iterates through all right facts stored
        /// in the rightMemory list and applies the joinConstraint function to determine how many of those facts block the
        /// given left token. The result is an integer count of blocking facts, which is used to update the leftMatches
        /// dictionary and determine whether the NOT condition is satisfied for that token. This count is essential for
        /// managing the state of left tokens and ensuring that assertions and retractions are propagated correctly based on
        /// the current state of matches in the NOT node.
        /// </summary>
        /// <param name="token">The left token to count blockers for.</param>
        /// <returns>The number of right-memory blockers for the given left token.</returns>
        private int CountMatches(Token token) => rightMemory.Count(fact => joinConstraint(token, fact));

        /// <summary>
        /// Stores the recomputed blocker count and propagates only zero/nonzero transitions.
        /// </summary>
        /// <param name="token">The left token to reconcile.</param>
        private void ReconcileLeft(Token token)
        {
            int oldCount = leftMatches[token];
            int newCount = CountMatches(token);
            leftMatches[token] = newCount;

            if (oldCount == 0 && newCount > 0) PropagateRetract(token);
            else if (oldCount > 0 && newCount == 0) PropagateAssert(token);
        }

        /// <summary>
        /// Removes a successor node from this NotNode. This method is used when a successor node is no longer needed or when the 
        /// structure of the Rete network changes. When a successor is removed, it will no longer receive any tokens asserted, 
        /// retracted, or refreshed through this node. The method simply removes the specified child node from the list of successors, 
        /// ensuring that it is no longer part of the propagation path for tokens.
        /// </summary>
        /// <param name="successor">The successor node to remove. Cannot be null.</param>
        public void RemoveSuccessor(IReteNode successor)
        {
            _successors.Remove(successor);
        }

        /// <summary>
        /// Sends an assertion downstream to all successor nodes for the given token. This is called when
        /// a left token becomes valid (not blocked by any right facts) and needs to be propagated to 
        /// successor nodes to indicate that the NOT condition is satisfied for that token. Each successor
        /// node will receive the assertion and can update its state accordingly based on the new valid 
        /// token. 
        /// </summary>
        /// <param name="token">The token to send to each successor.</param>
        private void PropagateAssert(Token token) => _successors.ForEach(s => s.Assert(token));

        /// <summary>
        /// Sends a retraction downstream to all successor nodes for the given token. This is called when
        /// a left token becomes invalid (blocked by at least one right fact) and needs to be propagated 
        /// to successor nodes to indicate that the NOT condition is no longer satisfied for that token. 
        /// Each successor node will receive the retraction and can update its state accordingly based on
        /// the new invalid token. 
        /// </summary>
        /// <param name="token">The token to retract from each successor.</param>
        private void PropagateRetract(Token token) => _successors.ForEach(s => s.Retract(token));

        /// <summary>
        /// A debug method to print the current state of the NOT node, including the number of left tokens, 
        /// right facts, and the block counts for each left token. This can be useful for understanding
        /// how the NOT node is processing matches and how it is affected by incoming facts and tokens. 
        /// The method takes an optional level parameter to control indentation for better readability 
        /// when printing nested nodes in the Rete network. By providing insight into the internal state 
        /// of the NOT node, this debug method can help developers diagnose issues and understand the 
        /// behavior of the Rete network during development and testing.
        /// </summary>
        /// <param name="fact">The fact whose information is to be output.</param>
        /// <param name="level">A level of indentation.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            Console.WriteLine($"{indent}NotNode:[{nodeName}] {leftMatches.Count} left tokens, {rightMemory.Count} right facts");
            foreach (var entry in leftMatches)
            {
                Console.WriteLine($"{indent}  Left Token: {entry.Key}, Blocked by {entry.Value} right facts");
            }
        }
    }
}
