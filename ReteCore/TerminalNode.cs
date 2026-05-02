//-----------------------------------------------------------------------
// <copyright file="TerminalNode.cs">
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
    /// Represents a terminal node in a Rete network that schedules rule activations when matching facts are asserted.
    /// </summary>
    /// <remarks>A TerminalNode is the endpoint of a Rete rule network. When a fact matching the rule
    /// conditions reaches this node, it creates an activation and adds it to the agenda for later execution.
    /// TerminalNode instances are typically associated with a specific rule and action. No successor nodes should be
    /// added after a TerminalNode.</remarks>
    public class TerminalNode : IReteNode
    {
        /// <summary>
        /// The name of the rule associated with this terminal node. This is used for identification and debugging purposes when activations are 
        /// created and added to the agenda.
        /// This is part of the rule metadata.
        /// </summary>
        private readonly string _ruleName;
        /// <summary>
        /// The action to execute when an activation is fired for this terminal node. This action will be called with the token that triggered the activation, 
        /// allowing it to access the matched facts and perform the desired operations when the rule fires.
        /// This is part of the rule metadata.
        /// </summary>
        private readonly Action<Token> _action;
        /// <summary>
        /// The agenda to which activations created by this terminal node will be added. The agenda is responsible for managing the execution of activations 
        /// based on their salience and other factors.
        /// </summary>
        private readonly Agenda _agenda;
        /// <summary>
        /// An integer representing the rule level priority of activations created by this terminal node. Higher priority values indicate that the rule will 
        /// fire before lower priority ones when multiple activations are pending.
        /// This is part of the rule metadata.
        /// This is a part of the rule metadata. 
        /// </summary>
        private readonly int _priority;
        /// <summary>
        /// An integer representing the condition level priority or salience of activations created by this terminal node. Higher salience values indicate higher 
        /// priority for execution when multiple activations are pending in the agenda. 
        /// This is a part of the rule metadata.
        /// </summary>
        private readonly int _salience;
        /// <summary>
        /// A list of global guard functions that are evaluated before adding an activation to the agenda. If any global guard returns false, the activation will 
        /// not be added.  
        /// This is a part of the rule metadata.
        /// </summary>
        private readonly List<Func<bool>> _globalGuards;
        /// <summary>
        /// A list of late filters that are evaluated at the time of activation, just before adding the activation to the agenda. Each late filter checks a specific 
        /// fact in the token against a predicate function. If any late filter returns false, the activation will not be added to the agenda. 
        /// This is a part of the rule metadata.
        /// </summary>
        private readonly List<LateFilter> _lateFilters;
        /// <summary>
        /// A set of hash codes for tokens that have already triggered activations from this terminal node. This is used to prevent duplicate activations for the 
        /// same token, ensuring that each unique combination of facts only results in one activation being added to the agenda. When a token is retracted, 
        /// its hash code is removed from this set, allowing for new activations if the same facts are asserted again in the future.
        /// </summary>
        private readonly HashSet<int> _firedTokens = new();

        /// <summary>
        /// Initializes a new instance of the TerminalNode class with the specified rule metadata.
        /// </summary>
        /// <param name="metaData">The metadata for the rule associated with this terminal node. Cannot be null.</param>
        public TerminalNode(RuleMetadata metaData)
        {
            _ruleName = metaData.Name;
            _action = metaData.Action;
            _agenda = metaData.Agenda;
            _globalGuards = metaData.GlobalGuards;
            _lateFilters = metaData.LateFilters;
            _priority = metaData.Priority;
            _salience = metaData.Salience;
        }

        /// <summary>
        /// Prevents adding a successor node to this terminal node.
        /// </summary>
        /// <remarks>Terminal nodes do not support successors. Calling this method has no effect other
        /// than logging a message.</remarks>
        /// <param name="node">The node that would be added as a successor. This parameter is ignored.</param>
        public void AddSuccessor(IReteNode node) { Console.WriteLine("[TerminalNode]: Nothing should come after this node."); }

        /// <summary>
        /// Asserts a fact into the rule engine, creating an activation if the fact has not already been processed.  Two other
        /// checks are performed before adding the activation to the agenda:  First, any global guard conditions are evaluated.
        /// If any global guard returns false, the activation is not loaded.  Second, any late filters are evaulated.  If any
        /// late filter returns false, the activation is not loaded.  If both checks pass and the fact has not previously
        /// triggered, a new activation is created and added to the agenda.  If the specified fact is a Token that has not 
        /// previously triggered an activation for this rule, an activation is added to the agenda. Duplicate assertions of the
        /// same Token are ignored.        
        /// /// </summary>
        /// <param name="fact">The fact to assert. Must be a non-null object of type Token to be considered for activation.</param>
        public void Assert(object fact)
        {
            if (fact is Token finalMatch)
            {
                // Check if this Token has already been triggered.  If not, continue with checks
                // before adding the activation.
                if (!_firedTokens.Contains(finalMatch.GetHashCode()))
                {
                    // Check any global state guards first (if any)
                    if (!_globalGuards.All(guard => guard())) return;

                    // Check the late filters before adding the activation to the agenda
                    bool lateFiltersPass = _lateFilters.All(filter =>
                    {
                        var factToCheck = finalMatch.Get<object>(filter.Alias);
                        if (factToCheck == null) return false;
                        return filter.Predicate(factToCheck);
                    });
                    // All checks pass. Add the activation to the Agenda and mark as triggered.
                    if (lateFiltersPass)
                    {
                        _agenda.Add(new Activation(_ruleName, _action, finalMatch, _priority, _salience));
                        Console.WriteLine($"[ASSERT] Activation for rule '{_ruleName}' added to agenda.");
                        _firedTokens.Add(finalMatch.GetHashCode());
                    }
                }
            }
        }

        /// <summary>
        /// Retracts the specified fact from the rule engine, removing any associated activations or tokens.
        /// </summary>
        /// <remarks>If the specified fact is currently part of any pending activations or has previously
        /// fired tokens, those will be removed from the agenda and internal state. This operation cancels any pending
        /// rule activations related to the fact.</remarks>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            if (fact is Token token)
            {
                _firedTokens.Remove(token.GetHashCode());
            }
            // Find and remove any activations in the agenda that contain this fact
            _agenda.RemoveByFact(fact);
            Console.WriteLine($"[RETRACT] Pending activation for rule '{_ruleName}' cancelled.");
        }

        /// <summary>
        /// Updates the specified fact in the working memory, re-evaluating any rules that depend on the given property.
        /// </summary>
        /// <remarks>Call this method after modifying a property of a fact to ensure that the rule engine
        /// reconsiders any rules affected by the change. This method removes and re-inserts the fact, triggering rule
        /// re-evaluation as appropriate.</remarks>
        /// <param name="fact">The fact object to refresh in the working memory. Cannot be null.</param>
        /// <param name="propertyName">The name of the property on the fact that has changed. Cannot be null or empty.</param>
        public void Refresh(object fact, string propertyName)
        {
            // Remove existing activations for this fact from the Agenda
            Retract(fact);
            Assert(fact);
        }
        /// <summary>
        /// Writes a formatted debug message to the console that includes the rule name and the specified fact, indented
        /// according to the given level.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. This object is converted to a string using its ToString
        /// method.</param>
        /// <param name="level">The indentation level for the output. Each level increases the indentation by two spaces. The default is 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            Console.WriteLine($"{indent}TerminalNode:{_ruleName}:{fact}");
        }
    }
}
