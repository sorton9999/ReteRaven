//-----------------------------------------------------------------------
// <copyright file="AlphaToBetaAdapter.cs">
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
    /// Adapts an Alpha Memory output (single fact) to a Beta Memory input (Token).
    /// This allows the first JoinNode in a chain to receive a Token on its left.
    /// </summary>
    public class AlphaToBetaAdapter : IReteNode
    {
        /// <summary>
        /// The BetaMemory instance that this adapter will feed tokens into. When a fact is asserted into the adapter, it will be 
        /// wrapped into a Token and asserted into this BetaMemory.
        /// </summary>
        private readonly BetaMemory _betaMemory;
        /// <summary>
        /// The name of the fact being adapted. This is used to create a Token with a consistent identifier for the fact when it is 
        /// wrapped and asserted into the BetaMemory. The fact name helps maintain clarity in the rule network and can be used for 
        /// debugging or tracing purposes to identify which facts are being processed through this adapter.
        /// </summary>
        private readonly string _factName;

        /// <summary>
        /// This constructor initializes a new instance of the AlphaToBetaAdapter class with the specified BetaMemory and fact name. 
        /// The adapter will take facts asserted into it, wrap them into Tokens with the given fact name, and assert those tokens into 
        /// the provided BetaMemory. This allows for seamless integration of Alpha Memory outputs into the Beta Memory processing 
        /// pipeline, enabling the first JoinNode in a rule to receive tokens that represent individual facts from the Alpha Memory.
        /// </summary>
        /// <param name="betaMemory">The BetaMemory to add the token and associated fact to</param>
        /// <param name="factName">The name of the fact</param>
        /// <exception cref="ArgumentNullException">Thrown on a null BetaMemory argument</exception>
        public AlphaToBetaAdapter(BetaMemory betaMemory, string factName)
        {
            _betaMemory = betaMemory ?? throw new ArgumentNullException(nameof(betaMemory));
            _factName = factName;
        }

        /// <summary>
        /// Returns an enumerable of successor nodes. In this implementation, there is typically one successor node that receives 
        /// facts that satisfy the condition defined by the predicate.
        /// </summary>
        public IEnumerable<IReteNode> Successors
        {
            get
            {
                return new List<IReteNode> { _betaMemory };
            }
        }

        /// <summary>
        /// The parent node in the Rete network. This property allows for navigation back up the network from this node.
        /// </summary>
        public IReteNode? Parent { get; set; }

        /// <summary>
        /// Adds a successor node to this adapter. In the context of the Rete network, this method is used to connect the output of 
        /// the adapter (which produces Tokens) to the next node in the network, typically a JoinNode or another BetaMemory. When a 
        /// fact is asserted into this adapter, it will be wrapped into a Token and then passed to all successor nodes that have been 
        /// added through this method. This allows the adapter to serve as a bridge between Alpha Memory outputs and the Beta Memory 
        /// processing that follows in the rule evaluation process.
        /// </summary>
        /// <param name="node"></param>
        public void AddSuccessor(IReteNode node)
        {
            Console.WriteLine("[AlphaToBetaAdapter] -- This node currently does not implement AddSuccessor.\n" +
            "Operations are done on the added BetaMemory explicitly to start the chain.\n" +
            "In the future this may be used for BetaMemory to BetaMemory connections.");
        }

        /// <summary>
        /// Asserts a fact into the adapter. The fact is wrapped into a Token with the specified fact name and then asserted into 
        /// the connected BetaMemory. This allows the first JoinNode in the rule network to receive a Token representing the fact 
        /// from the Alpha Memory, enabling it to participate in the rule evaluation process as if it were a standard token from a 
        /// BetaMemory. The fact name is used to maintain clarity and consistency in the tokens being processed through the network.
        /// </summary>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            // Wrap the single fact into the first Token of a potential chain
            var initialToken = new Token(_factName, fact);
            _betaMemory?.Assert(initialToken);
        }

        /// <summary>
        /// Retracts a fact from the adapter. This method will remove any tokens from the connected BetaMemory that contain the 
        /// specified fact. Since the adapter wraps facts into tokens, it relies on the BetaMemory's Retract method to identify 
        /// and remove any tokens that include the retracted fact. This ensures that when a fact is retracted, all relevant tokens 
        /// in the BetaMemory are updated accordingly, allowing successor nodes to adjust their state based on the change in facts.
        /// </summary>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            // BetaMemory.Retract is already designed to find and remove 
            // any Tokens containing this specific fact.
            _betaMemory?.Retract(fact);
        }

        /// <summary>
        /// Refreshes a fact in the adapter. This method will trigger a re-evaluation of any tokens in the connected BetaMemory 
        /// that contain the specified fact, based on the property that has changed. The adapter relies on the BetaMemory's 
        /// Refresh method to identify relevant tokens and propagate the refresh to successor nodes, allowing them to re-evaluate 
        /// their conditions based on the updated information. This is crucial for maintaining the accuracy and responsiveness of 
        /// the Rete network as facts evolve over time.
        /// </summary>
        /// <param name="fact">The fact object whose property is being refreshed. Cannot be null.</param>
        /// <param name="propertyName">The name of the property to refresh. Cannot be null or empty.</param>
        public void Refresh(object fact, string propertyName)
        {
            _betaMemory?.Refresh(fact, propertyName);
        }

        /// <summary>
        /// This method is intended to remove a successor node from the adapter. However, in the current implementation, the adapter 
        /// does not support dynamic modification of its successors, as it is designed to feed directly into a specific BetaMemory.
        /// </summary>
        /// <param name="node">The node to be removed as a successor. This parameter is ignored in the current implementation.</param>
        public void RemoveSuccessor(IReteNode node)
        {
            Console.WriteLine("[AlphaToBetaAdapter] -- This node currently does not implement RemoveSuccessor.\n" +
            "Operations are done on the added BetaMemory explicitly to start the chain.\n" +
            "In the future this may be used for BetaMemory to BetaMemory connections.");
        }

        /// <summary>
        /// A visual debugging method that prints the fact being processed and the internal state of the connected BetaMemory. 
        /// This method can be used to trace the flow of facts through the adapter and into the BetaMemory, providing insight 
        /// into how facts are being wrapped into tokens and how they are being processed by successor nodes. The level 
        /// parameter can be used to control the indentation of the output for better readability when visualizing complex 
        /// rule networks.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 4);
            Console.WriteLine($"{indent}[AlphaToBetaAdapter] Wrapping Fact: {fact}");
            _betaMemory?.DebugPrint(fact, level + 1);
        }
    }
}
