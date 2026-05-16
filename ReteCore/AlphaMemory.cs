//-----------------------------------------------------------------------
// <copyright file="AlphaMemory.cs">
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
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ReteCore
{
    /// <summary>
    /// The AlphaMemory class represents a node in a Rete network that stores individual facts and propagates them to successor nodes. 
    /// It maintains a collection of facts that have been asserted and provides methods for asserting, retracting, and refreshing facts. 
    /// When a fact is asserted, it is added to the collection and propagated to all successor nodes. When a fact is retracted, it is 
    /// removed from the collection and successors are notified of the retraction. The Refresh method allows for updating the state of a 
    /// fact without changing its presence in the memory. 
    /// This class is a fundamental component of the Rete algorithm, enabling efficient pattern matching and rule evaluation in a rule engine.
    /// </summary>
    public class AlphaMemory : IReteNode
    {
        /// <summary>
        /// A collection of facts that have been asserted into this AlphaMemory. Each fact is stored as an object, and the collection 
        /// allows for efficient checking of whether a fact is already present. When a new fact is asserted, it is added to this collection 
        /// if it is not already present, and when a fact is retracted, it is removed from the collection. The presence of a fact in this 
        /// collection determines whether it will be propagated to successor nodes when operations are performed on it.
        /// </summary>
        public List<object> Facts { get; } = new();

        /// <summary>
        /// The list of successor nodes that will receive facts asserted, retracted, or refreshed through this AlphaMemory. Each successor 
        /// is an IReteNode that will be affected by operations performed on this node. The collection is initialized as an empty list and 
        /// can be modified by adding new successor nodes using the AddSuccessor method. The order of successors in the list may affect the 
        /// order in which facts are propagated to them, but does not affect the logic of the Rete network.
        /// </summary>
        private readonly List<IReteNode> _successors = new();

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
        /// Adds a successor node to this AlphaMemory. Successor nodes will receive facts asserted, retracted, or refreshed through this 
        /// AlphaMemory. This method allows for building the Rete network by connecting nodes together. When a new successor is added, 
        /// it will immediately receive all existing facts in this AlphaMemory through the Assert method, ensuring that the new node is 
        /// up-to-date with the current state of facts.
        /// </summary>
        /// <param name="node">The node to add as a successor. Cannot be null.</param>
        public void AddSuccessor(IReteNode node) { node.Parent = this; _successors.Add(node); }

        /// <summary>
        /// The Assert method adds a fact to the AlphaMemory if it is not already present and propagates it to all successor nodes.
        /// </summary>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            if (!Facts.Contains(fact))
            {
                Facts.Add(fact);

                foreach (var succ in _successors)
                {
                    succ.Assert(fact);
                }
            }
        }

        /// <summary>
        /// The Retract method removes a fact from the AlphaMemory if it exists and notifies all successor nodes of the retraction.
        /// </summary>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            if (Facts.Remove(fact))
            {
                // Tell successors this fact is gone
                foreach (var succ in _successors)
                {
                    succ.Retract(fact);
                }
            }
        }

        /// <summary>
        /// The Refresh method is used to update the state of a fact in the AlphaMemory without changing its presence. If the fact exists, 
        /// it propagates the refresh to all successor nodes, allowing them to update their state based on the new information.
        /// </summary>
        /// <param name="fact">The fact object whose property is being refreshed. Cannot be null.</param>
        /// <param name="propertyName">The name of the property to refresh. Cannot be null or empty.</param>
        public void Refresh(object fact, string propertyName)
        {
            if (Facts.Contains(fact))
            {
                foreach (var succ in _successors)
                {
                    succ.Refresh(fact, propertyName);
                }
            }
        }

        /// <summary>
        /// Remove the given child node from the list of successors. This method is used to disconnect a node from this AlphaMemory, preventing it 
        /// from receiving further facts asserted, retracted, or refreshed through this node. When a child node is removed, it will no longer be 
        /// part of the propagation path for facts in this AlphaMemory, which can affect the flow of information and the activation of rules in the 
        /// Rete network. This method allows for dynamic modification of the network structure by removing connections between nodes as needed.
        /// </summary>
        /// <param name="successor">The successor node to remove. Cannot be null.</param>
        public void RemoveSuccessor(IReteNode successor)
        {
            _successors.Remove(successor);
        }

        /// <summary>
        /// A debugging method that prints the current state of the AlphaMemory, including whether a specific fact is present and the total 
        /// number of facts stored.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 4);
            bool contains = Facts.Contains(fact);
            Console.WriteLine($"{indent}[AlphaMemory] - Fact present: {contains}. Total facts stored: {Facts.Count}");
        }
    }
}
