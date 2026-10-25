//-----------------------------------------------------------------------
// <copyright file="JoinNode.cs">
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
    /// Represents a join node in a RETE network that combines partial matches from the left input with facts from the
    /// right input based on a specified condition.
    /// </summary>
    /// <remarks>A JoinNode is a core component of the RETE algorithm, used in rule engines to efficiently
    /// match patterns against a set of facts. It receives tokens (partial matches) from its left input and facts from
    /// its right input, evaluating a join condition to determine which combinations should be propagated to successor
    /// nodes. The JoinNode manages both left and right activations and supports retraction and refresh operations to
    /// maintain consistency as facts change. Thread safety is not guaranteed; external synchronization may be required
    /// if used concurrently.</remarks>
    public class JoinNode : IReteNode
    {
        /// <summary>
        /// The name assigned to the new fact being joined.
        /// </summary>
        private readonly string _nextName;
        /// <summary>
        /// Represents the left input node that supplies tokens from previous join operations.
        /// </summary>
        private readonly IReteNode? _leftInput;
        /// <summary>
        /// Represents the right input memory containing individual facts for this node.
        /// </summary>
        private readonly AlphaMemory? _rightInput;
        /// <summary>
        /// The condition function that evaluates whether a given token from the left input and a fact from the 
        /// right input satisfy the join criteria. This function should return true if the token and fact can 
        /// be joined together, and false otherwise. The condition is typically defined based on the specific 
        /// rule being implemented and may involve checking properties of the token's facts against properties 
        /// of the right input fact.
        /// </summary>
        private readonly Func<Token, object, bool> _condition;
        /// <summary>
        /// The list of join matches that have been found by this node. Each JoinMatch represents a successful 
        /// combination of a token from the left input and a fact from the right input that satisfy the join condition.
        /// </summary>
        private readonly List<JoinMatch> _matches = new();
        /// <summary>
        /// The list of successor nodes that will receive new tokens when a successful join occurs. Each 
        /// successor is an IReteNode that will be affected by operations performed on this JoinNode. 
        /// The collection is initialized as an empty list and can be modified by adding new successor nodes.
        /// </summary>
        private readonly List<IReteNode> _successors = new();

        /// <summary>
        /// A JoinNode combines tokens from the left input with facts from the right input based on a specified condition. 
        /// When a new token arrives from the left input, it is evaluated against all facts in the right input memory 
        /// using the provided condition function. If the condition is satisfied, a new token is created that combines the 
        /// left token and the right fact, and this new token is propagated to all successor nodes. The JoinNode also 
        /// supports retraction and refresh operations to maintain consistency as facts change. 
        /// The constructor sets up the necessary connections to the left and right inputs and registers this node as a 
        /// successor to those inputs as needed.
        /// </summary>
        /// <param name="left">The Beta memory holding the token to evaluate</param>
        /// <param name="right">The Alpha memory holding the fact</param>
        /// <param name="name">The name of the token to evaluate</param>
        /// <param name="cond">The predicate to evaluate which takes a Token and a Fact object and returns a boolean to
        /// indicate whether or not the condition succeeds.</param>
        public JoinNode(IReteNode? left, AlphaMemory? right, string name, Func<Token, object, bool> cond)
        {
            _nextName = name;
            _leftInput = left;
            _rightInput = right;
            _condition = cond;
            if (_leftInput is BetaMemory beta)
            {
                beta.AddSuccessor(this);
            }
            else if (_leftInput is CompositeBetaMemory composite)
            {
                composite.AddSuccessor(this);
            }
            right?.AddSuccessor(this);
        }

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
        /// The Assert method is responsible for processing new facts or tokens that arrive at this JoinNode. When a new fact 
        /// is asserted, the method evaluates the join condition against all existing tokens from the left input (if the fact 
        /// is from the right input) or against all existing facts from the right input (if the fact is from the left input). 
        /// If the condition is satisfied for any combination of token and fact, a new token is created that combines the left 
        /// token and the right fact, and this new token is propagated to all successor nodes. This method ensures that the 
        /// JoinNode correctly evaluates the join condition and maintains the flow of tokens through the Rete network as new 
        /// facts are introduced.
        /// </summary>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            if (fact is Token leftToken)
            {
                if (_rightInput == null) return;
                foreach (var rightFact in _rightInput.Facts.ToList())
                {
                    ReconcileMatch(leftToken, rightFact);
                }
            }
            else
            {
                if (_leftInput is not ILatentMemory leftMemory) return;
                foreach (var token in leftMemory.Tokens.ToList())
                {
                    ReconcileMatch(token, fact);
                }
            }
        }

        /// <summary>
        /// Removes all recorded pairs containing the retracted input and retracts their derived tokens downstream.
        /// </summary>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            // Identify all matches that involve the retracted fact or token.
            // Depending on whether the retracted input is a Token or a fact,
            // we filter the matches accordingly.
            var matchesToRemove = fact is Token token
                ? _matches.Where(match => ReferenceEquals(match.Left, token)).ToList()
                : _matches.Where(match => ReferenceEquals(match.Right, fact)).ToList();

            foreach (var match in matchesToRemove)
            {
                _matches.Remove(match);
                foreach (var successor in _successors)
                {
                    successor.Retract(match.Output);
                }
            }
        }

        /// <summary>
        /// Re-evaluates affected pairs, retracting stale outputs and refreshing outputs whose match remains valid. This method ensures that the internal state
        /// of the node remains consistent after a match has been refreshed.
        /// </summary>
        /// <param name="factOrToken">The fact from the right side or Token from the left to update.</param>
        /// <param name="propertyName">The name of the property being updated</param>
        public void Refresh(object factOrToken, string propertyName)
        {
            if (factOrToken is Token token)
            {
                if (_rightInput == null) { return; }
                foreach (var rightFact in _rightInput.Facts.ToList())
                {
                    ReconcileMatch(token, rightFact, propertyName);
                }
            }
            else
            {
                if (_leftInput is not ILatentMemory leftMemory) { return; }
                foreach (var leftToken in leftMemory.Tokens.ToList())
                {
                    ReconcileMatch(leftToken, factOrToken, propertyName);
                }
            }
        }

        /// <summary>
        /// Removes a successor node from this JoinNode. This method is used when a successor node is no longer needed or when the 
        /// structure of the Rete network changes. When a successor is removed, it will no longer receive any tokens asserted, 
        /// retracted, or refreshed through this node.
        /// </summary>
        /// <param name="successor">The successor node to remove. Cannot be null.</param>
        public void RemoveSuccessor(IReteNode successor)
        {
            _successors.Remove(successor);
        }

        /// <summary>
        /// Reconciles one pair and propagates only new, removed, or refreshed join outputs. This method ensures that the internal state
        /// of the node remains consistent after a match has been evaluated.
        /// </summary>
        /// <param name="left">The left side token</param>
        /// <param name="right">The right side fact object</param>
        private void ReconcileMatch(Token left, object right, string? propertyName = null)
        {
            var existingMatch = _matches.FirstOrDefault(match =>
                ReferenceEquals(match.Left, left) && ReferenceEquals(match.Right, right));
            bool isMatch = _condition(left, right);

            if (isMatch && existingMatch == null)
            {
                var output = new Token(left, _nextName, right);
                _matches.Add(new JoinMatch(left, right, output));
                foreach (var successor in _successors)
                {
                    successor.Assert(output);
                }
            }
            else if (!isMatch && existingMatch != null)
            {
                _matches.Remove(existingMatch);
                foreach (var successor in _successors)
                {
                    successor.Retract(existingMatch.Output);
                }
            }
            else if (isMatch && existingMatch != null && propertyName != null)
            {
                foreach (var successor in _successors)
                {
                    successor.Refresh(existingMatch.Output, propertyName);
                }
            }
        }

        /// <summary>
        /// The JoinMatch record represents a successful combination of a token from the left input and a fact from the right
        /// input that satisfy the join condition.
        /// </summary>
        /// <param name="Left">The token from the left input.</param>
        /// <param name="Right">The fact from the right input.</param>
        /// <param name="Output">The resulting token that represents the join of the left and right inputs.</param>
        private sealed record JoinMatch(Token Left, object Right, Token Output);

        /// <summary>
        /// The DebugPrint method is a utility function that outputs the current state of the JoinNode for debugging purposes. It prints the 
        /// name of the node and indicates that it has been reached, along with information about checking against the left memory. This 
        /// method can be used to trace the flow of facts and tokens through the Rete network during development and troubleshooting, 
        /// providing insight into how the JoinNode is processing incoming data and interacting with its inputs and successors.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            // Joins are "Right Activations" in this context
            Console.WriteLine($"{indent}[JoinNode:{_nextName}] Reached - Checking against Left Memory...");
        }
    }

}
