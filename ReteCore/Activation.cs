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
        /// An enumeration representing the processing state of an activation. This indicates whether the activation is pending, has been 
        /// fired, or has been cancelled. The state of an activation can be used to manage the lifecycle of the activation within the agenda.
        /// </summary>
        public enum ActivationStateEnum
        {
            Cancelled,
            Pending,
            Fired
        }
        /// <summary>
        /// A unique identifier for this activation.
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();
        /// <summary>
        /// The name of the rule associated with this activation. This is used for identification and debugging purposes, 
        /// allowing users to trace which rule is being activated when an activation is fired. The rule name can help 
        /// in understanding the flow of rule execution and diagnosing issues in the rule engine by providing context 
        /// about which rules are being triggered based on the matched facts in the token.
        /// </summary>
        public string RuleName { get; }
        /// <summary>
        /// The group this agenda is assigned to.
        /// </summary>
        public string AgendaGroupName { get; internal set; }
        /// <summary>
        /// An action delegate that defines the operation to be performed when this activation is fired. The action takes 
        /// a Token as a parameter, which contains the facts that triggered the activation. When the Fire method is called, 
        /// this action will be executed with the associated token, allowing it to perform the necessary operations defined 
        /// by the rule, such as modifying facts, asserting new facts, or interacting with external systems based on the 
        /// matched conditions.
        /// </summary>
        public Action<Token> Action { get; }
        /// <summary>
        /// The action delegate that defines the operation to be performed when this activation is fired. The action takes a
        /// RuleExecutionContext as a parameter, which contains the facts that triggered the activation and provides additional
        /// context for executing the rule's action. When the Fire method is called, this action will be executed with the
        /// associated context, allowing it to perform the necessary operations defined by the rule, such as modifying facts,
        /// asserting new facts, or interacting with external systems based on the matched conditions.
        /// </summary>
        public Action<RuleExecutionContext>? ContextAction { get; }
        /// <summary>
        /// The action delegate that defines the operation to be performed when this activation is fired. The action takes a 
        /// IReadOnlyList<FactChange> as a parameter, which contains the fact changes that were published by the rule execution.
        /// When the Fire method is called, this action will be executed with the associated fact changes, allowing it to perform
        /// the necessary operations defined by the rule, such as updating external systems or triggering additional rules based
        /// on the changes.
        /// </summary>
        public Action<IReadOnlyList<FactChange>>? PublishChanges { get; }
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
        /// The current processing state of this activation.
        /// </summary>
        public ActivationStateEnum State { get; set; } = ActivationStateEnum.Pending;
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
        public Activation(
            string name,
            Action<Token> action,
            Token match,
            int priority,
            int salience,
            string agendaGroupName = Agenda.MainAgendaGroup)
        {
            // Mark the timestamp on this object's instantiation which will be used for tie-breaking when mutiple
            // activations have the same Priority and Salience.
            _timestamp = Timestamp;
            RuleName = name;
            AgendaGroupName = ValidateAgendaGroupName(agendaGroupName);
            Action = action;
            Match = match;
            Salience = salience;
            Priority = priority;
        }

        /// <summary>
        /// An alternate constructor for creating an Activation instance that includes a context-aware action and a publish changes action.
        /// </summary>
        /// <param name="name">The name of the activation.  Usually the rule name.</param>
        /// <param name="action">The context-aware action that executes when this activation fires.</param>
        /// <param name="publishChanges">The action that publishes the fact changes produced by the context-aware action.</param>
        /// <param name="match">The triggering Token containing the fact on which to act upon.</param>
        /// <param name="priority">A priority value relative to other activations.</param>
        /// <param name="salience">A priority value relative to other activations.</param>
        public Activation(
            string name,
            Action<RuleExecutionContext> action,
            Action<IReadOnlyList<FactChange>> publishChanges,
            Token match,
            int priority,
            int salience,
            string agendaGroupName = Agenda.MainAgendaGroup)
            : this(name, _ => { }, match, priority, salience, agendaGroupName)
        {
            ContextAction = action;
            PublishChanges = publishChanges;
        }

        /// <summary>
        /// Fires the activation by executing its associated action. If a context-aware action is defined, it creates a
        /// RuleExecutionContext and invokes the context action. After execution, it completes the context and publishes
        /// any fact changes if applicable. If no context action is defined, it simply invokes the standard action with
        /// the triggering token. This method encapsulates the logic for executing the rule's behavior when its conditions
        /// are met.
        /// </summary>
        public void Fire()
        {
            // If no context action is defined, execute the standard action with the triggering token.
            if (ContextAction == null)
            {
                Action(Match);
                return;
            }

            // If a context action is defined, create a RuleExecutionContext and invoke the context action.
            var context = new RuleExecutionContext(Match, RuleName, Id, AgendaGroupName);
            try
            {
                ContextAction(context);
            }
            catch
            {
                // If an exception occurs during the execution of the context action, discard any staged changes and rethrow the exception.
                context.Discard();
                throw;
            }

            // Complete the context and retrieve any fact changes that were staged during the execution of the context action.
            IReadOnlyList<FactChange>? changes = context.Complete();
            if (changes != null)
            {
                PublishChanges?.Invoke(changes);
            }
        }

        /// <summary>
        /// Validate the group name.  Throws an ArgumentException if invalid.
        /// </summary>
        /// <param name="agendaGroupName">The name to validate</param>
        /// <returns>The validated group name</returns>
        /// <exception cref="ArgumentException">Thown when invalid</exception>
        private static string ValidateAgendaGroupName(string agendaGroupName)
        {
            if (string.IsNullOrWhiteSpace(agendaGroupName))
            {
                throw new ArgumentException("An agenda group name cannot be null or whitespace.", nameof(agendaGroupName));
            }
            return agendaGroupName;
        }

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
