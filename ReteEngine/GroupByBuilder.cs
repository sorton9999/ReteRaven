//-----------------------------------------------------------------------
// <copyright file="GroupByBuilder.cs">
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
using System.Text;
using System.Threading.Tasks;

namespace ReteEngine
{
    /// <summary>
    /// Defines the fluent configuration contract for a standalone <c>GroupBy</c> aggregation, 
    /// allowing callers to filter candidate facts before partitioning them into keyed buckets 
    /// and applying scalar aggregations inherited from <see cref="IBaseGroup{TInitial, TRightFact}"/>.
    /// </summary>
    /// <typeparam name="TInitial">The initial fact type associated with the rule chain.</typeparam>
    /// <typeparam name="TRightFact">The type of fact being partitioned and aggregated.</typeparam>
    /// <typeparam name="TKey">The non-nullable type of the grouping key.</typeparam>
    public interface IGroupByBuilder<TInitial, TRightFact, TKey> : IBaseGroup<TInitial, TRightFact>
        where TKey : notnull
    {
        IGroupByBuilder<TInitial, TRightFact, TKey> Where(Func<TRightFact, bool> filter);
    }

    /// <summary>
    /// Configures and compiles a standalone <see cref="GroupByNode{TRightFact, TKey}"/> that partitions 
    /// incoming facts of type <typeparamref name="TRightFact"/> into dynamic key buckets without requiring 
    /// an upstream left-hand <see cref="Token"/> anchor.
    /// </summary>
    /// <typeparam name="TInitial">The initial fact type associated with the rule chain.</typeparam>
    /// <typeparam name="TRightFact">The type of fact being partitioned and aggregated.</typeparam>
    /// <typeparam name="TKey">The non-nullable type of the grouping key.</typeparam>
    public class GroupByBuilder<TInitial, TRightFact, TKey> 
        : BaseGroupBuilder<TInitial, TRightFact>, IGroupByBuilder<TInitial, TRightFact, TKey>, IGroupAggregationShim<TInitial>
        where TKey : notnull
    {
        /// <summary>
        /// Extracts the grouping key from each candidate fact of type <typeparamref name="TRightFact"/>.
        /// </summary>
        private readonly Func<TRightFact, TKey> _keySelector;
        /// <summary>
        /// The token alias name under which each bucket's distinct grouping key will be bound.
        /// </summary>
        private readonly string _keyName;
        /// <summary>
        /// Optional single-fact predicate applied to filter incoming facts prior to bucketing.
        /// </summary>
        private Func<TRightFact, bool>? _filter;

        /// <summary>
        /// Initializes a new instance of <see cref="GroupByBuilder{TInitial, TRightFact, TKey}"/>.
        /// </summary>
        /// <param name="reteBuilder">The parent <see cref="ReteBuilder{TInitial}"/> managing the rule pipeline.</param>
        /// <param name="alphaMemory">The <see cref="AlphaMemory"/> supplying candidate facts to group.</param>
        /// <param name="keySelector">The delegate used to extract the grouping key from each fact.</param>
        /// <param name="keyName">The token alias name under which each bucket's key is stored.</param>
        public GroupByBuilder(
            ReteBuilder<TInitial> reteBuilder,
            AlphaMemory alphaMemory,
            Func<TRightFact, TKey> keySelector,
            string keyName)
            : base(reteBuilder, alphaMemory)
        {
            _keySelector = keySelector;
            _keyName = keyName;
        }

        /// <summary>
        /// Sets the single-fact filter predicate used to exclude facts before assigning them to buckets.
        /// </summary>
        /// <param name="filter">The predicate evaluated against each candidate fact of type <typeparamref name="TRightFact"/>.</param>
        /// <returns>The current <see cref="IGroupByBuilder{TInitial, TRightFact, TKey}"/> instance for chaining an aggregation.</returns>
        public IGroupByBuilder<TInitial, TRightFact, TKey> Where(Func<TRightFact, bool> filter)
        {
            _filter = filter;
            return this;
        }

        #region IGroupAggregationShim Impls

        /// <summary>
        /// Rejects two-argument <c>(Token, T)</c> join predicates because standalone <c>GroupBy</c> 
        /// operates directly on an <see cref="AlphaMemory"/> without a preceding left-hand <see cref="Token"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the caller.</typeparam>
        /// <param name="joinCondition">The unsupported two-argument join condition.</param>
        /// <exception cref="InvalidOperationException">Always thrown for standalone <c>GroupBy</c> rules.</exception>
        void IGroupAggregationShim<TInitial>.ApplyWhere<T>(Func<Token, T, bool> joinCondition) => 
            throw new InvalidOperationException("Standalone GroupBy requires a single-argument Func<T, bool> filter predicate.");

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and applies 
        /// the single-fact filter predicate to this <c>GroupBy</c> builder.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="filter">The single-fact predicate evaluated against each candidate item.</param>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        void IGroupAggregationShim<TInitial>.ApplyWhere<T>(Func<T, bool> filter)
        {
            if (filter is Func<TRightFact, bool> typed)
            {
                Where(typed);
            }
            else
            {
                throw new InvalidOperationException($"Expected {typeof(TRightFact).Name}, got {typeof(T).Name}.");
            }
        }


        /// <summary>
        /// Adapts the generic integer <c>Sum</c> selector from <see cref="IGroupAggregationShim{TInitial}"/> 
        /// and delegates execution to <see cref="BaseGroupBuilder{TInitial, TRightFact}.Sum(Func{TRightFact, int}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="int"/> property to sum from each fact.</param>
        /// <param name="asVariableName">The token alias under which each bucket's computed sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Sum<T>(Func<T, int> selector, string asVariableName) =>
            Sum((Func<TRightFact, int>)(object)selector, asVariableName);

        /// <summary>
        /// Adapts the generic decimal <c>Sum</c> selector from <see cref="IGroupAggregationShim{TInitial}"/> 
        /// and delegates execution to <see cref="BaseGroupBuilder{TInitial, TRightFact}.Sum(Func{TRightFact, decimal}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="decimal"/> property to sum from each fact.</param>
        /// <param name="asVariableName">The token alias under which each bucket's computed sum will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Sum<T>(Func<T, decimal> selector, string asVariableName) =>
            Sum((Func<TRightFact, decimal>)(object)selector, asVariableName);

        /// <summary>
        /// Adapts the generic decimal <c>Average</c> selector from <see cref="IGroupAggregationShim{TInitial}"/> 
        /// and delegates execution to <see cref="BaseGroupBuilder{TInitial, TRightFact}.Average(Func{TRightFact, decimal}, string)"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the fluent caller.</typeparam>
        /// <param name="selector">Extracts the <see cref="decimal"/> property to average from each fact.</param>
        /// <param name="asVariableName">The token alias under which each bucket's computed average will be stored.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> instance.</returns>
        ReteBuilder<TInitial> IGroupAggregationShim<TInitial>.Average<T>(Func<T, decimal> selector, string asVariableName) =>
            Average((Func<TRightFact, decimal>)(object)selector, asVariableName);

        #endregion

        #region BaseGroupBuilder overrides

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> because standalone <c>GroupBy</c> 
        /// does not join against a preceding left-hand <see cref="Token"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the caller.</typeparam>
        /// <param name="joinCondition">The unsupported join predicate.</param>
        /// <exception cref="InvalidOperationException">Always thrown for standalone <c>GroupBy</c> rules.</exception>
        protected override void ApplyJoinWhere<T>(Func<Token, T, bool> joinCondition)
        {
            throw new InvalidOperationException("Standalone GroupBy does not support two-argument (Token, T) join conditions; use a single-argument Func<T, bool> filter instead.");
        }

        /// <summary>
        /// Validates that <typeparamref name="T"/> matches <typeparamref name="TRightFact"/> and forwards 
        /// the filter predicate to <see cref="Where(Func{TRightFact, bool})"/>.
        /// </summary>
        /// <typeparam name="T">The fact type specified by the caller.</typeparam>
        /// <param name="filter">The single-fact predicate to apply.</param>
        /// <exception cref="InvalidOperationException">Thrown when <typeparamref name="T"/> does not match <typeparamref name="TRightFact"/>.</exception>
        protected override void ApplyFilterWhere<T>(Func<T, bool> filter)
        {
            if (typeof(T) != typeof(TRightFact))
                throw new InvalidOperationException($"Type mismatch: GroupBy is for {typeof(TRightFact).Name} but Where requested {typeof(T).Name}.");

            Where((Func<TRightFact, bool>)(object)filter);
        }
        #endregion

        /// <summary>
        /// Builds (or retrieves from <see cref="BetaNodeRegistry"/>) a <see cref="GroupByNode{TRightFact, TKey}"/> 
        /// and downstream <see cref="BetaMemory"/>, connects them to <see cref="BaseGroupBuilder{TInitial, TRightFact}._alphaMemory"/>, 
        /// and updates the builder's tail node.
        /// </summary>
        /// <param name="typedAggregator">The strongly typed aggregation delegate applied to each bucket's fact list.</param>
        /// <param name="variableName">The token alias under which each bucket's computed aggregate value will be bound.</param>
        /// <param name="aggregatorKind">A descriptive key identifying the aggregation operation (e.g., <c>"Count"</c>, <c>"Min:Int32"</c>, <c>"Max:Int32"</c>).</param>
        /// <param name="selector">The optional property-selector delegate included in the cache signature.</param>
        /// <returns>The updated <see cref="ReteBuilder{TInitial}"/> pointing to the downstream <see cref="BetaMemory"/>.</returns>
        protected override ReteBuilder<TInitial> CompileAggregation(
            Func<IEnumerable<TRightFact>, object> typedAggregator,
            string variableName,
            string aggregatorKind,
            Delegate? selector)
        {
            string signature = PatternSignatureBuilder.GenerateGroupBySignature(
                _alphaMemory,
                _keySelector,
                _filter,
                aggregatorKind,
                selector,
                _keyName,
                variableName);

            IReteNode groupByNode;
            BetaMemory downstreamBeta;

            var registry =_reteBuilder.Engine.BetaNodeRegistry;
            if (registry != null)
            {
                (groupByNode, downstreamBeta) = registry.GetOrCreateGroupByPair(
                    _alphaMemory,
                    signature,
                    () => new GroupByNode<TRightFact, TKey>(
                        _keySelector,
                        _filter,
                        typedAggregator,
                        _keyName,
                        variableName));

                registry.ConnectNodeIfNeeded(_alphaMemory, groupByNode);
            }
            else
            {
                groupByNode = new GroupByNode<TRightFact, TKey>(
                    _keySelector,
                    _filter,
                    typedAggregator,
                    _keyName,
                    variableName);

                _alphaMemory.AddSuccessor(groupByNode);

                downstreamBeta = new BetaMemory();
                groupByNode.AddSuccessor(downstreamBeta);
            }

            _reteBuilder.SetLastNode(downstreamBeta);

            return _reteBuilder;
        }
    }
}
