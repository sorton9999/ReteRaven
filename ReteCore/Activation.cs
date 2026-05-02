//-----------------------------------------------------------------------
// <copyright file="Activation.cs">
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
    /// The Activation class represents a pending rule activation in a Rete-based rule engine. It encapsulates the information needed to 
    /// execute a rule when its conditions are met, including the name of the rule, the action to perform, the token that triggered the 
    /// activation, and the salience (priority) of the activation. Activations are typically created by terminal nodes in the Rete network 
    /// and added to an agenda for later execution. When an activation is fired, it executes its associated action using the token that 
    /// caused it to be activated. 
    /// The salience value allows for prioritizing activations when multiple rules are activated simultaneously, with higher salience 
    /// values being executed first.
    /// </summary>
    public class Activation : IComparable<Activation>
    {
        /// <summary>
        /// The name of the rule associated with this activation. This is used for identification and debugging purposes, 
        /// allowing users to trace which rule is being activated when an activation is fired. The rule name can help 
        /// in understanding the flow of rule execution and diagnosing issues in the rule engine by providing context 
        /// about which rules are being triggered based on the matched facts in the token.
        /// </summary>
        public string RuleName { get; }
        /// <summary>
        /// An action delegate that defines the operation to be performed when this activation is fired. The action takes 
        /// a Token as a parameter, which contains the facts that triggered the activation. When the Fire method is called, 
        /// this action will be executed with the associated token, allowing it to perform the necessary operations defined 
        /// by the rule, such as modifying facts, asserting new facts, or interacting with external systems based on the 
        /// matched conditions.
        /// </summary>
        public Action<Token> Action { get; }
        /// <summary>
        /// The token that triggered this activation. This token contains the facts that matched the conditions of the rule 
        /// associated with this activation. When the activation is fired, this token will be passed to the action delegate, 
        /// allowing it to access the relevant facts and perform the necessary operations defined by the rule. The token 
        /// serves as a context for the activation, providing the information needed to execute the rule's action effectively.
        /// </summary>
        public Token Match { get; }
        /// <summary>
        /// A numeric value representing the priority of this activation relative to others in the agenda. Higher salience 
        /// values indicate higher priority, meaning that activations with higher salience will be fired before those with 
        /// lower salience when multiple activations are pending in the agenda. This allows for fine-grained control over 
        /// the order of rule execution, ensuring that more important rules are executed first when there are competing 
        /// activations.
        /// </summary>
        public int Salience { get; }
        /// <summary>
        /// A numeric value representing the priority of this activation relative to others in the agenda. Higher priority 
        /// values indicate that the rule will fire before lower priority ones when multiple activations are pending in the 
        /// agenda. This allows for additional control over the execution order of rules, especially when salience values 
        /// are equal or when further differentiation is needed beyond salience alone.
        /// </summary>
        public int Priority { get; }
        /// <summary>
        /// A timestamp indicating when this activation was created. This can be used to break ties when multiple activations 
        /// have the same salience and priority, ensuring a consistent and predictable firing order based on the order of 
        /// creation.
        /// </summary>
        public long Timestamp { get; } = DateTime.UtcNow.Ticks;
        /// <summary>
        /// A private field to store the timestamp. This is so the timestamp can be set at the time of instantiation.
        /// </summary>
        private long _timestamp = 0;

        /// <summary>
        /// Creates a new Activation instance with the specified rule name, action, token match, and salience. This 
        /// constructor initializes the properties of the activation, allowing it to be added to an agenda for later 
        /// execution when the conditions of the associated rule are satisfied. The rule name is used for identification 
        /// and debugging purposes, while the action defines what will be executed when the activation fires. The token 
        /// match contains the facts that triggered the activation, and the salience determines the priority of this 
        /// activation relative to others in the agenda.
        /// </summary>
        /// <param name="name">The name of the activation.  Usually the rule name.</param>
        /// <param name="action">The action that executes when this activation fires.</param>
        /// <param name="match">The triggering Token containing the fact on which to act upon.</param>
        /// <param name="salience">A priority value relative to other activations.</param>
        public Activation(string name, Action<Token> action, Token match, int priority, int salience)
        {
            // Mark the timestamp on this object's instantiation which will be used for tie-breaking when mutiple
            // activations have the same Priority and Salience.
            _timestamp = Timestamp;
            RuleName = name;
            Action = action;
            Match = match;
            Salience = salience;
            Priority = priority;
        }

        /// <summary>
        /// Fires this activation by executing its associated action with the token that triggered it. This method is 
        /// typically called by the agenda when processing pending activations, allowing the rule's action to be 
        /// executed based on the matched facts contained in the token. The action can perform any operations defined 
        /// by the rule, such as modifying facts, asserting new facts, or interacting with external systems, using the 
        /// information available in the token.
        /// </summary>
        public void Fire() => Action(Match);

        /// <summary>
        /// A comparison method that allows activations to be ordered based on their priority, salience, and timestamp. 
        /// This is used by the agenda to sort activations when multiple activations are pending, ensuring that the most 
        /// important or the most desired activations fire before lesser ones.
        /// </summary>
        /// <param name="other">The Activation object to compare to.</param>
        /// <returns>An integer indicating the relative order of the activations.</returns>
        public int CompareTo(Activation? other)
        {
            // Compare Priority (Primary)
            int priorityCompare = other?.Priority.CompareTo(this.Priority) ?? 0;
            if (priorityCompare != 0) return priorityCompare;

            // Priority is equal, compare Salience (Secondary tie-breaker)
            int salienceCompare = other?.Salience.CompareTo(this.Salience) ?? 0;
            if (salienceCompare != 0) return salienceCompare;

            // Priority and Salience are equal, compare Timestamp in a LIFO manner (Tertiary tie-breaker)
            return other?.Timestamp.CompareTo(this.Timestamp) ?? 0;
        }
    }
}
