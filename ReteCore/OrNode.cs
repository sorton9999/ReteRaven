//-----------------------------------------------------------------------
// <copyright file="OrNode.cs">
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
    /// Represents a node in a Rete network that forwards facts to multiple successor nodes, effectively implementing a
    /// logical OR branch.
    /// </summary>
    /// <remarks>The OrNode is typically used within a Rete-based rule engine to allow a fact to be propagated
    /// to several alternative branches in the network. When a fact is asserted, retracted, or refreshed, the operation
    /// is forwarded to all successor nodes. This enables rules that require matching on any of several conditions to be
    /// efficiently represented. The OrNode does not perform any filtering or transformation of facts; it simply passes
    /// them through to its successors.</remarks>
    public class OrNode : IReteNode
    {
        /// <summary>
        /// A list of successor nodes that will receive facts asserted, retracted, or refreshed through this 
        /// OrNode. Each successor is an IReteNode that will be affected by operations performed on this node. 
        /// The collection is initialized as an empty list and can be modified by adding new successor nodes 
        /// using the AddSuccessor method. The order of successors in the list may affect the order in which 
        /// facts are propagated to them, but does not affect the logic of the Rete network.
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
        /// Adds the specified node as a successor to this node in the Rete network.
        /// </summary>
        /// <param name="node">The node to add as a successor. Cannot be null.</param>
        public void AddSuccessor(IReteNode node) { node.Parent = this; _successors.Add(node); }

        /// <summary>
        /// Propagates the specified fact to all successor nodes for further processing.
        /// </summary>
        /// <remarks>This method forwards the provided fact to each successor node in the current node's
        /// collection. The fact is not modified by this method.</remarks>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            foreach (var successor in _successors)
            {
                successor.Assert(fact);
            }
        }

        /// <summary>
        /// Retracts the specified fact from the rule engine, removing it from further consideration in rule evaluation.
        /// </summary>
        /// <remarks>If the specified fact is not currently asserted, this method has no effect.
        /// Retracting a fact may cause dependent rules to be reevaluated or deactivated.</remarks>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            foreach (var successor in _successors) { successor.Retract(fact); }
        }

        /// <summary>
        /// Propagates a refresh operation for the specified fact and property to all successor nodes.
        /// </summary>
        /// <remarks>Use this method to notify all successor nodes that a particular property of a fact
        /// has changed and may require re-evaluation. This method does not perform any refresh logic itself but
        /// delegates the operation to its successors.</remarks>
        /// <param name="fact">The fact object whose property is being refreshed. Cannot be null.</param>
        /// <param name="prop">The name of the property to refresh. Cannot be null or empty.</param>
        public void Refresh(object fact, string prop)
        {
            foreach (var successor in _successors) { successor.Refresh(fact, prop); }
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
        /// Writes a formatted debug message to the console that includes the specified fact and the current number of
        /// successors.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 3);
            Console.WriteLine($"{indent}[OrNode] - Currently holding {_successors.Count} successors. Fact[{fact}]");
        }
    }
}
