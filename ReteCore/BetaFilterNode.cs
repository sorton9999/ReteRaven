using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ReteCore
{
    /// <summary>
    /// A Beta-network filter node that evaluates a predicate directly against incoming <see cref="Token"/> 
    /// instances—eliminating redundant <see cref="AlphaMemory"/> self-joins when testing properties 
    /// on already-bound aliases or aggregated variables (such as <c>Having</c> clauses).
    /// </summary>
    public class BetaFilterNode : IReteNode
    {
        /// <summary>
        /// The predicate evaluated against each incoming <see cref="Token"/> to determine whether it passes downstream.
        /// </summary>
        private readonly Func<Token, bool> _predicate;
        /// <summary>
        /// Tracks the set of <see cref="Token"/> instances that currently satisfy <see cref="_predicate"/>, 
        /// allowing <see cref="Refresh"/> to detect state transitions and issue retractions or assertions accurately.
        /// </summary>
        private readonly HashSet<Token> _passedTokens = new();
        /// <summary>
        /// Downstream Rete nodes subscribed to token assertions, retractions, and refreshes from this filter.
        /// </summary>
        private readonly List<IReteNode> _successors = new();

        /// <summary>
        /// The parent node in the Rete network. This property allows for navigation back up the network from this node.
        /// </summary>
        IReteNode? IReteNode.Parent { get; set; }

        /// <summary>
        /// Returns an enumerable of successor nodes. In this implementation, there is typically one successor node that receives 
        /// facts that satisfy the condition defined by the predicate.
        /// </summary>
        IEnumerable<IReteNode> IReteNode.Successors => _successors;

        /// <summary>
        /// Registers a downstream <see cref="IReteNode"/> successor and sets its parent reference to this node.
        /// </summary>
        /// <param name="successor">The downstream Rete node to connect.</param>
        public void AddSuccessor(IReteNode successor) { successor.Parent = this; _successors.Add(successor); }

        /// <summary>
        /// Initializes a new instance of <see cref="BetaFilterNode"/> with the specified token predicate.
        /// </summary>
        /// <param name="predicate">The condition evaluated against incoming <see cref="Token"/> matches.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is <see langword="null"/>.</exception>
        public BetaFilterNode(Func<Token, bool> predicate)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>
        /// Evaluates an incoming <see cref="Token"/> against <see cref="_predicate"/> and propagates it 
        /// to all downstream successors if the condition passes.
        /// </summary>
        /// <param name="fact">The incoming <see cref="Token"/> from an upstream Beta memory or node.</param>
        public void Assert(object fact)
        {
            if (fact is not Token token) return;

            if (_predicate(token))
            {
                _passedTokens.Add(token);
                foreach (var s in _successors) s.Assert(token);
            }
        }

        /// <summary>
        /// Removes a retracted <see cref="Token"/> from <see cref="_passedTokens"/> and cascades 
        /// the retraction downstream if it had previously passed this filter.
        /// </summary>
        /// <param name="fact">The <see cref="Token"/> being retracted.</param>
        public void Retract(object fact)
        {
            if (fact is not Token token) return;

            if (_passedTokens.Remove(token))
            {
                foreach (var s in _successors) s.Retract(token);
            }
        }

        /// <summary>
        /// Re-evaluates <see cref="_predicate"/> when a fact inside <paramref name="fact"/> mutates, 
        /// asserting newly qualifying tokens, retracting tokens that no longer pass, or forwarding 
        /// refreshes for tokens that remain valid.
        /// </summary>
        /// <param name="fact">The modified <see cref="Token"/> being refreshed.</param>
        /// <param name="propertyName">The name of the property that changed on an underlying fact.</param>
        public void Refresh(object fact, string propertyName)
        {
            if (fact is not Token token) return;

            bool wasPassed = _passedTokens.Contains(token);
            bool nowPasses = _predicate(token);

            if (nowPasses && !wasPassed)
            {
                _passedTokens.Add(token);
                foreach (var s in _successors) s.Assert(token);
            }
            else if (!nowPasses && wasPassed)
            {
                _passedTokens.Remove(token);
                foreach (var s in _successors) s.Retract(token);
            }
            else if (nowPasses && wasPassed)
            {
                foreach (var s in _successors) s.Refresh(token, propertyName);
            }
        }

        /// <summary>
        /// Removes a successor node from this node. This method is used when a successor node is no longer needed or when the 
        /// structure of the Rete network changes. When a successor is removed, it will no longer receive any tokens asserted, 
        /// retracted, or refreshed through this node. The method simply removes the specified child node from the list of successors, 
        /// ensuring that it is no longer part of the propagation path for tokens.
        /// </summary>
        /// <param name="successor">The successor node to remove. Cannot be null.</param>
        void IReteNode.RemoveSuccessor(IReteNode successor)
        {
            _successors.Remove(successor);
        }

        /// <summary>
        /// A debug method to print the current state of the node, including the number of left tokens.  This can be useful for understanding
        /// how the node is processing matches and how it is affected by incoming facts and tokens. 
        /// </summary>
        /// <param name="fact">The fact whose information is to be output.</param>
        /// <param name="level">A level of indentation.</param>
        void IReteNode.DebugPrint(object fact, int level)
        {
            string indent = new string(' ', level * 4);
            Console.WriteLine($"{indent}[BetaFilterNode] - Currently holding {_passedTokens.Count} partial matches.");
        }
    }
}
