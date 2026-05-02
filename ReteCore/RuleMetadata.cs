using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    /// <summary>
    /// The RuleMetadata class encapsulates all the necessary information about a rule in a Rete-based rule engine.
    /// </summary>
    public class RuleMetadata
    {
        /// <summary>
        /// The index of the rule in the Rete network. This index is assigned when the rule is added to the engine.
        /// </summary>
        public int RuleIndex { get; init; }
        /// <summary>
        /// The name of the rule. This is used for identification and debugging purposes, allowing users to trace 
        /// which rule is being activated when an activation is fired. This is a required field.
        /// </summary>
        public required string Name { get; init; }
        /// <summary>
        /// The action delegate that defines the operation to be performed when this rule is activated.
        /// The action takes a Token as a parameter, which contains the facts that triggered the activation.
        /// This is a required field. 
        /// </summary>
        public required Action<Token> Action { get; init; }
        /// <summary>
        /// The agenda to which activations of this rule should be added when the rule is activated. This allows for 
        /// flexibility in managing activations, as different rules can be directed to different agendas if needed. 
        /// This is a required field.
        /// </summary>
        public required Agenda Agenda { get; init; }
        /// <summary>
        /// A list of global guard functions that are evaluated before adding an activation to the agenda. If any 
        /// global guard returns false, the activation will not be added, ensuring that certain conditions are met 
        /// before the rule is executed.
        /// </summary>
        public List<Func<bool>> GlobalGuards { get; init; } = new();
        /// <summary>
        /// A list of late filters that are evaluated at the time of activation, just before adding the activation to 
        /// the agenda. Each late filter checks a specific fact in the token against a predicate function. If any late 
        /// filter returns false, the activation will not be added to the agenda, allowing for additional conditions 
        /// to be enforced at the time of rule firing based on the current state of the facts in the token.
        /// </summary>
        public List<LateFilter> LateFilters { get; init; } = new();
        /// <summary>
        /// An integer representing the rule level priority of activations created by this terminal node. Higher priority 
        /// values indicate that the rule will fire before lower priority ones when multiple activations are pending in 
        /// the agenda. This allows for control over the execution order of rules, when it is important to ensure that 
        /// certain rules are executed before others. A default value of 0 means that if priority is not specified, it will 
        /// not affect the execution order of activations relative to those with explicitly set priority values.
        /// </summary>
        public int Priority { get; init; } = 0;
        /// <summary>
        /// An integer representing the condition level priority or salience of activations created by this terminal node. 
        /// Higher salience values indicate higher priority for execution when multiple activations are pending in the agenda. 
        /// This allows for fine-grained control over the order in which rule conditions are fired when multiple facts are 
        /// asserted that match the same rule, ensuring that more important conditions are evaluated before less important ones. 
        /// The default value is 0, which means that if salience is not specified, it will not affect the execution order of 
        /// activations relative to those with explicitly set salience values.
        /// </summary>
        public int Salience { get; init; } = 0;
    }

    /// <summary>
    /// The LateFilter class represents a condition that is evaluated at the time of activation, 
    /// just before adding the activation to the agenda.
    /// </summary>
    public class LateFilter
    {
        /// <summary>
        /// The rule alias for the fact to be evaluated by this late filter. This alias corresponds 
        /// to the name used in the rule definition to refer to a specific fact.
        /// </summary>
        public string Alias { get; set; }
        /// <summary>
        /// The predicate function that defines the condition to be evaluated for the fact associated 
        /// with this late filter.
        /// </summary>
        public Func<object, bool> Predicate { get; set; }
    }

}
