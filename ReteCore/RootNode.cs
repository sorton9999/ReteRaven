//-----------------------------------------------------------------------
// <copyright file="RootNode.cs">
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
    /// The RootNode class represents the root of the Rete network. It serves as the entry point for all facts asserted into the network and is 
    /// responsible for propagating facts to its successor nodes. The RootNode does not perform any specific processing on the facts but simply serves 
    /// as a starting point for the flow of facts through the network. This class provides the foundation for how facts are introduced and propagated 
    /// through the network.
    /// </summary>
    public class RootNode : IReteNode
    {
        public IReteNode? Parent { get; set; } = null;
        private readonly List<IReteNode> _children = new();
        public IEnumerable<IReteNode> Successors => _children;
        public void AddSuccessor(IReteNode node) { node.Parent = this; _children.Add(node); }
        public void Assert(object fact) => _children.ForEach(c => c.Assert(fact));
        public void Retract(object fact) => _children.ForEach(c => c.Retract(fact));
        public void Refresh(object fact, string propertyName)
        {
            foreach (var child in _children)
            {
                child.Refresh(fact, propertyName);
            }
        }
        public ObjectTypeNode<T> GetSuccessor<T>()
        {
            return _children
                .OfType<ObjectTypeNode<T>>()
                .FirstOrDefault();
        }
        /// <summary>
        /// Removes a successor node from this RootNode. This method is used when a successor node is no longer needed or when the 
        /// structure of the Rete network changes. When a successor is removed, it will no longer receive any tokens asserted, 
        /// retracted, or refreshed through this node. The method simply removes the specified child node from the list of successors, 
        /// ensuring that it is no longer part of the propagation path for tokens. This allows for dynamic modification of the Rete network 
        /// as it evolves over time and ensures that nodes can be added or removed as needed without affecting the overall functionality of 
        /// the system.
        /// </summary>
        /// <param name="successor"></param>
        public void RemoveSuccessor(IReteNode successor)
        {
            _children.Remove(successor);
        }

        public void DebugPrint(object fact, int level = 0)
        {
            Console.WriteLine($"[RootNode] Fact: {fact}");
            foreach (var child in _children) child.DebugPrint(fact, level + 1);
        }
    }
}
