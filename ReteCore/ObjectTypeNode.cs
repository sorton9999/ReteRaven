//-----------------------------------------------------------------------
// <copyright file="ObjectTypeNode.cs">
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
    /// The ObjectTypeNode class is a specialized node in a Rete network that filters incoming facts based on their type. It only propagates 
    /// facts that match the specified type parameter T to its successor nodes. This allows for efficient routing of facts through the network, 
    /// ensuring that only relevant facts are processed by downstream nodes. The ObjectTypeNode serves as a crucial component in the Rete 
    /// algorithm, enabling the network to handle complex patterns and conditions by categorizing facts according to their types.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ObjectTypeNode<T> : IReteNode
    {
        public IReteNode Parent { get; set; }
        private readonly List<IReteNode> _children = new();
        public IEnumerable<IReteNode> Successors => _children;
        public void AddSuccessor(IReteNode node) { node.Parent = this; _children.Add(node); }
        public void Assert(object fact) { if (fact is T) _children.ForEach(c => c.Assert(fact)); }
        public void Retract(object fact) { if (fact is T) _children.ForEach(c => c.Retract(fact)); }
        public void Refresh(object fact, string propertyName)
        {
            if (fact is T typedFact)
            {
                foreach (var child in _children)
                {
                    child.Refresh(fact, propertyName);
                }
            }
        }
        /// <summary>
        /// Removes a successor node from this RootNode. This method is used when a successor node is no longer needed or when the 
        /// structure of the Rete network changes. When a successor is removed, it will no longer receive any tokens asserted, 
        /// retracted, or refreshed through this node. The method simply removes the specified child node from the list of successors, 
        /// ensuring that it is no longer part of the propagation path for tokens. This allows for dynamic modification of the Rete network 
        /// as it evolves over time and ensures that nodes can be added or removed as needed without affecting the overall functionality of 
        /// the system.
        /// </summary>
        /// <param name="successor">The successor node to remove. Cannot be null.</param>
        public void RemoveSuccessor(IReteNode successor)
        {
            _children.Remove(successor);
        }

        public void DebugPrint(object fact, int level = 0)
        {
            bool match = fact is T;
            string indent = new string(' ', level * 2);
            Console.WriteLine($"{indent}[TypeNode:{typeof(T).Name}] {(match ? "MATCH" : "SKIP")}");
            if (match) foreach (var child in _children) child.DebugPrint(fact, level + 1);
        }
    }
}
