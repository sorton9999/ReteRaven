//-----------------------------------------------------------------------
// <copyright file="AlphaConditionNode.cs">
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
    /// The AlphaConditionNode class represents a node in a Rete network that applies a simple condition (predicate) to incoming facts.
    /// </summary>
    /// <typeparam name="T">The fact is of this type.</typeparam>
    public class AlphaConditionNode<T> : IReteNode
    {
        /// <summary>
        /// The predicate function that defines the condition to be applied to facts of type T. This function takes 
        /// an instance of T as input and returns a boolean indicating whether the fact satisfies the condition. The 
        /// predicate is evaluated for each fact that reaches this node, and only those facts that return true will 
        /// be propagated to the successor node. This allows for efficient filtering of facts based on specific 
        /// criteria defined by the predicate.
        /// </summary>
        private readonly Func<T, bool> _predicate;
        /// <summary>
        /// The successor node that will receive facts that satisfy the condition defined by the predicate. In a 
        /// typical Rete network, this is often an AlphaMemory node that stores facts that pass the condition for 
        /// further processing. The successor node is set at construction and is responsible for receiving and 
        /// handling facts that meet the criteria defined by this AlphaConditionNode. This design allows for a 
        /// clear separation of concerns, where the AlphaConditionNode focuses on filtering facts based on the 
        /// predicate, while the successor node manages the storage and propagation of those facts within the 
        /// Rete network.
        /// </summary>
        private readonly IReteNode _successor;
        /// <summary>
        /// This HashSet is used to keep track of facts that have already matched the condition defined by the predicate.
        /// </summary>
        private readonly HashSet<object> _matchedFacts = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// This constructor initializes a new instance of the AlphaConditionNode class with the specified property 
        /// name, predicate, and successor node. The property name is used for selective re-evaluation when facts 
        /// change, while the predicate defines the condition that facts must satisfy to be propagated to the successor. 
        /// The successor node is where facts that pass the condition will be sent for further processing in the Rete 
        /// network. This constructor sets up the necessary components for the AlphaConditionNode to function as a 
        /// filter within the Rete algorithm, allowing it to efficiently evaluate incoming facts and manage their flow 
        /// through the network based on defined conditions.
        /// </summary>
        /// <param name="propertytName">The name of the fact</param>
        /// <param name="predicate">The filtering condition. All facts that satisfy this condition are propagated forward.</param>
        /// <param name="successor">The successor node to propagate the fact to.</param>
        public AlphaConditionNode(string propertytName, Func<T, bool> predicate, IReteNode successor)
        {
            TargetProperty = propertytName;
            _predicate = predicate;
            _successor = successor;
        }

        /// <summary>
        /// The name of the property that this AlphaConditionNode is interested in. This is used for selective re-evaluation 
        /// when a fact changes. If the changed property matches TargetProperty, the node will re-evaluate the condition for 
        /// that fact. If TargetProperty is null or empty, it will re-evaluate for any change, which is less efficient but 
        /// necessary if the condition depends on multiple properties or the entire object state.
        /// </summary>
        public string TargetProperty { get; }

        /// <summary>
        /// The parent node in the Rete network. This property allows for navigation back up the network from this node.
        /// </summary>
        public IReteNode? Parent { get; set; }

        /// <summary>
        /// Returns an enumerable of successor nodes. In this implementation, there is typically one successor node that receives 
        /// facts that satisfy the condition defined by the predicate.
        /// </summary>
        public IEnumerable<IReteNode> Successors { get { return new[] { _successor }; } }

        /// <summary>
        /// An AlphaConditionNode typically has one successor, which is often an AlphaMemory node that stores facts that 
        /// pass the condition. The AddSuccessor method is provided for completeness, but in a typical Rete implementation, 
        /// the successor is set at construction and does not change dynamically. If you want to support multiple successors, 
        /// you would need to modify this class to maintain
        /// </summary>
        /// <param name="node">The node to add as a successor. Cannot be null.</param>
        public void AddSuccessor(IReteNode node) 
        {
            node.Parent = this;
            Console.WriteLine("[AlphaConditionNode] -- This node does not implement AddSuccessor.\n" +
            "Implement this to support multiple successors from this node."); 
        }

        /// <summary>
        /// Asserts a fact only when it passes the predicate and is not already tracked as a match.
        /// </summary>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            if (fact is T typedFact && _predicate(typedFact) && _matchedFacts.Add(fact))
            {
                _successor.Assert(typedFact);
            }
        }

        /// <summary>
        /// Retracts a fact from the successor only if this node previously tracked it as a match.
        /// </summary>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            if (fact is T typedFact && _matchedFacts.Remove(fact))
            {
                _successor.Retract(typedFact);
            }
        }

        /// <summary>
        /// Re-evaluates the predicate and propagates only a match transition; unchanged matches are refreshed downstream.
        /// </summary>
        /// <param name="fact">The fact object whose property is being refreshed. Cannot be null.</param>
        /// <param name="changedProperty">The name of the property to refresh. Cannot be null or empty.</param>
        public void Refresh(object fact, string changedProperty)
        {
            // Until conditions expose explicit property dependencies, conservatively re-evaluate
            // every change. TargetProperty is a diagnostic label, not a dependency declaration.
            if (fact is T typedFact)
            {
                bool wasMatch = _matchedFacts.Contains(fact);
                bool isMatch = _predicate(typedFact);

                if (wasMatch && !isMatch)
                {
                    _matchedFacts.Remove(fact);
                    _successor.Retract(fact);
                }
                else if (!wasMatch && isMatch)
                {
                    _matchedFacts.Add(fact);
                    _successor.Assert(typedFact);
                }
                else if (wasMatch)
                {
                    _successor.Refresh(fact, changedProperty);
                }
            }
        }

        /// <summary>
        /// Not implemented. This node does not support dynamic removal of successors. If you need to support
        /// this functionality, you should implement it in a subclass or modify this class to handle multiple
        /// successors.
        /// </summary>
        /// <param name="successor">The successor node to remove. Cannot be null.</param>
        public void RemoveSuccessor(IReteNode successor)
        {
            if (successor == _successor)
            {
                Console.WriteLine("[AlphaConditionNode] -- This node does not implement RemoveSuccessor.\n" +
                "Implement this to support dynamic changes to the successor of this node.");
            }
        }

        /// <summary>
        /// This method is used for debugging purposes to print the structure of the Rete network and the evaluation results of 
        /// facts at this node.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            if (fact is T typedFact)
            {
                bool pass = _predicate(typedFact);
                Console.WriteLine($"{indent}[AlphaNode:{TargetProperty}] {(pass ? "PASS" : "FAIL")}");
                if (pass)
                {
                    _successor.DebugPrint(fact, level + 1);
                }
            }
        }
    }
}
