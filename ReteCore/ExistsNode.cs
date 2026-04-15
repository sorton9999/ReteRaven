using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    /// <summary>
    /// The ExistsNode class represents a node in a Rete network that implements the "exists" condition.
    /// It tracks partial matches from the left (beta) side and facts from the right (alpha) side, 
    /// ensuring that a token is only propagated to successors if at least one matching fact exists on 
    /// the right. The node maintains a count of how many right facts currently block each left token, 
    /// allowing it to efficiently determine when to assert or retract tokens based on changes in the 
    /// right memory. This node is essential for implementing rules that require the existence of certain
    /// conditions without needing to specify all possible combinations of facts.
    /// <Remark>
    /// As a side note, in a rete network, the ExistsNode and the NotNode are very similar in structure 
    /// and logic, with the main difference being that the ExistsNode propagates tokens when at least one
    /// match exists, while the NotNode propagates tokens only when no matches exist. This means that the
    /// ExistsNode will assert tokens when the count of blocking facts is greater than zero, whereas the 
    /// NotNode will assert tokens when the count is zero. Both nodes must carefully manage their internal
    /// state to ensure correct propagation of tokens based on changes in the right memory.</Remark>
    /// </summary>
    public class ExistsNode : IReteNode
    {
        /// <summary>
        /// Stores the count of how many right facts currently block each left token. The key is a Token
        /// representing a partial match from the left side, and the value is an integer count of how 
        /// many facts in the right memory match that token according to the join constraint. When a 
        /// token is asserted from the left, it is evaluated against all facts in the right memory, and 
        /// the count is updated accordingly. If the count is greater than zero, it means that there is 
        /// at least one matching fact on the right, and the token can be propagated to successor nodes. 
        /// If the count is zero, it means that there are no matching facts on the right, and the token 
        /// is effectively blocked from propagating until a matching fact is added to the right memory.
        /// </summary>
        private readonly Dictionary<Token, int> leftMatches = new Dictionary<Token, int>();
        /// <summary>
        /// Stores the facts that have been asserted on the right side of this node. Each fact is stored 
        /// as an object in a list, and this collection is used to evaluate incoming tokens from the left
        /// side against the join constraint. When a new fact is asserted on the right, it is added to 
        /// this list, and all existing tokens in the leftMatches dictionary are re-evaluated against 
        /// this new fact to determine if they should be propagated or blocked. When a fact is retracted 
        /// from the right, it is removed from this list, and any tokens that were previously blocked by 
        /// that fact are re-evaluated to see if they can now be propagated. This right memory is 
        /// essential for maintaining the state of matches and ensuring that tokens are correctly 
        /// propagated based on the current facts in the system.
        /// </summary>
        private readonly List<object> rightMemory = new List<object>();
        /// <summary>
        /// Stores the successor nodes that will receive tokens asserted, retracted, or refreshed through
        /// this ExistsNode. Each successor is an IReteNode that will be affected by operations performed
        /// on this node. The collection is initialized as an empty list and can be modified by adding 
        /// new successor nodes using the AddSuccessor method. The order of successors in the list may 
        /// affect the order in which tokens are propagated to them, but does not affect the logic of the
        /// Rete network.
        /// </summary>
        private readonly List<IReteNode> successors = new List<IReteNode>();
        /// <summary>
        /// The join constraint function that determines whether a given left token and right fact should
        /// be considered a match. This function is critical for the operation of the ExistsNode, as it 
        /// defines the logic for how tokens and facts are evaluated against each other to determine 
        /// whether a token should be blocked or allowed to propagate. The join constraint is typically 
        /// defined based on the specific conditions of the rule being implemented and may involve 
        /// checking properties of the token and fact, comparing values, or applying any other logic 
        /// necessary to determine if they satisfy the conditions for a match. The function should return
        /// true if the token and fact satisfy the conditions for a match, and false otherwise. This 
        /// allows for flexible and powerful rule definitions based on the specific requirements of the 
        /// application using the Rete engine.
        /// </summary>
        private readonly Func<Token, object, bool> joinConstraint;
        /// <summary>
        /// The name of this ExistsNode, used for identification and debugging purposes.
        /// </summary>
        private readonly string nodeName;

        /// <summary>
        /// The constructor for the ExistsNode takes a name for debugging purposes and a join constraint 
        /// function that determines whether a given left token and right fact should be considered a 
        /// match. The join constraint is a critical component of the node, as it defines the logic for 
        /// how tokens and facts are evaluated against each other to determine whether a token should be 
        /// blocked or allowed to propagate. The nodeName parameter is used for identification and 
        /// debugging, allowing developers to easily trace the flow of tokens through the network and 
        /// understand which nodes are responsible for certain matches or failures. The joinConstraint 
        /// function should return true if the token and fact satisfy the conditions for a match, and 
        /// false otherwise. This allows for flexible and powerful rule definitions based on the specific
        /// requirements of the application using the Rete engine.
        /// </summary>
        /// <param name="name">The name of this node as identification.</param>
        /// <param name="constraint">The condition to be used as the 'EXISTS' constraint.</param>
        public ExistsNode(string name, Func<Token, object, bool> constraint)
        {
            joinConstraint = constraint;
            nodeName = name;
        }

        /// <summary>
        /// Adds a successor node to this ExistsNode. When a new successor is added, the node must 
        /// "Refresh" it with the current valid state of tokens. This means that for each token currently
        /// in the leftMatches dictionary that has a count of zero (indicating it is not blocked by any 
        /// right facts), the node will assert that token to the new successor. This ensures that the new
        /// successor node is immediately aware of all currently valid tokens and can properly propagate 
        /// them further down the network. The Refresh process is crucial for maintaining consistency in 
        /// the Rete network, especially when nodes are added dynamically after some tokens have already 
        /// been processed. By refreshing the new successor with the current state, we ensure that it can
        /// function correctly without missing any relevant tokens or facts.
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
        /// The Assert method is the main entry point for handling incoming data to this node. It can 
        /// receive either a Token (from the left/beta side) or a single fact (from the right/alpha side).
        /// The method determines the type of the input and delegates to the appropriate handler method 
        /// (AssertLeft for tokens and AssertRight for facts). This design allows the node to maintain a 
        /// clear separation of logic for handling different types of inputs while still providing a 
        /// unified interface for asserting data into the node. The Assert method is responsible for 
        /// ensuring that the internal state of the node is updated correctly based on the incoming data,
        /// which in turn affects how tokens are propagated to successor nodes.
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
        /// Handles incoming partial matches from the Left (Beta side) by evaluating them against the 
        /// current facts in the right memory. For each token asserted from the left, the node checks 
        /// how many facts in the right memory match it according to the join constraint. The count of 
        /// matching facts is stored in the leftMatches dictionary. If at least one match exists 
        /// (count > 0), the token is immediately propagated to successor nodes. This ensures that only 
        /// tokens that satisfy the "exists" condition are allowed to propagate, while those that do not 
        /// have any matching facts on the right are effectively blocked and not sent downstream.
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

            // If at least one match exists, propagate immediately
            if (count > 0)
            {
                PropagateAssert(token);
            }
        }

        /// <summary>
        /// Handles incoming single facts from the Right (Alpha side) by adding them to the right memory
        /// and checking if they match any currently active left tokens. For each fact asserted from the 
        /// right, the node iterates through all tokens in the leftMatches dictionary and evaluates the 
        /// join constraint against the new fact. If a match is found, the count for that token is 
        /// incremented. If this is the first matching fact for that token (count changes from 0 to 1), 
        /// the token is propagated to successor nodes. This ensures that when a new fact is added that 
        /// satisfies the "exists" condition for any token, those tokens are activated and sent 
        /// downstream, while tokens that do not have any matching facts remain blocked.
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

                    // Only propagate the FIRST time we find a match
                    if (oldCount == 0)
                    {
                        PropagateAssert(token);
                    }
                }
            }
        }

        /// <summary>
        /// The Retract method is responsible for handling the removal of either a Token 
        /// (from the left/beta side) or a single fact (from the right/alpha side). Similar to the 
        /// Assert method, it determines the type of input and delegates to the appropriate handler 
        /// method (RetractLeft for tokens and RetractRight for facts). When retracting a token from 
        /// the left, the node checks if it was previously active (count > 0) and if so, it propagates 
        /// a retraction to successor nodes. When retracting a fact from the right, the node removes it 
        /// from the right memory and checks if this causes any previously blocked tokens to become 
        /// active again. If a token's count changes from 1 to 0 due to the retraction of a fact, it 
        /// means that token is no longer blocked and should be retracted from successor nodes. This 
        /// ensures that the node maintains an accurate representation of which tokens are currently 
        /// valid based on the presence or absence of matching facts in the right memory.
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
        /// Retracts a token from the left side by removing it from the leftMatches dictionary. If the 
        /// token was previously active (count > 0), it propagates a retraction to successor nodes. This 
        /// ensures that when a token is removed from the left side, any downstream nodes that were 
        /// relying on that token being active are notified and can update their state accordingly. The 
        /// node does not need to check for matches against the right memory when retracting a token, as 
        /// the presence of matching facts only affects whether a token is active or not, and once a 
        /// token is retracted, it is no longer relevant regardless of the right memory state.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void RetractLeft(Token token)
        {
            if (leftMatches.TryGetValue(token, out int count))
            {
                leftMatches.Remove(token);
                // Only need to retract downstream if it was currently "Active" (count > 0)
                if (count > 0) { PropagateRetract(token); }
            }
        }

        /// <summary>
        /// Retracts a fact from the right side by removing it from the right memory and checking if 
        /// this causes any previously blocked tokens to become active again. For each token in the 
        /// leftMatches dictionary, if the retracted fact matches the token according to the join 
        /// constraint, the count for that token is decremented. If this causes the count to change from
        /// 1 to 0, it means that token is no longer blocked by any facts on the right and should be 
        /// retracted from successor nodes. This ensures that when a fact is removed from the right side,
        /// any tokens that were previously blocked by that fact are properly updated and downstream 
        /// nodes are notified of their new state.
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

                    // Only retract when the LAST matching fact is removed
                    if (oldCount == 1)
                    {
                        PropagateRetract(token);
                    }
                }
            }
        }

        /// <summary>
        /// Forces a downstream node to sync with this node's current valid tokens. This is used when a 
        /// new successor node is added, or when an existing successor node needs to be refreshed due 
        /// to changes in the network. The method takes either a Token or a fact and a property name 
        /// (for fact refreshes) and determines which type of refresh to perform. For token refreshes, 
        /// it checks if the token is currently blocked or not and propagates an assert if it is valid. 
        /// For fact refreshes, it checks all tokens against the refreshed fact and updates their counts 
        /// accordingly, propagating asserts or retracts as needed based on the new state of matches. 
        /// This method ensures that successor nodes are always in sync with the current state of this 
        /// ExistsNode, allowing for correct propagation of tokens based on the latest information.
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
        /// Refreshes a token from the left side by checking if it is currently blocked or not and 
        /// propagating an assert if it is valid. This method is used when a new successor node is added
        /// or when an existing successor node needs to be refreshed due to changes in the network. If 
        /// the token is not currently in the leftMatches dictionary, it means it has not been processed
        /// yet, so we call AssertLeft to evaluate it against the current right memory and update its 
        /// state accordingly. This ensures that any new or updated tokens are properly evaluated and 
        /// propagated to successor nodes based on their current validity.
        /// </summary>
        /// <param name="token">The token this operation is acting upon.</param>
        public void RefreshLeft(Token token)
        {
            if (!leftMatches.ContainsKey(token))
            {
                AssertLeft(token);
            }
        }

        /// <summary>
        /// Refreshes a fact from the right side by checking all tokens against the refreshed fact and 
        /// updating their counts accordingly. This method is used when a new successor node is added 
        /// or when an existing successor node needs to be refreshed due to changes in the network. For 
        /// each token in the leftMatches dictionary, if the refreshed fact matches the token according 
        /// to the join constraint, we check if it was previously blocked (count == 0) and if so, we 
        /// increment the count and propagate an assert to successor nodes. If it was previously active 
        /// (count > 0), we simply update the count without propagating since it is already valid. This 
        /// ensures that any changes to facts on the right side are properly reflected in the state of 
        /// tokens and that successor nodes are kept up-to-date with the current valid tokens based on 
        /// the latest information.
        /// </summary>
        /// <param name="fact">The fact this operation is acting upon.</param>
        /// <param name="propertyName">The name of the property in the fact cell that is being updated.</param>
        public void RefreshRight(object fact, string propertyName)
        {
            foreach (var entry in leftMatches)
            {
                if (joinConstraint(entry.Key, fact))
                {
                    // This is a bit brute-force, but it ensures correctness
                    int oldCount = entry.Value;
                    leftMatches[entry.Key] = oldCount + 1;
                    if (oldCount == 0)
                    {
                        PropagateAssert(entry.Key);
                    }
                }
            }
        }

        /// <summary>
        /// Propagates an assert of a token to all successor nodes. This method is called when a token 
        /// becomes valid (i.e., it has at least one matching fact on the right) and needs to be sent 
        /// downstream to successor nodes. The method iterates through all successor nodes and calls 
        /// their Assert method with the given token, allowing them to process it according to their own
        /// logic and potentially propagate it further down the network. 
        /// </summary>
        /// <param name="token">The token to send to each successor.</param>
        private void PropagateAssert(Token token) => successors.ForEach(s => s.Assert(token));

        /// <summary>
        /// Propagates a retraction of a token to all successor nodes. This method is called when a 
        /// token becomes invalid (i.e., it no longer has any matching facts on the right) and needs to 
        /// be retracted from downstream successor nodes. The method iterates through all successor nodes
        /// and calls their Retract method with the given token, allowing them to update their state 
        /// accordingly and potentially retract it further down the network. 
        /// </summary>
        /// <param name="token">The token to retract from each successor.</param>
        private void PropagateRetract(Token token) => successors.ForEach(s => s.Retract(token));

        /// <summary>
        /// Prints the internal state of this ExistsNode for debugging purposes. This method provides a 
        /// visual representation of the current tokens in the leftMatches dictionary, the facts in the 
        /// rightMemory, and how they are related in terms of blocking. The output includes the name of 
        /// the node, the number of left tokens and right facts, and for each left token, it shows how 
        /// many right facts are currently blocking it.
        /// </summary>
        /// <param name="fact">The fact whose information is to be output.</param>
        /// <param name="level">A level of indentation.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            Console.WriteLine($"{indent}ExistsNode:[{nodeName}] {leftMatches.Count} left tokens, {rightMemory.Count} right facts");
            foreach (var entry in leftMatches)
            {
                Console.WriteLine($"{indent}  Left Token: {entry.Key}, Blocked by {entry.Value} right facts");
            }
        }
    }
}
