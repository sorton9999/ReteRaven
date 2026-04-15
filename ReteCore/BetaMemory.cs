using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    /// <summary>
    /// The BetaMemory class represents a node in a Rete network that stores partial matches (tokens) of facts and propagates them to successor nodes.
    /// It implements the IReteNode interface, allowing it to participate in the Rete network, and the ILatentMemory interface, indicating that it can 
    /// hold tokens that may not yet be fully matched. The BetaMemory is responsible for managing the collection of tokens, handling assertions, 
    /// retractions, and refreshes of facts, and ensuring that successor nodes are updated accordingly when changes occur. It serves as a crucial 
    /// component in the Rete algorithm, enabling efficient pattern matching and rule evaluation by maintaining the state of partial matches and 
    /// facilitating the flow of information through the network.
    /// </summary>
    public class BetaMemory : IReteNode, ILatentMemory
    {
        /// <summary>
        /// The collection of tokens (partial matches) that have been asserted into this BetaMemory. Each token represents a combination of facts 
        /// that have matched certain conditions in the Rete network. The collection allows for efficient checking of whether a token is already 
        /// present, and it serves as the basis for propagating matches to successor nodes when new facts are asserted or existing facts are 
        /// retracted or refreshed. When a new token is asserted, it is added to this collection if it is not already present, and when a fact is 
        /// retracted, any tokens containing that fact are removed from the collection. The presence of tokens in this collection determines 
        /// whether they will be propagated to successor nodes when operations are performed on them.
        /// </summary>
        public List<Token> _tokens = new();
        /// <summary>
        /// A list of successor nodes that will receive tokens asserted, retracted, or refreshed through this BetaMemory. Each successor is an 
        /// IReteNode that will be affected by operations performed on this node. The collection is initialized as an empty list and can be 
        /// modified by adding new successor nodes using the AddSuccessor method. The order of successors in the list may affect the order in 
        /// which tokens are propagated to them, but does not affect the logic of the Rete network.
        /// </summary>
        private readonly List<IReteNode> _successors = new();
        /// <summary>
        /// A collection of tokens that have been asserted into this BetaMemory. Each token represents a combination of facts that have matched 
        /// certain conditions in the Rete network. The collection allows for efficient checking of whether a token is already present, and it 
        /// serves as the basis for propagating matches to successor nodes when new facts are asserted or existing facts are retracted or 
        /// refreshed. When a new token is asserted, it is added to this collection if it is not already present, and when a fact is retracted, 
        /// any tokens containing that fact are removed from the collection. The presence of tokens in this collection determines whether they 
        /// will be propagated to successor nodes when operations are performed on them.
        /// </summary>
        public IEnumerable<Token> Tokens { get { return _tokens; } }

        /// <summary>
        /// Adds a successor node to the list of successors that will receive tokens asserted, retracted, or refreshed through this BetaMemory. 
        /// This method allows for building the Rete network by connecting nodes together. When a new successor is added, it will start 
        /// receiving tokens from this BetaMemory whenever operations are performed on it. The method takes an IReteNode as a parameter and adds 
        /// it to the _successors list, enabling the propagation of tokens to that node in future operations.
        /// </summary>
        /// <param name="node">The node to add as a successor. Cannot be null.</param>
        public void AddSuccessor(IReteNode node) => _successors.Add(node);

        /// <summary>
        /// The Assert method adds a new token to the BetaMemory if it doesn't already exist and propagates it to all successor nodes. It checks 
        /// for duplicates to avoid redundant processing.
        /// </summary>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            if (fact is Token token)
            {
                if (Tokens.Any(t => t.Equals(token))) { return; }
                _tokens.Add(token);
                foreach (var node in _successors)
                {
                    node.Assert(token);
                }
            }
        }

        /// <summary>
        /// The Retract method removes tokens containing the specified fact from the BetaMemory and notifies all successor nodes of the retraction. 
        /// It identifies tokens that include the retracted fact and ensures that they are removed from the memory, allowing successor nodes to 
        /// update their state accordingly.
        /// </summary>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            // Remove tokens containing the retracted fact
            if (fact is Token token)
            {
                var toRemove = Tokens
                    .Where(t => t.NamedFacts.Values.Any(f => f == token.Fact))
                    .ToList();
                foreach (var t in toRemove)
                {
                    _tokens.Remove(t);
                    foreach (var node in _successors) { node.Retract(fact); }
                }
            }
        }

        /// <summary>
        /// The Refresh method is responsible for updating tokens in the BetaMemory when a fact changes. It identifies tokens that contain the 
        /// changed fact and propagates the refresh to all successor nodes, allowing them to re-evaluate their conditions based on the updated 
        /// information. This ensures that the Rete network remains consistent and up-to-date as facts evolve over time.
        /// </summary>
        /// <param name="fact">The fact object whose property is being refreshed. Cannot be null.</param>
        /// <param name="propertyName">The name of the property to refresh. Cannot be null or empty.</param>
        public void Refresh(object fact, string propertyName)
        {
            var affectedTokens = Tokens.Where(t => t.NamedFacts.Values.Contains(fact)).ToList();

            foreach (var token in affectedTokens)
            {
                foreach (var successor in _successors)
                {
                    if (successor is JoinNode join)
                    {
                        join.Refresh(token, propertyName);
                    }
                    else
                    {
                        successor.Refresh(token, propertyName);
                    }
                }
            }
        }

        /// <summary>
        /// The DebugPrint method provides a way to output the current state of the BetaMemory for debugging purposes. 
        /// It prints the number of tokens currently stored in the memory, along with an optional indentation level to 
        /// help visualize the structure of the Rete network. This method can be called to inspect the contents of the 
        /// BetaMemory at any point during execution, allowing developers to understand how facts are being matched and 
        /// propagated through the network. 
        /// The fact parameter is not used in this implementation but can be included for future enhancements or specific 
        /// debugging scenarios.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 4);
            Console.WriteLine($"{indent}[BetaMemory] - Currently holding {_tokens.Count} partial matches.");
        }
    }
}
