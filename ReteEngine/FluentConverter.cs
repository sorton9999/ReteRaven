// -----------------------------------------------------------------------
// <copyright file="FluentConverter.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
// -----------------------------------------------------------------------
using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReteEngine
{
    /*
     * This file contains the FluentConverter class, which provides a fluent interface for defining rules in the Rete engine. 
     * It allows users to create rules using a more natural and readable syntax, chaining method calls to specify conditions 
     * and actions for rules. The FluentConverter class implements several interfaces (IRuleSetup, IEvaluationBuilder, 
     * IConnectionBuilder, IAggregationBuilder) that define the various stages of rule construction, enabling users to build 
     * complex rules with ease. This helps to prompt the designer to think about the structure of their rules in a more 
     * intuitive way and avoid becoming lost in the API, while still leveraging the underlying capabilities of the Rete 
     * engine for efficient pattern matching and rule evaluation.
     */

    /// <summary>
    /// This is the starting point for the fluent interface for defining rules in the Rete engine. It provides methods for 
    /// specifying global conditions, rule priority, and the initial conditions for the rule. 
    /// </summary>
    public interface IRuleSetup
    {
        IRuleSetup If(string name, Func<bool> globalCondition);
        IConnectionBuilder If<T>(string name, Func<T, bool> lateCondition);
        IRuleSetup Priority(int priority);
        IRuleSetup First();
        IRuleSetup Next();
        IRuleSetup Next(int seed);

        IEvaluationBuilder Where<T>(string name, Func<T, bool> initialCondition = null, 
            [CallerArgumentExpression(nameof(initialCondition))] string? debugLabel = null);
        IEvaluationBuilder StartWith<T>(AlphaMemory alpha, string factName);
        IEvaluationBuilder Not<T>(string name, Func<Token, T, bool> joinCondition, 
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);
        IEvaluationBuilder Exists<T>(string name, Func<Token, T, bool> joinCondition,
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);


        IAggregationBuilder From<T>(string? alias = null, Func<Token, T, bool> joinCondition = null, 
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);
    }

    /// <summary>
    /// This interface defines the methods for building connections between conditions in a rule. It allows for chaining multiple conditions 
    /// together using logical AND and OR operators, as well as specifying the actions to be taken when the rule is fired. The And and Or 
    /// methods allow for adding additional conditions to the rule, while the Then method specifies the action to be executed when the rule 
    /// is activated.
    /// </summary>
    public interface IConnectionBuilder : IRuleSetup
    {
        IConnectionBuilder And<T>(string name, Func<Token, T, bool> joinCondition, 
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);
        IConnectionBuilder Or<T>(string name, [CallerArgumentExpression(nameof(orConditions))] string? debugLabel = null, 
            params Func<Token, T, bool>[] orConditions);
        // These are for when you want to chain multiple conditions on the same fact type without needing to repeat
        // the fact type in the method signature
        IConnectionBuilder And();
        IConnectionBuilder Or();

        void Then(Action<Token> action, int salience = 0);
    }

    /// <summary>
    /// This interface defines the methods for building evaluation conditions in a rule. It allows for specifying conditions that operate on individual 
    /// facts, as well as joining conditions that connect multiple facts together.
    /// </summary>
    public interface IEvaluationBuilder
    {
        IEvaluationBuilder Where<T>(string name, Func<T, bool> initialCondition = null, 
            [CallerArgumentExpression(nameof(initialCondition))] string? debugLabel = null);
        IEvaluationBuilder JoinWith<T>(AlphaMemory nextAlpha, Func<Token, T, bool> condition);
        IEvaluationBuilder Exists<T>(string name, Func<Token, T, bool> joinCondition, 
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);
        IEvaluationBuilder Not<T>(string name, Func<Token, T, bool> joinCondition,
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);
        IEvaluationBuilder AndNot<T>(string name, Func<Token, T, bool> joinCondition, 
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);
        IConnectionBuilder And<T>(string name, Func<Token, T, bool> joinCondition, 
            [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null);
        IConnectionBuilder Or<T>(string name, [CallerArgumentExpression(nameof(orConditions))] string? debugLabel = null,
            params Func<Token, T, bool>[] orConditions);
        // These are for when you want to chain multiple conditions on the same fact type without needing to repeat
        // the fact type in the method signature
        IConnectionBuilder And();
        IConnectionBuilder Or();

        void Then(Action<Token> action, int salience = 0);
    }

    /// <summary>
    /// This interface defines the method for building aggregate conditions in a rule. It allows for specifying an aggregate predicate that 
    /// operates on a collection of facts, enabling the creation of rules that depend on the collective state of multiple facts rather than 
    /// just individual facts.
    /// </summary>
    public interface IAggregationBuilder
    {
        IEvaluationBuilder All<T>(Func<IEnumerable<T>, bool> aggregatePredicate);
    }

    /// <summary>
    /// The FluentConverter class provides a fluent interface for defining rules in the Rete engine. It implements the IRuleSetup, 
    /// IEvaluationBuilder, IConnectionBuilder, and IAggregationBuilder interfaces, allowing users to build complex rules using a natural and 
    /// readable syntax. The class internally uses a ReteBuilder to construct the rule based on the method calls made through the fluent 
    /// interface. This class helps to abstract away the complexities of the underlying Rete engine and provides a more intuitive way for 
    /// users to define rules, while still leveraging the capabilities of the engine.
    /// </summary>
    /// <typeparam name="TInitial">The type of the initial fact that the rule operates on.</typeparam>
    public class FluentConverter<TInitial> : IRuleSetup, IEvaluationBuilder, IConnectionBuilder, IAggregationBuilder
    {
        /// <summary>
        /// The ReteBuilder instance that is used internally to construct the rule based on the method calls made through the fluent interface.
        /// </summary>
        private ReteBuilder<TInitial> _builder;
        /// <summary>
        /// The ReteEngine instance that is used to create the ReteBuilder and ultimately build the rule. This is passed in through the constructor.
        /// </summary>
        private ReteEngine _engine;
        /// <summary>
        /// The name of the aggregate being built. This is used to keep track of the current aggregate when building rules that involve aggregation, 
        /// allowing for multiple aggregates to be defined within the same rule without conflicts.
        /// </summary>
        private string aggregateName = "default_agg";

        /// <summary>
        /// The constructor for the FluentConverter class. It takes a ReteEngine instance and a rule name as parameters, which are used to create a 
        /// new ReteBuilder instance.
        /// </summary>
        /// <param name="engine">The ReteEngine instance used to create the ReteBuilder.</param>
        /// <param name="ruleName">The name of the rule being defined.</param>
        public FluentConverter(ReteEngine engine, string ruleName)
        {
            _engine = engine;
            _builder = new ReteBuilder<TInitial>(engine, ruleName);
        }

        /// <summary>
        /// A convenience method that allows for chaining multiple conditions on the same fact type without needing to repeat the fact type in the method 
        /// signature.  This allows for the progression of conditions in a more natural way, where you can start with a condition on a fact type and then 
        /// chain additional conditions on the same fact type using <see cref="And"/> and <see cref="Or"/> without needing to specify the fact type again. 
        /// This helps to keep the rule definition more concise and readable when dealing with multiple conditions on the same fact type.
        /// </summary>
        /// <returns>The current <see cref="IConnectionBuilder"/> (this instance) to continue building the rule.</returns>
        public IConnectionBuilder And()
        {
            return this;
        }

        /// <summary>
        /// A convenience method that allows for chaining multiple conditions on the same fact type without needing to repeat the fact type in the method 
        /// signature for similar reasons as with the <see cref="And"/> call above.
        /// </summary>
        /// <returns>The current <see cref="IConnectionBuilder"/> (this instance) to continue building the rule.</returns>
        public IConnectionBuilder Or()
        {
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.If that registers a global boolean condition.
        /// </summary>
        /// <param name="name">The name to assign to the global condition within the rule.</param>
        /// <param name="globalCondition">A function returning <c>true</c> when the global condition holds.</param>
        /// <returns>The current <see cref="IRuleSetup"/> to continue rule setup.</returns>
        public IRuleSetup If(string name, Func<bool> globalCondition)
        {
            _builder.If(name, globalCondition);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.If that registers a typed late evaluation condition.
        /// </summary>
        /// <typeparam name="T">The fact type the condition evaluates.</typeparam>
        /// <param name="name">The name to assign to the condition within the rule.</param>
        /// <param name="lateCondition">A predicate invoked with a fact of type <typeparamref name="T"/> to evaluate the condition.</param>
        /// <returns>The current <see cref="IConnectionBuilder"/> to continue building connections.</returns>
        public IConnectionBuilder If<T>(string name, Func<T, bool> lateCondition)
        {
            _builder.If(name, lateCondition);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Priority that sets rule priority.
        /// </summary>
        /// <param name="priorityVal">The priority value to assign to the rule.</param>
        /// <returns>The current <see cref="IRuleSetup"/> to continue rule setup.</returns>
        public IRuleSetup Priority(int priorityVal)
        {
            _builder.Priority(priorityVal);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.First that marks the rule as first in ordering.
        /// </summary>
        /// <returns>The current <see cref="IRuleSetup"/> to continue rule setup.</returns>
        public IRuleSetup First()
        {
            _builder.First();
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Next that advances the rule ordering.
        /// </summary>
        /// <returns>The current <see cref="IRuleSetup"/> to continue rule setup.</returns>
        public IRuleSetup Next()
        {
            _builder.Next();
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Next that advances the rule ordering using a seed.
        /// </summary>
        /// <param name="seed">A seed value used when computing the next ordering position.</param>
        /// <returns>The current <see cref="IRuleSetup"/> to continue rule setup.</returns>
        public IRuleSetup Next(int seed)
        {
            _builder.Next(seed);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Where to add an initial evaluation condition on a fact type.
        /// </summary>
        /// <typeparam name="T">The fact type the condition evaluates.</typeparam>
        /// <param name="name">The name to assign to the fact in the rule.</param>
        /// <param name="debugLabel">An optional debug label for diagnostics.</param>
        /// <param name="initialCondition">An optional predicate evaluated against facts of type <typeparamref name="T"/>.</param>
        /// <returns>The current <see cref="IEvaluationBuilder"/> to continue building evaluations.</returns>
        public IEvaluationBuilder Where<T>(string name, Func<T, bool> initialCondition = null ,[CallerArgumentExpression(nameof(initialCondition))] string? debugLabel = null)
        {
            _builder.Where(name, debugLabel, initialCondition);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.StartWith to begin the rule with a provided alpha memory.
        /// </summary>
        /// <typeparam name="T">The fact type stored in the provided alpha memory.</typeparam>
        /// <param name="alpha">The <see cref="AlphaMemory"/> to start the rule with.</param>
        /// <param name="factName">The name to assign to the fact produced by the alpha memory.</param>
        /// <returns>The current <see cref="IEvaluationBuilder"/> to continue building evaluations.</returns>
        public IEvaluationBuilder StartWith<T>(AlphaMemory alpha, string factName)
        {
            _builder.StartWith(alpha, factName);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.JoinWith to join the current token stream with another alpha memory.
        /// </summary>
        /// <typeparam name="T">The fact type in the next alpha memory.</typeparam>
        /// <param name="nextAlpha">The alpha memory to join with.</param>
        /// <param name="condition">A join predicate invoked with the current <see cref="Token"/> and a fact of type <typeparamref name="T"/>.</param>
        /// <returns>The current <see cref="IEvaluationBuilder"/> to continue building evaluations.</returns>
        public IEvaluationBuilder JoinWith<T>(AlphaMemory nextAlpha, Func<Token, T, bool> condition)
        {
            _builder.JoinWith(nextAlpha, condition);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Not to add a negative (not) condition joined to the current token.
        /// </summary>
        /// <typeparam name="T">The fact type to test non-existence for.</typeparam>
        /// <param name="name">The name to assign to the negative test fact within the rule.</param>
        /// <param name="joinCondition">A join predicate invoked with the current <see cref="Token"/> and a fact of type <typeparamref name="T"/>.</param>
        /// <param name="debugLabel">An optional debug label for diagnostics.</param>
        /// <returns>The current <see cref="IEvaluationBuilder"/> to continue building evaluations.</returns>
        public IEvaluationBuilder Not<T>(string name, Func<Token, T, bool> joinCondition, [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null)
        {
            _builder.Not(name, joinCondition, debugLabel);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.AndNot to add an additional negative (and not) condition joined to the current token.
        /// </summary>
        /// <typeparam name="T">The fact type to test non-existence for.</typeparam>
        /// <param name="name">The name to assign to the negative test fact within the rule.</param>
        /// <param name="joinCondition">A join predicate invoked with the current <see cref="Token"/> and a fact of type <typeparamref name="T"/>.</param>
        /// <param name="debugLabel">An optional debug label for diagnostics.</param>
        /// <returns>The current <see cref="IEvaluationBuilder"/> to continue building evaluations.</returns>
        public IEvaluationBuilder AndNot<T>(string name, Func<Token, T, bool> joinCondition, [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null)
        {
            _builder.AndNot(name, joinCondition, debugLabel);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Exists to add an existence test joined to the current token.
        /// </summary>
        /// <typeparam name="T">The fact type to test existence for.</typeparam>
        /// <param name="name">The name to assign to the existence test fact within the rule.</param>
        /// <param name="joinCondition">A join predicate invoked with the current <see cref="Token"/> and a fact of type <typeparamref name="T"/>.</param>
        /// <param name="debugLabel">An optional debug label for diagnostics.</param>
        /// <returns>The current <see cref="IEvaluationBuilder"/> to continue building evaluations.</returns>
        public IEvaluationBuilder Exists<T>(string name, Func<Token, T, bool> joinCondition, [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null)
        {
            _builder.Exists(name, joinCondition, debugLabel);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.From to start building an aggregate from a fact source.
        /// </summary>
        /// <typeparam name="T">The fact type that will be aggregated.</typeparam>
        /// <param name="alias">An optional alias name for the aggregate; if null the current aggregate name is preserved.</param>
        /// <param name="joinCondition">An optional join predicate used to relate tokens to items in the aggregate.</param>
        /// <param name="debugLabel">An optional debug label for diagnostics.</param>
        /// <returns>The current <see cref="IAggregationBuilder"/> to define aggregate predicates.</returns>
        public IAggregationBuilder From<T>(string? alias = null, Func<Token, T, bool> joinCondition = null, [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null)
        {
            aggregateName = alias ?? aggregateName;
            _builder.From(aggregateName, joinCondition, debugLabel);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.All to add an aggregate predicate that evaluates a collection of facts.
        /// </summary>
        /// <typeparam name="T">The fact type contained in the aggregate.</typeparam>
        /// <param name="aggregatePredicate">A predicate that receives the aggregated <see cref="IEnumerable{T}"/> and returns <c>true</c> if the 
        /// aggregate condition holds.</param>
        /// <returns>The current <see cref="IEvaluationBuilder"/> to continue building evaluations after the aggregate.</returns>
        public IEvaluationBuilder All<T>(Func<IEnumerable<T>, bool> aggregatePredicate)
        {
            _builder.All(aggregateName, aggregatePredicate);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.And that adds a join condition for a named fact.
        /// </summary>
        /// <typeparam name="T">The fact type to join.</typeparam>
        /// <param name="name">The name to assign to the fact within the rule.</param>
        /// <param name="joinCondition">A join predicate invoked with the current <see cref="Token"/> and a fact of type <typeparamref name="T"/>.</param>
        /// <param name="debugLabel">An optional debug label for diagnostics.</param>
        /// <returns>The current <see cref="IConnectionBuilder"/> to continue building connections.</returns>
        public IConnectionBuilder And<T>(string name, Func<Token, T, bool> joinCondition, [CallerArgumentExpression(nameof(joinCondition))] string? debugLabel = null)
        {
            _builder.And(name, joinCondition, debugLabel);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Or that adds an OR branch for a named fact with optional multiple conditions.
        /// </summary>
        /// <typeparam name="T">The fact type to evaluate in the OR branch.</typeparam>
        /// <param name="name">The name to assign to the fact within the OR branch.</param>
        /// <param name="debugLabel">An optional debug label for diagnostics.</param>
        /// <param name="orConditions">Optional join predicates; if provided the OR branch will succeed when any predicate succeeds.</param>
        /// <returns>The current <see cref="IConnectionBuilder"/> to continue building connections.</returns>
        public IConnectionBuilder Or<T>(string name, [CallerArgumentExpression(nameof(orConditions))] string? debugLabel = null, params Func<Token, T, bool>[] orConditions)
        {
            _builder.Or(name, debugLabel, orConditions);
            return this;
        }

        /// <summary>
        /// A wrapper for <see cref="ReteBuilder<TInitial>"/>.Then that registers the action to execute when the rule fires.
        /// </summary>
        /// <param name="action">The action to execute when the rule is activated; receives the matching <see cref="Token"/>.</param>
        /// <param name="salience">An optional salience (priority) modifier for the rule's activation.</param>
        public void Then(Action<Token> action, int salience = 0)
        {
            _builder.Then(action, salience);
        }


    }
}
