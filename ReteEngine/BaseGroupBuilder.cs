using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteEngine
{
    public interface IBaseGroup<TInitial, TRightFact>
    {
        ReteBuilder<TInitial> Count(string asVariableName);
        ReteBuilder<TInitial> Sum(Func<TRightFact, int> selector, string asVariableName);
        ReteBuilder<TInitial> Sum(Func<TRightFact, decimal> selector, string asVariableName);
        ReteBuilder<TInitial> Average(Func<TRightFact, decimal> selector, string asVariableName);
        ReteBuilder<TInitial> Min<TResult>(Func<TRightFact, TResult> selector, string asVariableName)
            where TResult : IComparable<TResult>;
        ReteBuilder<TInitial> Max<TResult>(Func<TRightFact, TResult> selector, string asVariableName)
            where TResult : IComparable<TResult>;
    }

    public interface IGroupAggregationShim<TInitial>
    {
        void ApplyWhere<T>(Func<Token, T, bool> joinCondition);
        void ApplyWhere<T>(Func<T, bool> filter);
        ReteBuilder<TInitial> Count(string asVariableName);
        ReteBuilder<TInitial> Sum<T>(Func<T, int> selector, string asVariableName);
        ReteBuilder<TInitial> Sum<T>(Func<T, decimal> selector, string asVariableName);
        ReteBuilder<TInitial> Average<T>(Func<T, decimal> selector, string asVariableName);
        ReteBuilder<TInitial> Min<T, TResult>(Func<T, TResult> selector, string asVariableName)
            where TResult : IComparable<TResult>;
        ReteBuilder<TInitial> Max<T, TResult>(Func<T, TResult> selector, string asVariableName)
            where TResult : IComparable<TResult>;
    }

    /// <summary>
    /// Provides a shared base implementation for fluent aggregation builders (<see cref="GroupBuilder{TInitial, TRightFact}"/> 
    /// and <see cref="GroupByBuilder{TInitial, TRightFact, TKey}"/>), centralizing strongly typed scalar aggregation operations 
    /// (<c>Count</c>, <c>Sum</c>, <c>Average</c>, <c>Min</c>, <c>Max</c>) and type-erased <see cref="IGroupAggregationShim{TInitial}"/> adapters.
    /// </summary>
    /// <typeparam name="TInitial">The initial fact type that started the rule chain.</typeparam>
    /// <typeparam name="TRightFact">The type of fact being collected and aggregated.</typeparam>
    public abstract class BaseGroupBuilder<TInitial, TRightFact>
        : IBaseGroup<TInitial, TRightFact>, IGroupAggregationShim<TInitial>
    {
        /// <summary>
        /// The parent <see cref="ReteBuilder{TInitial}"/> managing the rule's Rete network construction.
        /// </summary>
        protected readonly ReteBuilder<TInitial> _reteBuilder;
        /// <summary>
        /// The <see cref="AlphaMemory"/> supplying candidate facts of type <typeparamref name="TRightFact"/> to aggregate.
        /// </summary>
        protected readonly AlphaMemory _alphaMemory;

        /// <summary>
        /// Initializes a new instance of <see cref="BaseGroupBuilder{TInitial, TRightFact}"/>.
        /// </summary>
        /// <param name="reteBuilder">The parent <see cref="ReteBuilder{TInitial}"/> instance.</param>
        /// <param name="alphaMemory">The <see cref="AlphaMemory"/> containing candidate facts for aggregation.</param>
        protected BaseGroupBuilder(ReteBuilder<TInitial> reteBuilder, AlphaMemory alphaMemory)
        {
            _reteBuilder = reteBuilder;
            _alphaMemory = alphaMemory;
        }

        /// <summary>
        /// Aggregates the total number of matching facts in the group and binds the <see cref="int"/> result 
        /// to <paramref name="asVariableName"/> on the emitted <see cref="Token"/>.
        /// </summary>
        /// <param name="asVariableName">The token alias under which the count will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> to continue chaining downstream filters or actions.</returns>
        public ReteBuilder<TInitial> Count(string asVariableName) =>
            CompileAggregation(items => items.Count(), asVariableName, "Count", null);

        /// <summary>
        /// Computes the integer sum of values selected by <paramref name="selector"/> across all matching facts 
        /// in the group and binds the <see cref="int"/> result to <paramref name="asVariableName"/>.
        /// </summary>
        /// <param name="selector">Extracts the <see cref="int"/> value to sum from each fact.</param>
        /// <param name="asVariableName">The token alias under which the sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> to continue chaining downstream filters or actions.</returns>
        public ReteBuilder<TInitial> Sum(Func<TRightFact, int> selector, string asVariableName) =>
            CompileAggregation(items => items.Sum(selector), asVariableName, "Sum:Int32", selector);

        /// <summary>
        /// Computes the decimal sum of values selected by <paramref name="selector"/> across all matching facts 
        /// in the group and binds the <see cref="decimal"/> result to <paramref name="asVariableName"/>.
        /// </summary>
        /// <param name="selector">Extracts the <see cref="decimal"/> value to sum from each fact.</param>
        /// <param name="asVariableName">The token alias under which the sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> to continue chaining downstream filters or actions.</returns>
        public ReteBuilder<TInitial> Sum(Func<TRightFact, decimal> selector, string asVariableName) =>
            CompileAggregation(items => items.Sum(selector), asVariableName, "Sum:Decimal", selector);

        /// <summary>
        /// Computes the decimal arithmetic mean of values selected by <paramref name="selector"/> across all matching facts 
        /// in the group (returning <c>0m</c> when empty) and binds the result to <paramref name="asVariableName"/>.
        /// </summary>
        /// <param name="selector">Extracts the <see cref="decimal"/> value to average from each fact.</param>
        /// <param name="asVariableName">The token alias under which the average will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> to continue chaining downstream filters or actions.</returns>
        public ReteBuilder<TInitial> Average(Func<TRightFact, decimal> selector, string asVariableName) =>
            CompileAggregation(items => items.Any() ? items.Average(selector) : 0m, asVariableName, "Average:Decimal", selector);

        /// <summary>
        /// Finds the minimum value selected by <paramref name="selector"/> across all matching facts in the group 
        /// (returning <see langword="default"/> when empty) and binds the result to <paramref name="asVariableName"/>.
        /// </summary>
        /// <typeparam name="TResult">The comparable type of the selected property.</typeparam>
        /// <param name="selector">Extracts the comparable value from each fact.</param>
        /// <param name="asVariableName">The token alias under which the minimum value will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> to continue chaining downstream filters or actions.</returns>
        public ReteBuilder<TInitial> Min<TResult>(Func<TRightFact, TResult> selector, string asVariableName)
            where TResult : IComparable<TResult> =>
            CompileAggregation(items => items.Any() ? items.Min(selector)! : default!, asVariableName, $"Min:{typeof(TResult).Name}", selector);

        /// <summary>
        /// Finds the maximum value selected by <paramref name="selector"/> across all matching facts in the group 
        /// (returning <see langword="default"/> when empty) and binds the result to <paramref name="asVariableName"/>.
        /// </summary>
        /// <typeparam name="TResult">The comparable type of the selected property.</typeparam>
        /// <param name="selector">Extracts the comparable value from each fact.</param>
        /// <param name="asVariableName">The token alias under which the maximum value will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> to continue chaining downstream filters or actions.</returns>
        public ReteBuilder<TInitial> Max<TResult>(Func<TRightFact, TResult> selector, string asVariableName)
            where TResult : IComparable<TResult> =>
            CompileAggregation(items => items.Any() ? items.Max(selector)! : default!, asVariableName, $"Max:{typeof(TResult).Name}", selector);

        #region Implement IGroupAggregation<TInitial>

        /// <summary>
        /// Forwards a two-argument <c>(Token, T)</c> join predicate from the type-erased shim 
        /// to the concrete builder's <see cref="ApplyJoinWhere{T}"/> implementation.
        /// </summary>
        /// <typeparam name="T">The expected right-hand fact type.</typeparam>
        /// <param name="joinCondition">The join predicate evaluated against the current token and candidate fact.</param>
        void IGroupAggregationShim<TInitial>.ApplyWhere<T>(Func<Token, T, bool> joinCondition) =>
            ApplyJoinWhere(joinCondition);

        /// <summary>
        /// Forwards a single-fact <c>(T)</c> filter predicate from the type-erased shim 
        /// to the concrete builder's <see cref="ApplyFilterWhere{T}"/> implementation.
        /// </summary>
        /// <typeparam name="T">The expected fact type being filtered.</typeparam>
        /// <param name="filter">The filter predicate evaluated against each candidate fact.</param>
        void IGroupAggregationShim<TInitial>.ApplyWhere<T>(Func<T, bool> filter) =>
            ApplyFilterWhere(filter);

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and delegates 
        /// the integer <c>Sum</c> aggregation to <see cref="Sum(Func{TRightFact, int}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="int"/> property to sum from each fact.</param>
        /// <param name="asVariableName">The token alias under which the computed sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Sum<T>(Func<T, int> selector, string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Sum requested {typeof(T).Name}.");

            return Sum((Func<TRightFact, int>)(object)selector, asVariableName);
        }

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and delegates 
        /// the decimal <c>Sum</c> aggregation to <see cref="Sum(Func{TRightFact, decimal}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="decimal"/> property to sum from each fact.</param>
        /// <param name="asVariableName">The token alias under which the computed sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Sum<T>(Func<T, decimal> selector, string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Sum requested {typeof(T).Name}.");

            return Sum((Func<TRightFact, decimal>)(object)selector, asVariableName);
        }

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and delegates 
        /// the decimal <c>Average</c> aggregation to <see cref="Average(Func{TRightFact, decimal}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="decimal"/> property to average from each fact.</param>
        /// <param name="asVariableName">The token alias under which the computed average will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Average<T>(Func<T, decimal> selector, string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Average requested {typeof(T).Name}.");

            return Average((Func<TRightFact, decimal>)(object)selector, asVariableName);
        }

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and delegates 
        /// the <c>Min</c> aggregation to <see cref="Min{TResult}(Func{TRightFact, TResult}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <typeparam name="TResult">The comparable type of the selected property.</typeparam>
        /// <param name="selector">Extracts the comparable value from each fact.</param>
        /// <param name="asVariableName">The token alias under which the minimum value will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Min<T, TResult>(
            Func<T, TResult> selector,
            string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Min requested {typeof(T).Name}.");

            return Min((Func<TRightFact, TResult>)(object)selector, asVariableName);
        }

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and delegates 
        /// the <c>Max</c> aggregation to <see cref="Max{TResult}(Func{TRightFact, TResult}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <typeparam name="TResult">The comparable type of the selected property.</typeparam>
        /// <param name="selector">Extracts the comparable value from each fact.</param>
        /// <param name="asVariableName">The token alias under which the maximum value will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Max<T, TResult>(
            Func<T, TResult> selector,
            string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Max requested {typeof(T).Name}.");

            return Max((Func<TRightFact, TResult>)(object)selector, asVariableName);
        }

        #endregion

        /// <summary>
        /// Applies a two-argument <c>(Token, T)</c> join predicate in derived builders that support left-hand token context.
        /// </summary>
        /// <typeparam name="T">The type of right-hand fact being joined.</typeparam>
        /// <param name="joinCondition">The join predicate to apply.</param>
        protected abstract void ApplyJoinWhere<T>(Func<Token, T, bool> joinCondition);
        /// <summary>
        /// Applies a single-fact <c>(T)</c> filter predicate to restrict candidate facts prior to aggregation.
        /// </summary>
        /// <typeparam name="T">The type of fact being filtered.</typeparam>
        /// <param name="filter">The filter predicate to apply.</param>
        protected abstract void ApplyFilterWhere<T>(Func<T, bool> filter);

        /// <summary>
        /// Constructs (or retrieves from <see cref="BetaNodeRegistry"/>) the concrete Rete aggregation node 
        /// (such as <see cref="AggregationGroupNode"/> or <see cref="GroupByNode{TRightFact, TKey}"/>), 
        /// wires it into the network, and advances the builder's tail node to the downstream <see cref="BetaMemory"/>.
        /// </summary>
        /// <param name="typedAggregator">
        /// The strongly typed aggregation function applied to the matched collection of <typeparamref name="TRightFact"/> items.
        /// </param>
        /// <param name="variableName">
        /// The token alias under which the computed scalar result will be bound on emitted tokens.
        /// </param>
        /// <param name="aggregatorKind">
        /// A descriptive identifier for the aggregation operation and return type (e.g., <c>"Count"</c>, <c>"Sum:Int32"</c>, <c>"Min:Int32"</c>) 
        /// used to generate unique cache signatures.
        /// </param>
        /// <param name="selector">
        /// The optional property-selector delegate used by numeric aggregations, included in the cache signature 
        /// to prevent collisions across different property selectors, or <see langword="null"/> for <c>Count</c>.
        /// </param>
        /// <returns>
        /// The updated <see cref="ReteBuilder{TInitial}"/> pointing to the downstream <see cref="BetaMemory"/>.
        /// </returns>
        protected abstract ReteBuilder<TInitial> CompileAggregation(
            Func<IEnumerable<TRightFact>, object> typedAggregator,
            string variableName,
            string aggregatorKind,
            Delegate? selector);
    }
}
