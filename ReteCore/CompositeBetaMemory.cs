//-----------------------------------------------------------------------
// <copyright file="CompositeBetaMemory.cs">
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
    /// <summary>
    /// The CompositeBetaMemory class represents a node in a Rete network that combines multiple branches of tokens 
    /// (partial matches) using an OR logic.
    /// </summary>
    public class CompositeBetaMemory : IReteNode, ILatentMemory
    {
        /// <summary>
        /// Tracks the tokens that are currently supported by at least one branch in this CompositeBetaMemory. 
        /// Each token is associated with a count of how many branches support it, allowing the node to manage 
        /// the activation and deactivation of matches as branches are asserted and retracted. When a new token 
        /// is asserted from a branch, it is added to this dictionary with an initial count of 1 if it is not 
        /// already present, or its count is incremented if it is already supported. When a token is retracted 
        /// from a branch, its count is decremented, and if the count reaches zero, the token is removed from the 
        /// dictionary and retracted from successor nodes. This mechanism ensures that the CompositeBetaMemory 
        /// accurately reflects the active matches based on the branches that support them, enabling correct 
        /// propagation of matches through the network.
        /// </summary>
        private readonly Dictionary<Token, int> _supportedMatches = new();
        /// <summary>
        /// A list of successor nodes that will receive tokens asserted, retracted, or refreshed through this 
        /// CompositeBetaMemory. Each successor is an IReteNode that will be affected by operations performed on this node. 
        /// The collection is initialized as an empty list and can be modified by adding new successor nodes using the 
        /// AddSuccessor method. The order of successors in the list may affect the order in which tokens are propagated 
        /// to them, but does not affect the logic of the Rete network.
        /// </summary>
        private readonly List<IReteNode> _successors = new();

        /// <summary>
        /// A collection of tokens that are currently supported by at least one branch in this CompositeBetaMemory. 
        /// Each token is associated with a count of how many branches support it, allowing the node to manage the 
        /// activation and deactivation of matches as branches are asserted and retracted. This property provides 
        /// access to the active tokens that have been propagated through this node, enabling successor nodes to evaluate 
        /// their conditions based on these matches.
        /// </summary>
        public IEnumerable<Token> Tokens => _supportedMatches.Keys;

        /// <summary>
        /// The parent node in the Rete network. This property allows for navigation back up the network from this node.
        /// </summary>
        public IReteNode? Parent { get; set; }

        /// <summary>
        /// Returns an enumerable of successor nodes. In this implementation, there is typically one successor node that receives 
        /// facts that satisfy the condition defined by the predicate.
        /// </summary>
        public IEnumerable<IReteNode> Successors => _successors;

        /// <summary>
        /// Adds a successor node to this CompositeBetaMemory. Successor nodes will receive assertions and retractions 
        /// for any tokens that become active or inactive in this node. This method allows the Rete network to be 
        /// dynamically constructed, enabling the flow of matches through the network as conditions are evaluated. When 
        /// a new successor is added, it will immediately receive assertions for all currently active tokens, ensuring 
        /// that it is up-to-date with the current state of matches in this node.
        /// </summary>
        /// <param name="node">The node to add as a successor. Cannot be null.</param>
        public void AddSuccessor(IReteNode node) { node.Parent = this; _successors.Add(node); }

        /// <summary>
        /// Asserts a fact (token) into this CompositeBetaMemory. If the token is not already supported by any branch, 
        /// it will be added to the _supportedMatches dictionary with an initial count of 1, and the assertion will be 
        /// propagated to all successor nodes. If the token is already supported, the count will simply be incremented, 
        /// indicating that another branch supports this match. This method ensures that the node accurately tracks which 
        /// tokens are active based on the branches that support them, allowing for correct propagation of matches through 
        /// the network.
        /// </summary>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            if (fact is Token token)
            {
                if (!_supportedMatches.ContainsKey(token))
                {
                    _supportedMatches[token] = 1;
                    // First time seeing this match: Propagate to next node (And/Then)
                    foreach (var successor in _successors)
                    {
                        successor.Assert(fact);
                    }
                }
                else
                {
                    // Already active via another branch: Just increment support count
                    _supportedMatches[token]++;
                }
            }
        }

        /// <summary>
        /// Retracts a fact (token) from this CompositeBetaMemory. If the token is supported by only one branch, it 
        /// will be removed from the _supportedMatches dictionary, and the retraction will be propagated to all 
        /// successor nodes. If the token is supported by multiple branches, the count will simply be decremented, 
        /// indicating that one less branch supports this match. This method ensures that the node accurately tracks 
        /// which tokens are active based on the branches that support them, allowing for correct propagation of 
        /// matches through the network as conditions change.
        /// </summary>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            if (fact is Token token && _supportedMatches.TryGetValue(token, out int count))
            {
                if (count <= 1)
                {
                    // Last branch supporting this match is gone: Remove and propagate
                    _supportedMatches.Remove(token);
                    foreach (var successor in _successors)
                    {
                        successor.Retract(fact);
                    }
                }
                else
                {
                    // Match is still supported by other branches: Just decrement
                    _supportedMatches[token] = count - 1;
                }
            }
        }

        /// <summary>
        /// Refreshes a fact (token) in this CompositeBetaMemory. This method is called when a property of a fact changes, 
        /// and it allows the node to re-evaluate the token against its conditions. The refresh will be propagated to all 
        /// successor nodes, ensuring that any changes in the token's properties are reflected in the matches that are 
        /// active in those nodes. This method is essential for maintaining the accuracy of matches in the network as facts 
        /// evolve over time.
        /// </summary>
        /// <param name="fact">The fact object whose property is being refreshed. Cannot be null.</param>
        /// <param name="propertyName">The name of the property to refresh. Cannot be null or empty.</param>
        public void Refresh(object fact, string propertyName)
        {
            // Propagate the refresh to all active branches
            foreach (var successor in _successors)
            {
                successor.Refresh(fact, propertyName);
            }
        }

        /// <summary>
        /// Removes a child node from this CompositeBetaMemory. This method is used to detach a successor node from this node,         
        /// which may be necessary when the structure of the Rete network changes. When a child node is removed, it will no 
        /// longer receive assertions, retractions, or refreshes from this CompositeBetaMemory, effectively isolating it from 
        /// the flow of matches through this part of the network. This method allows for dynamic modifications to the network 
        /// structure, enabling the addition and removal of nodes as needed.
        /// </summary>
        /// <param name="successor">The successor node to remove. Cannot be null.</param>
        public void RemoveSuccessor(IReteNode successor)
        {
            _successors.Remove(successor);
        }

        /// <summary>
        /// This method is used for debugging purposes to print the current state of the CompositeBetaMemory. It displays the 
        /// fact being processed, whether it is currently active (supported by at least one branch), and recursively prints
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            if (fact is Token token)
            {
                string indent = new string(' ', level * 2);
                bool isActive = _supportedMatches.ContainsKey(token);
                Console.WriteLine($"{indent}[OR Node] Fact: {fact}, Active: {isActive}");

                foreach (var successor in _successors)
                {
                    successor.DebugPrint(fact, level + 1);
                }
            }
            else
            {
                Console.WriteLine("A token didn't come in to provide a memory.");
            }
        }
    }
}
