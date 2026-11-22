//-----------------------------------------------------------------------
// <copyright file="GroupBuilder.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReteEngine
{
    /// <summary>
    /// Configures and compiles an anchored aggregation group that collects and aggregates right-hand facts 
    /// of type <typeparamref name="TRightFact"/> for each left-hand <see cref="Token"/> in a Rete rule.
    /// </summary>
    /// <typeparam name="TInitial">The initial fact type that started the rule chain.</typeparam>
    /// <typeparam name="TRightFact">The type of fact being joined and aggregated per left-hand token.</typeparam>
    public interface IGroupBuilder<TInitial, TRightFact> : IBaseGroup<TInitial, TRightFact>
    {
        IGroupBuilder<TInitial, TRightFact> Where(Func<Token, TRightFact, bool> joinCondition);
    }


    /// <summary>
    /// The GroupBuilder class is a concrete implementation of the IGroupBuilder interface, providing functionality to define grouping 
    /// conditions and aggregation operations in a Rete network. It allows users to specify how facts should be grouped based on a join 
    /// condition and perform aggregations like Sum, Count, and Average on the grouped data. The class is generic, with TInitial 
    /// representing the type of the initial fact and TRightFact representing the type of facts being grouped.
    /// </summary>
    /// <typeparam name="TInitial">The type of the initial fact in the Rete network.</typeparam>
    /// <typeparam name="TRightFact">The type of the facts being grouped in the Rete network.</typeparam>
    public class GroupBuilder<TInitial, TRightFact>
    : BaseGroupBuilder<TInitial, TRightFact>, IGroupBuilder<TInitial, TRightFact>, IGroupAggregationShim<TInitial>
    {
        private Func<Token, TRightFact, bool>? _storedJoinCondition;

        /// <summary>
        /// Initializes a new instance of <see cref="GroupBuilder{TInitial, TRightFact}"/>.
        /// </summary>
        /// <param name="reteBuilder">The parent <see cref="ReteBuilder{TInitial}"/> managing the rule pipeline.</param>
        /// <param name="alphaMemory">The <see cref="AlphaMemory"/> supplying candidate right-hand facts.</param>
        public GroupBuilder(ReteBuilder<TInitial> reteBuilder, AlphaMemory alphaMemory)
            : base(reteBuilder, alphaMemory) { }

        /// <summary>
        /// Sets the join predicate used to match right-hand facts of type <typeparamref name="TRightFact"/> 
        /// against each incoming left-hand <see cref="Token"/> before aggregation.
        /// </summary>
        /// <param name="joinCondition">
        /// The predicate receiving the current <see cref="Token"/> and a candidate <typeparamref name="TRightFact"/>.
        /// </param>
        /// <returns>The current <see cref="IGroupBuilder{TInitial, TRightFact}"/> instance for chaining an aggregation operation.</returns>
        public IGroupBuilder<TInitial, TRightFact> Where(Func<Token, TRightFact, bool> joinCondition)
        {
            _storedJoinCondition = joinCondition;
            return this;
        }

        #region IGroupAggregationShim Impls

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and applies 
        /// the two-argument <c>(Token, T)</c> join predicate to this anchored group builder.
        /// </summary>
        /// <typeparam name="T">The right-hand fact type specified by the fluent caller.</typeparam>
        /// <param name="joinCondition">The predicate evaluated against the current <see cref="Token"/> and a candidate fact.</param>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        void IGroupAggregationShim<TInitial>.ApplyWhere<T>(Func<Token, T, bool> joinCondition)
        {
            if (joinCondition is Func<Token, TRightFact, bool> typed)
                Where(typed);
            else
                throw new InvalidOperationException($"Expected {typeof(TRightFact).Name}, got {typeof(T).Name}.");
        }

        /// <summary>
        /// Rejects single-argument <c>(T)</c> filter predicates on the type-erased shim because an anchored <c>Group</c> 
        /// requires a two-argument <c>(Token, T)</c> join condition to correlate right-hand facts with the left-hand token.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="filter">The unsupported single-fact filter predicate.</param>
        /// <exception cref="InvalidOperationException">Always thrown when called via the non-generic shim on an anchored group.</exception>
        void IGroupAggregationShim<TInitial>.ApplyWhere<T>(Func<T, bool> filter) =>
            throw new InvalidOperationException("Anchored Group requires a (Token, T) join condition.");

        /// <summary>
        /// Adapts the generic integer <c>Sum</c> selector from <see cref="IGroupAggregationShim{TInitial}"/> 
        /// and delegates execution to <see cref="BaseGroupBuilder{TInitial, TRightFact}.Sum(Func{TRightFact, int}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="int"/> property to sum from each matched fact.</param>
        /// <param name="asVariableName">The token alias under which the computed sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Sum<T>(Func<T, int> selector, string asVariableName) =>
            Sum((Func<TRightFact, int>)(object)selector, asVariableName);

        /// <summary>
        /// Adapts the generic decimal <c>Sum</c> selector from <see cref="IGroupAggregationShim{TInitial}"/> 
        /// and delegates execution to <see cref="BaseGroupBuilder{TInitial, TRightFact}.Sum(Func{TRightFact, decimal}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="decimal"/> property to sum from each matched fact.</param>
        /// <param name="asVariableName">The token alias under which the computed sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Sum<T>(Func<T, decimal> selector, string asVariableName) =>
            Sum((Func<TRightFact, decimal>)(object)selector, asVariableName);

        /// <summary>
        /// Adapts the generic decimal <c>Average</c> selector from <see cref="IGroupAggregationShim{TInitial}"/> 
        /// and delegates execution to <see cref="BaseGroupBuilder{TInitial, TRightFact}.Average(Func{TRightFact, decimal}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="decimal"/> property to average from each matched fact.</param>
        /// <param name="asVariableName">The token alias under which the computed average will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Average<T>(Func<T, decimal> selector, string asVariableName) =>
            Average((Func<TRightFact, decimal>)(object)selector, asVariableName);

        #endregion

        #region BaseGroupBuilder overrides

        /// <summary>
        /// Validates the type parameter and applies a two-argument <c>(Token, T)</c> join condition.
        /// </summary>
        /// <typeparam name="T">The expected right-hand fact type.</typeparam>
        /// <param name="joinCondition">The join predicate to apply.</param>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        protected override void ApplyJoinWhere<T>(Func<Token, T, bool> joinCondition)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Where requested {typeof(T).Name}.");

            Where((Func<Token, TRightFact, bool>)(object)joinCondition);
        }

        /// <summary>
        /// Adapts a single-fact filter predicate into a <c>(Token, TRightFact)</c> join condition.
        /// </summary>
        /// <typeparam name="T">The expected right-hand fact type.</typeparam>
        /// <param name="filter">The single-fact predicate to apply.</param>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        protected override void ApplyFilterWhere<T>(Func<T, bool> filter)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Where requested {typeof(T).Name}.");

            var typedFilter = (Func<TRightFact, bool>)(object)filter;
            Where((_, fact) => typedFilter(fact));
        }

        #endregion

        /// <summary>
        /// Builds (or retrieves from <see cref="BetaNodeRegistry"/>) an <see cref="AggregationGroupNode"/> 
        /// and downstream <see cref="BetaMemory"/>, wiring them into the Rete network.
        /// </summary>
        /// <param name="aggregator">The typed aggregation function applied to matched facts.</param>
        /// <param name="variableName">The token alias under which the computed scalar result will be bound.</param>
        /// <param name="aggregatorKind">A descriptive key identifying the aggregation operation (e.g., <c>"Count"</c>, <c>"Sum:Int32"</c>).</param>
        /// <param name="selector">The optional value-selector delegate used to distinguish signatures in the node cache.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> pointing to the downstream <see cref="BetaMemory"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no preceding left-hand memory node exists in the rule chain.</exception>
        protected override ReteBuilder<TInitial> CompileAggregation(
            Func<IEnumerable<TRightFact>, object> aggregator,
            string variableName,
            string aggregatorKind,
            Delegate? selector)
        {
            Func<IEnumerable<object>, object> nodeAggregator = rawList => aggregator(rawList.Cast<TRightFact>());
            var parentNode = _reteBuilder.GetLastNode();
            if (parentNode == null)
            {
                throw new InvalidOperationException("Cannot add a Group node without a preceding memory node.");
            }

            Func<Token, object, bool> wrappedJoin = (token, fact) =>
            {
                if (fact is TRightFact typedFact)
                {
                    if (_storedJoinCondition is Func<Token, TRightFact, bool> typedCondition)
                    {
                        return typedCondition(token, typedFact);
                    }
                    // If no .Where was chained on Group, include all facts in the AlphaMemory
                    return _storedJoinCondition == null;
                }
                return false;
            };

            string groupSignature = PatternSignatureBuilder.GenerateAggregationSignature(
                _alphaMemory,
                _storedJoinCondition,
                aggregatorKind,
                selector,
                variableName);

            AggregationGroupNode groupNode;
            BetaMemory downstreamMemory;

            var registry = _reteBuilder.Engine.BetaNodeRegistry;
            if (registry != null)
            {
                (groupNode, downstreamMemory) = registry.GetOrCreateAggregationPair(
                    parentNode,
                    groupSignature,
                    _alphaMemory,
                    wrappedJoin,
                    variableName,
                    nodeAggregator);

                registry.ConnectNodeIfNeeded(_alphaMemory, groupNode);
                registry.ConnectNodeIfNeeded(parentNode, groupNode);
            }
            else
            {
                groupNode = new AggregationGroupNode(
                    (ILatentMemory)parentNode,
                    _alphaMemory,
                    wrappedJoin,
                    variableName,
                    nodeAggregator);

                _alphaMemory.AddSuccessor(groupNode);

                if (parentNode is BetaMemory beta)
                {
                    beta.AddSuccessor(groupNode);
                }
                else if (parentNode is CompositeBetaMemory compositeBeta)
                {
                    compositeBeta.AddSuccessor(groupNode);
                }

                downstreamMemory = new BetaMemory();
                groupNode.AddSuccessor(downstreamMemory);
            }

            _reteBuilder.SetLastNode(downstreamMemory);
            return _reteBuilder;
        }
    }
}
