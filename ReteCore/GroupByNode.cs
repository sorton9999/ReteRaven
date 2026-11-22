using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ReteCore
{
    /// <summary>
    /// Exposes non-generic bucket inspection metrics for a <see cref="GroupByNode{TRightFact, TKey}"/>, 
    /// allowing the engine to query live group counts without knowing the node's generic type arguments at compile time.
    /// </summary>
    public interface IGroupByMetrics
    {
        string KeyName { get; }
        IReadOnlyDictionary<object, int> ObjectCounts { get; }
    }

    /// <summary>
    /// A Rete network node that dynamically partitions incoming facts of type <typeparamref name="TRightFact"/> 
    /// into keyed buckets using <see cref="_keySelector"/>, incrementally computing an aggregate value per bucket 
    /// and emitting one derived <see cref="Token"/> per non-empty group.
    /// </summary>
    /// <typeparam name="TRightFact">The type of fact being partitioned and aggregated.</typeparam>
    /// <typeparam name="TKey">The non-nullable type of the grouping key.</typeparam>
    public class GroupByNode<TRightFact, TKey> : IReteNode, IGroupByMetrics where TKey : notnull
    {
        /// <summary>
        /// Extracts the grouping key from each incoming fact of type <typeparamref name="TRightFact"/>.
        /// </summary>
        private readonly Func<TRightFact, TKey> _keySelector;
        /// <summary>
        /// Optional predicate used to filter out facts before they are assigned to a bucket.
        /// </summary>
        private readonly Func<TRightFact, bool>? _filter;
        /// <summary>
        /// Computes the scalar aggregation result (e.g., <c>Count</c>, <c>Sum</c>, <c>Average</c>, <c>Min</c>, or <c>Max</c>) 
        /// across all facts currently residing in a single bucket.
        /// </summary>
        private readonly Func<IEnumerable<TRightFact>, object> _aggregator;
        /// <summary>
        /// The token alias name under which each bucket's distinct key is bound.
        /// </summary>
        private readonly string _keyName;
        /// <summary>
        /// The token alias name under which each bucket's computed aggregate value is bound.
        /// </summary>
        private readonly string _valueName;
        /// <summary>
        /// Maps each active grouping key to the list of facts currently belonging to that bucket.
        /// </summary>
        private readonly Dictionary<TKey, List<TRightFact>> _buckets = new();
        /// <summary>
        /// Tracks the current bucket key assigned to each active fact so key mutations during <see cref="Refresh"/> 
        /// can migrate the fact from its old bucket to its new bucket.
        /// </summary>
        private readonly Dictionary<TRightFact, TKey> _factToKey = new();
        /// <summary>
        /// Stores the most recently asserted downstream <see cref="Token"/> for each bucket key 
        /// so stale tokens can be retracted cleanly when a bucket's aggregate changes or empties.
        /// </summary>
        private readonly Dictionary<TKey, Token> _lastPropagatedTokens = new();
        /// <summary>
        /// Downstream Rete nodes subscribed to token assertions and retractions from this node.
        /// </summary>
        private readonly List<IReteNode> _successors = new();

        /// <summary>
        /// Gets or sets the upstream parent <see cref="IReteNode"/> feeding facts into this node.
        /// </summary>
        public IReteNode? Parent { get; set; }

        /// <summary>
        /// Gets the enumerable sequence of downstream <see cref="IReteNode"/> successors connected to this node.
        /// </summary>
        public IEnumerable<IReteNode> Successors => _successors;

        /// <summary>
        /// Gets a snapshot dictionary mapping each active grouping key to the number of facts currently in its bucket.
        /// </summary>
        public IReadOnlyDictionary<TKey, int> Counts => _buckets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);

        /// <summary>
        /// Returns the Key Name
        /// </summary>
        string IGroupByMetrics.KeyName => _keyName;

        /// <summary>
        /// Returns the count of objects in the dictionary of fact counts.
        /// </summary>
        IReadOnlyDictionary<object, int> IGroupByMetrics.ObjectCounts => _buckets.ToDictionary(kvp => (object)kvp.Key, kvp => kvp.Value.Count);

        /// <summary>
        /// Registers a downstream <see cref="IReteNode"/> successor and assigns its <see cref="IReteNode.Parent"/> to this node.
        /// </summary>
        /// <param name="node">The downstream Rete node to connect.</param>
        public void AddSuccessor(IReteNode node) { node.Parent = this; _successors.Add(node); }

        /// <summary>
        /// Initializes a new instance of <see cref="GroupByNode{TRightFact, TKey}"/>.
        /// </summary>
        /// <param name="keySelector">The delegate used to extract the grouping key from each incoming fact.</param>
        /// <param name="filter">An optional predicate to exclude facts prior to bucketing.</param>
        /// <param name="aggregator">The aggregation function applied to each bucket's fact collection.</param>
        /// <param name="keyName">The token alias name for the bucket's grouping key.</param>
        /// <param name="valueName">The token alias name for the bucket's aggregated output value.</param>
        public GroupByNode(
            Func<TRightFact, TKey> keySelector,
            Func<TRightFact, bool>? filter,
            Func<IEnumerable<TRightFact>, object> aggregator,
            string keyName,
            string valueName)
        {
            _keySelector = keySelector;
            _filter = filter;
            _aggregator = aggregator;
            _keyName = keyName;
            _valueName = valueName;
        }

        /// <summary>
        /// Evaluates an incoming fact against the optional filter, assigns it to its key bucket, 
        /// recalculates that bucket's aggregate, and propagates the updated token downstream.
        /// </summary>
        /// <param name="fact">The fact asserted into working memory.</param>
        public void Assert(object fact)
        {
            if (fact is not TRightFact typedFact) return;
            if (_filter != null && !_filter(typedFact)) return;

            TKey key = _keySelector(typedFact);
            _factToKey[typedFact] = key;

            if (!_buckets.TryGetValue(key, out var list))
            {
                list = new List<TRightFact>();
                _buckets[key] = list;
            }
            list.Add(typedFact);

            PropagateBucket(key);
        }

        /// <summary>
        /// Removes a retracted fact from its assigned key bucket, recalculates (or removes) the bucket, 
        /// and updates or retracts the bucket's downstream token accordingly.
        /// </summary>
        /// <param name="fact">The fact retracted from working memory.</param>
        public void Retract(object fact)
        {
            if (fact is not TRightFact typedFact) return;
            if (!_factToKey.Remove(typedFact, out var oldKey)) return;

            if (_buckets.TryGetValue(oldKey, out var list))
            {
                list.Remove(typedFact);
                if (list.Count == 0)
                {
                    _buckets.Remove(oldKey);
                }
                PropagateBucket(oldKey);
            }
        }

        /// <summary>
        /// Re-evaluates a modified fact when one of its properties changes, handling filter entry/exit, 
        /// cross-bucket migration if its grouping key changed, or in-place aggregate recalculation.
        /// </summary>
        /// <param name="fact">The modified fact instance.</param>
        /// <param name="propertyName">The name of the property that changed.</param>
        public void Refresh(object fact, string propertyName)
        {
            if (fact is not TRightFact typedFact) return;

            bool wasTracked = _factToKey.TryGetValue(typedFact, out var oldKey);
            bool stillMatchesFilter = _filter == null || _filter(typedFact);

            if (!stillMatchesFilter)
            {
                if (wasTracked) Retract(typedFact);
                return;
            }

            if (!wasTracked)
            {
                Assert(typedFact);
                return;
            }

            TKey newKey = _keySelector(typedFact);
            if (!EqualityComparer<TKey>.Default.Equals(oldKey, newKey))
            {
                // Key changed (e.g. Status changed from "EnRoute" -> "Landed"): move between buckets
                Retract(typedFact);
                Assert(typedFact);
            }
            else
            {
                // Same bucket, recalculate aggregate in case a summed/averaged property changed
                PropagateBucket(oldKey);
            }
        }

        /// <summary>
        /// Detaches a downstream <see cref="IReteNode"/> successor from this node.
        /// </summary>
        /// <param name="successor">The successor node to remove.</param>
        public void RemoveSuccessor(IReteNode successor)
        {
            _successors.Remove(successor);
        }


        /// <summary>
        /// Retracts any previously emitted token for <paramref name="key"/> and, if the bucket still contains facts, 
        /// computes the updated aggregate value and asserts a new derived <see cref="Token"/> downstream.
        /// </summary>
        /// <param name="key">The bucket key whose downstream state needs to be synchronized.</param>
        private void PropagateBucket(TKey key)
        {
            if (_lastPropagatedTokens.TryGetValue(key, out var oldToken))
            {
                foreach (var s in _successors) s.Retract(oldToken);
                _lastPropagatedTokens.Remove(key);
            }

            if (!_buckets.TryGetValue(key, out var list) || list.Count == 0)
            {
                return;
            }

            object aggResult = _aggregator(list);
            var baseToken = new Token(_keyName, key);
            var fullToken = new Token(baseToken, _valueName, aggResult);

            _lastPropagatedTokens[key] = fullToken;
            foreach (var s in _successors) s.Assert(fullToken);
        }

        /// <summary>
        /// Prints a formatted diagnostic summary of this node's successor count and incoming fact to the console.
        /// </summary>
        /// <param name="fact">The fact currently being traced through the network.</param>
        /// <param name="level">The indentation depth representing the node's level in the Rete tree.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 3);
            Console.WriteLine($"{indent}[GroupByNode] - Currently holding {_successors.Count} successors. Fact[{fact}]");
        }

    }
}
