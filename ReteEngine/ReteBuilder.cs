//-----------------------------------------------------------------------
// <copyright file="ReteBuilder.cs">
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
using ReteCore;

namespace ReteEngine
{
    /// <summary>
    /// Provides a fluent interface for constructing and configuring rules in a Rete-based rule engine.
    /// </summary>
    /// <remarks>Use the RuleBuilder to define the sequence of conditions and actions that make up a rule. The
    /// builder supports chaining methods to specify matching patterns, logical combinations (AND/OR), and actions to
    /// execute when the rule is triggered. Each method call adds a new node or condition to the rule's execution graph.
    /// The builder is not thread-safe and should be used from a single thread during rule construction.</remarks>
    /// <typeparam name="TInitial">The type of the initial fact or object that the rule operates on.</typeparam>
    public class ReteBuilder<TInitial>
    {
        /// <summary>
        /// A constant value representing the highest priority level for rules. Setting a rule's priority to this value 
        /// ensures that it will be executed before any rules with lower priority when multiple rules are eligible to fire.
        /// </summary>
        public const int FIRST_RULE_PRIORITY_VALUE = 10000;
        /// <summary>
        /// The ReteEngine instance that this builder will configure. 
        /// The builder will add nodes and conditions to the engine's 
        /// network as the rule is constructed.
        /// </summary>
        private readonly ReteEngine _engine;
        /// <summary>
        /// The name of the rule being built. This is used for identification in the 
        /// engine and for debugging purposes.
        /// </summary>
        private readonly string _ruleName;
        /// <summary>
        /// The last node in the current rule's chain of conditions. This is used to keep track of 
        /// where to add the next condition or action.
        /// </summary>
        private IReteNode? _lastNode;
        /// <summary>
        /// This is the current rule levelpriority. It is used to determine the order of rule 
        /// execution when multiple rules are eligible to fire.
        /// </summary>
        private int _priority = 0;
        /// <summary>
        /// This list holds any global conditions that should be evaluated at the time the rule fires. 
        /// These conditions are not associated with any specific fact or token but are general predicates 
        /// that must be satisfied for the rule to execute. They are evaluated in the terminal node before 
        /// the action is executed, allowing for additional checks that may depend on external state or 
        /// context at the time of firing.
        /// </summary>
        private readonly List<Func<bool>> _globalConditions = new();
        /// <summary>
        /// This list holds any late filters that should be applied at the time the rule fires. Late filters 
        /// are conditions that are evaluated in the terminal node, similar to global conditions, but they 
        /// can reference specific facts or tokens using aliases defined in the rule. Each late filter consists 
        /// of an alias and a predicate function that takes an object (the fact) and returns a boolean 
        /// indicating whether the condition is satisfied. 
        /// Late filters allow for more complex conditions that depend on the matched facts at the time of 
        /// firing, providing additional flexibility in defining rule consequences based on the specific data 
        /// that triggered the rule.
        /// </summary>
        private readonly List<LateFilter> _lateFilters = new();

        /// <summary>
        /// Initializes a new instance of the RuleBuilder class with the specified Rete engine and rule name.
        /// </summary>
        /// <param name="engine">The ReteEngine instance that will be used to build and manage the rule. Cannot be null.</param>
        /// <param name="name">The name to assign to the rule being built. Cannot be null or empty.</param>
        public ReteBuilder(ReteEngine engine, string name)
        {
            _engine = engine;
            _ruleName = name;
        }

        /// <summary>
        /// Gets the name of the rule associated with this instance.
        /// </summary>
        public string RuleName { get { return _ruleName; } }

        /// <summary>
        /// Adds a typed fact with an optional condition to the rule being built.  This method creates an AlphaMemory node for 
        /// the specified type T and connects it to the current rule chain. If an initial condition is provided, it will be 
        /// used to filter facts of type T as they are asserted into the AlphaMemory. The name parameter is used to identify 
        /// this match condition within the rule, allowing it to be referenced in subsequent conditions or actions. If a debug 
        /// label is provided, diagnostic output will be written to the console when the type is added.
        /// </summary>
        /// <remarks>Use this method to specify that the rule should match facts of type T. Multiple calls to Where can be 
        /// chained to build more complex rules. The name parameter is used to reference this type in subsequent rule 
        /// conditions or actions.</remarks>
        /// <typeparam name="T">The type of fact to match in the rule.</typeparam>
        /// <param name="name">The name used to identify this match condition within the rule. Cannot be null or empty.</param>
        /// <param name="debugLabel">An optional label used for debugging purposes. If specified, diagnostic output will be 
        /// written when the type is added.</param>
        /// <param name="initialCondition">A function that defines an optional initial condition to filter facts of type T as 
        /// they are asserted into the AlphaMemory. The rule stops at this point if this initial condition is not satisfied.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, enabling further configuration of the rule.</returns>
        public ReteBuilder<TInitial> Where<T>(string name, string? debugLabel = null, Func<T, bool> initialCondition = null)
        {
            if (debugLabel != null)
            {
                Console.WriteLine($"===> Start {name}");
            }
            var alpha = _engine.GetAlphaMemory<T>(name, initialCondition);
            var beta = new BetaMemory();
            var adapter = new AlphaToBetaAdapter(beta, name);

            alpha.AddSuccessor(adapter);
            _lastNode = beta;
            return this;
        }

        /// <summary>
        /// The rule level priority determines the order in which rules are executed when multiple rules are eligible to fire. 
        /// Higher priority values indicate that a rule should be executed before those with lower priority values. By default, 
        /// the priority is set to 0, and rules with the same priority will be executed in the order they were added to the 
        /// agenda. Use this method to explicitly set the priority of a rule, allowing you to control the execution order when 
        /// there are competing rules that could fire at the same time. This is particularly useful in scenarios where certain 
        /// rules must take precedence over others based on business logic or specific conditions.
        /// </summary>
        /// <param name="level">The priority level to assign to the rule.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, enabling further configuration of the rule.</returns>
        public ReteBuilder<TInitial> Priority(int level)
        {
            _priority = level;
            return this;
        }

        /// <summary>
        /// A convenience method to set the rule's priority to a high value, ensuring that it will be executed before any rules 
        /// with lower priority when multiple rules are eligible to fire.
        /// </summary>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, enabling further configuration of the rule.</returns>
        public ReteBuilder<TInitial> First()
        {
            _priority = FIRST_RULE_PRIORITY_VALUE;
            return this;
        }

        /// <summary>
        /// A convenience method to decrement the rule's priority by 1, allowing it to be executed after any rules with higher priority.
        /// </summary>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, enabling further configuration of the rule.</returns>
        public ReteBuilder<TInitial> Next()
        {
            _priority -= 1;
            return this;
        }

        /// <summary>
        /// This is in case you want to insert a rule between two other rules without needing to change the priority of
        /// all the existing rules. You can specify a seed value to decrement the priority by, allowing you to create 
        /// gaps in the priority values for future insertions.
        /// </summary>
        /// <param name="seedValue">The seed value used to calculate the next priority level.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing for method chaining.</returns>
        public ReteBuilder<TInitial> Next(int seedValue)
        {
            _priority = seedValue - 1;
            return this;
        }

        /// <summary>
        /// Adds a global condition to the rule, which is evaluated at the time the rule fires. Global conditions are not 
        /// associated with any specific fact or token but are general predicates that must be satisfied for the rule to 
        /// execute. They are evaluated in the terminal node before the action is executed, allowing for additional checks 
        /// that may depend on external state or context at the time of firing. Use this method to specify conditions that 
        /// should be checked when the rule is triggered, regardless of the specific facts that matched the rule's pattern.
        /// </summary>
        /// <param name="name">The name used to identify the global condition within the rule network.</param>
        /// <param name="globalCondition">A function that determines whether the global condition is satisfied. Returns 
        /// <see langword="true"/> if the condition is met; otherwise, <see langword="false"/>.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing for method chaining.</returns>
        public ReteBuilder<TInitial> If(string name, Func<bool> globalCondition)
        {
            _globalConditions.Add(globalCondition);
            return this;
        }

        /// <summary>
        /// Adds a late filter condition to the rule, which is evaluated in the terminal node at the time the rule fires. 
        /// Late filters are conditions that can reference specific facts or tokens using aliases defined in the rule. Each 
        /// late filter consists of an alias and a predicate function that takes an object (the fact) and returns a boolean 
        /// indicating whether the condition is satisfied. Late filters allow for more complex conditions that depend on the 
        /// matched facts at the time of firing, providing additional flexibility in defining rule consequences based on the 
        /// specific data that triggered the rule. Use this method to specify conditions that should be checked against 
        /// specific facts when the rule is triggered, allowing for dynamic checks based on the matched data. The name 
        /// parameter serves as an alias for the fact being evaluated, which can be referenced in the predicate function.
        /// </summary>
        /// <typeparam name="T">The type of the fact being evaluated.</typeparam>
        /// <param name="name">The alias for the fact being evaluated.</param>
        /// <param name="lateCondition">A function that determines whether the late condition is satisfied. Returns 
        /// <see langword="true"/> if the condition is met; otherwise, <see langword="false"/>.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing for method chaining.</returns>
        /// <exception cref="InvalidCastException"></exception>
        public ReteBuilder<TInitial> If<T>(string name, Func<T, bool> lateCondition)
        {
            // We need to wrap the Func<T, bool> into a Func<object, bool> for storage
            // in TerminalNode's LateFilter list
            Func <object, bool> wrappedPred = (fact) =>
            {
                if (fact is T typedFact)
                {
                    return lateCondition(typedFact);
                }
                // If the fact is not of type T, it does not satisfy the condition
                throw new InvalidCastException(
                    $"Rule '{_ruleName}': Alias '{name}' expected type {typeof(T).Name} " +
                    $"but found {fact.GetType().Name}.");
            };
            _lateFilters.Add(new LateFilter
            {
                Alias = name,
                Predicate = wrappedPred,
            });
            return this;
        }

        /// <summary>
        /// Adds a join condition to the rule, requiring that both the previous conditions and the specified join
        /// condition are satisfied for a fact to match.
        /// </summary>
        /// <remarks>Use this method to combine multiple conditions in a rule using logical AND semantics.
        /// The join condition is evaluated for each fact of type T, and only facts that satisfy the condition are
        /// considered matches. If a debug label is provided, diagnostic output is written to the console each time the
        /// join condition is evaluated.</remarks>
        /// <typeparam name="T">The type of fact to join with the current rule conditions.</typeparam>
        /// <param name="name">The name used to identify the join node within the rule network.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the 
        /// current token. Returns <see langword="true"/> if the fact matches the join condition; otherwise, 
        /// <see langword="false"/>.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the 
        /// join evaluation is written to the console.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing for method chaining.</returns>
        public ReteBuilder<TInitial> And<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {
            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return result;
            };
            var alpha = _engine.GetAlphaMemory<T>(name);
            JoinNode join = new JoinNode(_lastNode, alpha, name, (token, fact) => wrapCondition(token, (T)fact));
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(join);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(join);
            }

            var betaMemory = new BetaMemory();
            join.AddSuccessor(betaMemory);

            _lastNode = betaMemory;

            return this;
        }

        /// <summary>
        /// Adds a logical OR branch to the rule, allowing the rule to match if any of the specified conditions are
        /// satisfied for facts of the given type.
        /// </summary>
        /// <remarks>Each condition in <paramref name="orConditions"/> is evaluated independently against
        /// facts of type <typeparamref name="T"/>. The rule continues if at least one condition is met. This method
        /// enables branching logic within a rule, similar to a logical OR operation.</remarks>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the 
        /// evaluation of each OR condition is written to the console.</param>
        /// <param name="orConditions">One or more predicate functions that define the alternative conditions. The rule matches 
        /// if any of these predicates return <see langword="true"/> for a given fact.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public ReteBuilder<TInitial> Or<T>(string name, string? debugLabel = null, params Func<Token, T, bool>[] orConditions)
        {
            // Save the starting point so all branches begin from the same prefix
            var branchStartNode = _lastNode; // Previous node in the chain

            // The collector node that merges all paths
            var orNode = new CompositeBetaMemory();

            foreach (var condition in orConditions)
            {
                var wrapCondition = (Token token, T fact) =>
                {
                    bool result = condition(token, fact);
                    if (debugLabel != null)
                    {
                        Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                    }
                    return result;
                };

                var alpha = _engine.GetAlphaMemory<T>();

                // Create a JoinNode for this specific condition
                var join = new JoinNode(branchStartNode, alpha, name,
                    (token, fact) => wrapCondition(token, (T)fact));

                if (branchStartNode is BetaMemory beta)
                {
                    beta.AddSuccessor(join);
                }
                else if (branchStartNode is CompositeBetaMemory compositeBeta)
                {
                    compositeBeta.AddSuccessor(join);
                }

                alpha.AddSuccessor(join);

                // Point this branch to the OrNode
                join.AddSuccessor(orNode);
                //_lastNode = orNode;
            }
            // Update the builder state: the rest of the rule now follows the orNode
            _lastNode = orNode;

            return this;
        }

        /// <summary>
        /// Adds a negated join condition to the rule, specifying that the rule should only match if there are no facts of 
        /// type T that satisfy the given join condition.
        /// </summary>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the 
        /// current token.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the 
        /// evaluation of the condition is written to the console.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public ReteBuilder<TInitial> AndNot<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {
            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return !result; // Negate the condition for AND NOT semantics
            };
            var alpha = _engine.GetAlphaMemory<T>();
            JoinNode join = new JoinNode(_lastNode, alpha, name, (token, fact) => wrapCondition(token, (T)fact));
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(join);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(join);
            }
            var betaMemory = new BetaMemory();
            join.AddSuccessor(betaMemory);
            _lastNode = betaMemory;
            return this;
        }

        /// <summary>
        /// Adds a "not" condition to the rule, specifying that there must be no facts of type T that satisfy the given 
        /// join condition for the rule to match.
        /// </summary>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the 
        /// current token.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the 
        /// evaluation of the condition is written to the console.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public ReteBuilder<TInitial> Not<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {
            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return result;
            };
            var alpha = _engine.GetAlphaMemory<T>();
            var notNode = new NotNode(name, (token, fact) => wrapCondition(token, (T)fact));
            alpha.AddSuccessor(notNode);

            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(notNode);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(notNode);
            }
            var betaMemory = new BetaMemory();
            notNode.AddSuccessor(betaMemory);
            _lastNode = betaMemory;
            return this;
        }

        /// <summary>
        /// Adds an "exists" condition to the rule, specifying that there must be at least one fact of type T that satisfies
        /// the given join condition for the rule to match.
        /// </summary>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the 
        /// current token.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the 
        /// evaluation of the condition is written to the console.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public ReteBuilder<TInitial> Exists<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {

            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return result;
            };

            var alphaMem = _engine.GetAlphaMemory<T>();

            JoinKeyExtractor joinKeyExtractor = new JoinKeyExtractor();
            ExistsNode existsNode = null;
            IndexedExistsNode indexedExistsNode = null;
            try
            {
                var (leftKey, rightKey) = joinKeyExtractor.Extract((Token t, object f) => wrapCondition(t, (T)f));
                indexedExistsNode = new IndexedExistsNode(name, leftKey, rightKey);
            }
            catch
            {
                existsNode = new ExistsNode(name, (token, fact) => wrapCondition(token, (T)fact));
            }
            alphaMem.AddSuccessor(existsNode != null ? existsNode : indexedExistsNode);

            //var existsNode = new ExistsNode((token, fact) => wrapCondition(token, fact));
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(existsNode != null ? existsNode : indexedExistsNode);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(existsNode != null ? existsNode : indexedExistsNode);
            }
            var betaMemory = new BetaMemory();
            //existsNode.AddSuccessor(betaMemory);
            if (existsNode != null)
            {
                existsNode.AddSuccessor(betaMemory);
            }
            else if (indexedExistsNode != null)
            {
                indexedExistsNode.AddSuccessor(betaMemory);
            }
            _lastNode = betaMemory;
            return this;
        }

        /// <summary>
        /// This method provides a way to aggregate facts of a specific type T into the token under a given alias. The optional 
        /// join condition allows you to specify criteria for which facts of type T should be included in the aggregation. If the join 
        /// condition is null or not provided, all facts of type T will be included in the aggregation. The aggregated facts are stored 
        /// in the token under the specified alias, which can then be referenced in subsequent conditions or actions using that alias. 
        /// This method is useful for scenarios where you want to collect related facts together and evaluate conditions based on the 
        /// aggregated collection at the time the rule fires.  
        /// 
        /// NOTE: This method is especially useful when combined with the All<T> method, which allows you to define conditions on the 
        /// aggregated collection of facts.  They are tied together by the given alias, which serves as the key for storing the 
        /// collection of facts in the token and referencing it in the aggregate condition.
        /// </summary>
        /// <typeparam name="T">Type of facts to aggregate.</typeparam>
        /// <param name="alias">The alpha memory name AND alias to store the collection under on the token.</param>
        /// <param name="joinCondition">Optional join constraint per token/fact. If null, every fact is included.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the 
        /// evaluation of the condition is written to the console.</param>
        public ReteBuilder<TInitial> From<T>(string alias, Func<Token, T, bool> joinCondition = null, string? debugLabel = null)
        {
            Func<Token, object, bool> wrapped = (token, fact) =>
            {
                if (joinCondition == null) return true;
                bool result = joinCondition(token, (T)fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return result;
            };

            var alpha = _engine.GetAlphaMemory<T>(alias);
            var allNode = new AllNode(alias, wrapped);

            // connect alpha to allNode
            alpha.AddSuccessor(allNode);

            // connect previous chain to allNode
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(allNode);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(allNode);
            }

            // add a beta memory after the aggregator
            var betaMemory = new BetaMemory();
            allNode.AddSuccessor(betaMemory);
            _lastNode = betaMemory;

            return this;
        }

        /// <summary>
        /// Allows you to define an aggregate condition on a collection of facts of type T that have been aggregated into the 
        /// token given the specified alias. The provided predicate will be evaluated against the collection of facts at the 
        /// time the rule fires, allowing you to specify conditions that depend on the aggregated data. 
        /// You could use this method to check if all facts in the collection satisfy a certain condition, if any fact meets a 
        /// criterion, or if the collection contains a specific number of items. 
        /// 
        /// NOTE: The alias parameter should match the name used in a previous From<T> method call that aggregated the facts into 
        /// the token. 
        /// 
        /// The aggregate predicate is a function that takes an IEnumerable<T> (the collection of facts) and returns a boolean 
        /// indicating whether the condition is satisfied. 
        /// </summary>
        /// <typeparam name="T">Element type of the collection.</typeparam>
        /// <param name="alias">Alias under which the collection is stored in the token.</param>
        /// <param name="aggregatePredicate">Predicate evaluated against the collection.</param>

        public ReteBuilder<TInitial> All<T>(string alias, Func<IEnumerable<T>, bool> aggregatePredicate)
        {
            // Create a wrapper predicate that can handle the fact as either an IEnumerable<T> or
            // a non-generic IEnumerable that we attempt to cast
            Func<object, bool> wrappedPred = (fact) =>
            {
                // Strongly typed, call directly
                if (fact is IEnumerable<T> typedSeq) { return aggregatePredicate(typedSeq); }

                // Non-generic IEnumerable, attempt to cast elements to T
                if (fact is System.Collections.IEnumerable ie)
                {
                    var list = new List<T>();
                    foreach (var o in ie)
                    {
                        if (o is T t)
                        {
                            list.Add(t);
                        }
                        else
                        {
                            throw new InvalidCastException(
                                $"Rule '{_ruleName}': Alias '{alias}' expected IEnumerable<{typeof(T).Name}> but contained {o?.GetType().Name ?? "null"}.");
                        }
                    }
                    return aggregatePredicate(list);
                }

                throw new InvalidCastException(
                    $"Rule '{_ruleName}': Alias '{alias}' expected IEnumerable<{typeof(T).Name}> but found {fact?.GetType().Name ?? "null"}.");
            };

            _lateFilters.Add(new LateFilter
            {
                Alias = alias,
                Predicate = wrappedPred
            });

            return this;
        }

        /// <summary>
        /// Adds a terminal action to the rule that will be executed when the rule is triggered.  This method finalizes the rule definition
        /// by specifying the consequence of the rule. The action will be invoked each time the rule's conditions are satisfied. Salience 
        /// can be used to control the order in which rules are executed when multiple rules are eligible to fire. Higher salience
        /// values indicate higher priority, meaning that rules with greater salience will be executed before those with lower salience when 
        /// they are both eligible to fire. The default salience is 0, and rules with the same salience will be executed in the order they 
        /// were added to the agenda. Use this method to specify the action that should be taken when the rule's conditions are met, and 
        /// optionally assign a salience to influence the execution order of the rule relative to others.
        /// </summary>
        /// <remarks>Use this method to specify the consequence of the rule. The action will be invoked
        /// each time the rule's conditions are satisfied. Salience can be used to control the order in which rules are
        /// executed when multiple rules are eligible.</remarks>
        /// <param name="action">The action to execute when the rule fires. The action receives the matched token as its parameter. Cannot be
        /// null.</param>
        /// <param name="salience">The priority of the rule when multiple rules are eligible to fire. Higher values indicate higher priority.
        /// The default is 0.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, enabling further rule configuration.</returns>
        public ReteBuilder<TInitial> Then(Action<Token> action, int salience = 0)
        {
            // Create the terminal node with the specified action and metadata
            var terminal = new TerminalNode(new RuleMetadata
            {
                Name = _ruleName,
                Action = action,
                Agenda = _engine.Agenda,
                GlobalGuards = _globalConditions,
                LateFilters = _lateFilters,
                Priority = _priority,
                Salience = salience
            });
            _engine.AddTerminalNode(terminal);
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(terminal);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(terminal);
            }
            return this;
        }

        /// <summary>
        /// Adds a trace node to the rule builder pipeline with the specified label, enabling inspection of rule
        /// evaluation at this point.
        /// </summary>
        /// <remarks>Use trace nodes to monitor or debug the flow of facts through the rule network.
        /// Tracing can help diagnose rule behavior or performance issues by providing labeled checkpoints in the
        /// evaluation process.</remarks>
        /// <param name="label">The label to associate with the trace node. Used to identify the trace point during rule evaluation.</param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, allowing for method chaining.</returns>
        public ReteBuilder<TInitial> Trace(string label)
        {
            var tracer = new ReteEngine.TraceNode(label);
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(tracer);
            }
            _lastNode = tracer;
            return this;
        }

        /// <summary>
        /// Asserts the specified fact into the current context or knowledge base. This call will add the fact to the last node in the 
        /// rule builder's chain, allowing it to be processed according to the defined rule conditions. The fact must be non-null and 
        /// will be evaluated against the conditions of the rule as it propagates through the network. Use this method to introduce new 
        /// facts into the rule evaluation process, which can trigger rules that match those facts based on their conditions.
        /// </summary>
        /// <param name="fact">The fact to assert. Cannot be null.</param>
        public void Assert(object fact)
        {
            _lastNode?.Assert(fact);
        }

        /// <summary>
        /// Begins the rule definition by specifying the initial AlphaMemory node and the fact name to match against. Use this method 
        /// as the starting point for constructing a rule, and then chain subsequent calls to JoinWith or other builder methods to 
        /// extend the rule's pattern matching.
        /// </summary>
        /// <remarks>Call this method as the first step when constructing a rule. Subsequent calls to
        /// JoinWith or other builder methods will extend the rule from this starting point.</remarks>
        /// <param name="alpha">The AlphaMemory node that serves as the starting point for the rule's pattern matching.</param>
        /// <param name="factName">The name of the fact to be matched by the initial AlphaMemory node. Cannot be null or empty.</param>
        /// <returns>A ReteBuilder<TInitial> instance configured to continue building the rule from the specified AlphaMemory
        /// node.</returns>
        public ReteBuilder<TInitial> StartWith(AlphaMemory alpha, string factName)
        {
            // Create the very first BetaMemory for this rule's chain
            var firstBeta = new BetaMemory();

            // Use the Adapter to convert single facts from Alpha into Tokens for Beta
            var adapter = new AlphaToBetaAdapter(firstBeta, factName);

            // Link the AlphaMemory to the Adapter
            alpha.AddSuccessor(adapter);

            // Update the tracker so the next 'JoinWith' knows where to connect
            _lastNode = firstBeta;

            return this;
        }

        /// <summary>
        /// Adds a join node to the rule network that combines the current beta memory with the specified alpha memory
        /// using the given join condition. Use this to construct more complex rules by specifying how facts from 
        /// different alpha memories should be joined together based on the provided condition. The join condition is 
        /// evaluated for each combination of tokens and facts from the respective memories, allowing for flexible rule 
        /// definitions that can capture complex relationships between different types of facts in the working memory.
        /// </summary>
        /// <remarks>Use this method to extend the rule network by specifying additional join conditions
        /// between working memory elements. The join condition is evaluated for each combination of tokens and facts
        /// from the respective memories.</remarks>
        /// <typeparam name="TNext">The type of the facts stored in the alpha memory to be joined.</typeparam>
        /// <param name="nextAlpha">The alpha memory node to join with the current beta memory. Cannot be null.</param>
        /// <param name="condition">A function that determines whether a token from the beta memory and a fact from the 
        /// alpha memory should be joined. Returns <see langword="true"/> to join the pair; otherwise, <see langword="false"/>.
        /// </param>
        /// <returns>The current <see cref="ReteBuilder{TInitial}"/> instance, enabling method chaining.</returns>
        public ReteBuilder<TInitial> JoinWith<TNext>(ReteCore.AlphaMemory nextAlpha, Func<Token, TNext, bool> condition)
        {
            BetaMemory? beta = _lastNode as BetaMemory;
            var join = new ReteCore.JoinNode(beta!, nextAlpha, "dummy", (t, f) => condition(t, (TNext)f));
            var nextBeta = new ReteCore.BetaMemory();
            join.AddSuccessor(nextBeta);
            _lastNode = nextBeta;
            return this;
        }

        /// <summary>
        /// Defines the terminal action to execute when the rule is triggered, and adds the rule to the specified agenda
        /// with an optional salience. Use this method to finalize the rule definition by specifying the consequence of the 
        /// rule and registering it with the agenda. The action will be invoked each time the rule's conditions are satisfied, 
        /// and salience can be used to control the execution order of the rule relative to others when multiple rules are 
        /// eligible to fire.
        /// </summary>
        /// <remarks>This method finalizes the rule definition by specifying the action to perform when
        /// the rule conditions are met. The rule is then registered with the provided agenda. If multiple rules are
        /// eligible to fire, those with higher salience values are prioritized.</remarks>
        /// <param name="agenda">The agenda to which the rule will be added. Cannot be null.</param>
        /// <param name="action">The action to execute when the rule fires. Receives the token that caused the rule to trigger. 
        /// Cannot be null.</param>
        /// <param name="salience">The priority of the rule within the agenda. Higher values indicate higher priority.
        /// The default is 0.</param>
        public void Then(Agenda agenda, Action<Token> action, int salience = 0)
        {
            var terminal = new TerminalNode(new RuleMetadata 
            { 
                Name = _ruleName, 
                Action = action, 
                Agenda = agenda, 
                GlobalGuards = _globalConditions, 
                LateFilters = _lateFilters, 
                Priority = _priority, 
                Salience = salience 
            });
            _lastNode = terminal;
            _lastNode.Assert(new Token("end", 250));
        }
    }
}


