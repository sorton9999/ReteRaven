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
        private readonly List<IReteNode> successors = new List<IReteNode>();
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
        /// Adds a successor node to this NOT node. When a new successor is added, it is immediately 
        /// refreshed with the current valid state of left tokens that are not blocked by any right 
        /// facts, ensuring that the new node is up-to-date with the current state of matches in this 
        /// NOT node. This allows for dynamic construction of the Rete network while maintaining correct 
        /// propagation of matches to successor nodes.
        /// </summary>
        /// <param name="node">The node to add this node to as a successor.</param>
        public void AddSuccessor(IReteNode node)
        {
            successors.Add(node);
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
        /// Handles incoming partial matches from the Left (Beta side). It checks the new token against all 
        /// facts in the right memory using the join constraint.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void AssertLeft(Token token)
        {
            int count = 0;
            foreach (var fact in rightMemory)
            {
                if (joinConstraint(token, fact)) count++;
            }

            leftMatches[token] = count;

            // If count is 0, the NOT condition is satisfied. Pass it down.
            if (count == 0)
            {
                PropagateAssert(token);
            }
        }

        /// <summary>
        /// Handles incoming single facts from the Right (Alpha side). It checks the new fact against all
        /// existing left tokens to see if it blocks any of them.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void AssertRight(object fact)
        {
            rightMemory.Add(fact);

            // Check if this new fact "kills" any currently active left tokens
            foreach (var token in new List<Token>(leftMatches.Keys))
            {
                if (joinConstraint(token, fact))
                {
                    int oldCount = leftMatches[token];
                    leftMatches[token] = oldCount + 1;

                    Console.WriteLine($"[NOT] Blocking Fact Found for {token}. Count: {oldCount} -> {leftMatches[token]}");

                    // Transition from 0 to 1 means the NOT is no longer true
                    if (oldCount == 0)
                    {
                        Console.WriteLine("[NOT] Condition just became FALSE. Sending RETRACT downstream.");
                        PropagateRetract(token);
                    }
                }
                //else { Retract(fact); }
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
        /// Handles retractions of right facts. It removes the fact from the right memory and checks if 
        /// this retraction "resurrects" any left tokens that were previously blocked by this fact. If a 
        /// left token's block count transitions from 1 to 0, it means the NOT condition is now satisfied
        /// for that token, and it propagates an assertion downstream to indicate that the token is now 
        /// valid again. This ensures that the Rete network correctly reflects the change in state caused
        /// by the retraction of the right fact and allows any successor nodes to update their state 
        /// accordingly based on the new valid tokens.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        public void RetractRight(object fact)
        {
            rightMemory.Remove(fact);

            // Check if removing this fact "resurrects" any left tokens
            foreach (var token in new List<Token>(leftMatches.Keys))
            {
                if (joinConstraint(token, fact))
                {
                    int oldCount = leftMatches[token];
                    leftMatches[token] = oldCount - 1;

                    // Transition from 1 to 0 means the NOT is true again
                    if (oldCount == 1)
                    {
                        Assert(token);
                    }
                }
            }
        }

        /// <summary>
        /// Forces a downstream node to sync with this node's current valid tokens. This is used when a 
        /// new successor node is added to ensure it receives the correct state of matches from this NOT
        /// node. It checks if the input is a left token or a right fact and delegates to the appropriate
        /// refresh method to update the state of matches and propagate any necessary assertions or 
        /// retractions downstream. This allows for dynamic updates to the Rete network while maintaining
        /// correct propagation of matches to successor nodes.
        /// </summary>
        /// <param name="factOrToken">The fact or token this operation is acting upon.</param>
        /// <param name="propertyName">The name of the property in the fact cell that is being updated.</param>
        public void Refresh(object factOrToken, string propertyName)
        {
            if (factOrToken is Token token)
            {
                RefreshLeft(token);
            }
            else
            {
                RefreshRight(factOrToken, propertyName);
            }
        }

        /// <summary>
        /// Handles refreshes of left tokens. It checks if the token is currently blocked by any right 
        /// facts and, if not, it propagates an assertion downstream to ensure that successor nodes are 
        /// updated with the current valid state of this token. This is important for maintaining correct
        /// state in the Rete network, especially when new successor nodes are added or when existing 
        /// nodes need to be refreshed due to changes in the network. By checking the block count for the
        /// token and only propagating if it is currently valid (not blocked), we ensure that the NOT 
        /// condition is correctly maintained and that successor nodes receive accurate information about
        /// the state of matches in this NOT node.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void RefreshLeft(Token token)
        {
            if (leftMatches.TryGetValue(token, out int count) && count == 0)
            {
                PropagateAssert(token);
            }
        }

        /// <summary>
        /// Handles refreshes of right facts. It checks if the fact blocks any existing left tokens and 
        /// updates their block counts accordingly. If a token transitions from blocked to unblocked
        /// or vice versa, it propagates the necessary assertions or retractions downstream to ensure 
        /// that successor nodes are updated with the current valid state of matches in this NOT node. 
        /// This is important for maintaining correct state in the Rete network, especially when new 
        /// successor nodes are added or when existing nodes need to be refreshed due to changes in the 
        /// network. By checking the join constraint for each token against the refreshed fact and 
        /// updating their block counts, we ensure that the NOT condition is correctly maintained and 
        /// that successor nodes receive accurate information about the state of matches in this NOT node.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        /// <param name="propertyName">The name of the property in the fact cell that is being updated.</param>
        public void RefreshRight(object fact, string propertyName)
        {
            // For simplicity, we will just re-evaluate all left tokens against this fact
            foreach (var token in new List<Token>(leftMatches.Keys))
            {
                if (joinConstraint(token, fact))
                {
                    // This is a bit brute-force, but it ensures correctness
                    int oldCount = leftMatches[token];
                    leftMatches[token] = oldCount + 1;
                    if (oldCount == 0)
                    {
                        PropagateRetract(token);
                    }
                }
            }
        }

        /// <summary>
        /// Sends an assertion downstream to all successor nodes for the given token. This is called when
        /// a left token becomes valid (not blocked by any right facts) and needs to be propagated to 
        /// successor nodes to indicate that the NOT condition is satisfied for that token. Each successor
        /// node will receive the assertion and can update its state accordingly based on the new valid 
        /// token. 
        /// </summary>
        /// <param name="token">The token to send to each successor.</param>
        private void PropagateAssert(Token token) => successors.ForEach(s => s.Assert(token));

        /// <summary>
        /// Sends a retraction downstream to all successor nodes for the given token. This is called when
        /// a left token becomes invalid (blocked by at least one right fact) and needs to be propagated 
        /// to successor nodes to indicate that the NOT condition is no longer satisfied for that token. 
        /// Each successor node will receive the retraction and can update its state accordingly based on
        /// the new invalid token. 
        /// </summary>
        /// <param name="token">The token to retract from each successor.</param>
        private void PropagateRetract(Token token) => successors.ForEach(s => s.Retract(token));

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
