//-----------------------------------------------------------------------
// <copyright file="BetaNodeRegistry.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReteCore
{
    /// <summary>
    /// A registry for reusing shared beta memory nodes and join node pairs across multiple rules.
    /// This enables beta-node sharing: when two rules request the same (previousNode, patternSignature) pair,
    /// they receive the same physical node instance, dramatically reducing memory footprint and improving
    /// token propagation efficiency.
    /// 
    /// Sharing occurs at compilation time (rule definition) and is transparent to the rule engine.
    /// Each rule's TerminalNode still fires independently, but they share the upstream beta prefix.
    /// </summary>
    public class BetaNodeRegistry
    {
        /// <summary>
        /// Maps (previousNode, patternSignature) → BetaMemory instance.
        /// Used for caching simple beta memory nodes created by Where<T> and other single-node operations.
        /// The key ensures that:
        /// - Same previousNode reference + same signature → reused BetaMemory
        /// - Different previousNode OR different signature → new BetaMemory
        /// </summary>
        private readonly Dictionary<(IReteNode?, string), BetaMemory> _betaMemoryCache = new();

        /// <summary>
        /// Maps (previousNode, joinSignature) → (JoinNode, BetaMemory) pair.
        /// Used for caching join node pairs created by And<T>, Not<T>, etc.
        /// Returns the exact same (join, beta) pair for identical signatures.
        /// </summary>
        private readonly Dictionary<(IReteNode?, string), (JoinNode, BetaMemory)> _joinNodeCache = new();

        /// <summary>
        /// Counters for diagnostic/telemetry purposes.
        /// </summary>
        private int _betaMemoryCacheHits = 0;
        private int _betaMemoryCacheMisses = 0;
        private int _joinNodeCacheHits = 0;
        private int _joinNodeCacheMisses = 0;

        /// <summary>
        /// Retrieves or creates a BetaMemory node for a given pattern at a given point in the network.
        /// If a BetaMemory with the exact (previousNode, patternSignature) pair exists, it is reused.
        /// Otherwise, a new BetaMemory is created and cached.
        /// </summary>
        /// <param name="previousNode">The node before this beta memory (where tokens flow in from). Can be null for root-level Where.</param>
        /// <param name="patternSignature">A unique string identifying the pattern (e.g., "Where<Product>(P):12345").</param>
        /// <returns>A cached or newly created BetaMemory instance.</returns>
        public BetaMemory GetOrCreateBetaMemory(IReteNode? previousNode, string patternSignature)
        {
            if (string.IsNullOrEmpty(patternSignature))
                throw new ArgumentException("Pattern signature cannot be null or empty.", nameof(patternSignature));

            var key = (previousNode, patternSignature);
            if (_betaMemoryCache.TryGetValue(key, out var beta))
            {
                _betaMemoryCacheHits++;
                Console.WriteLine($"[BetaRegistry] Cache HIT: {patternSignature}");
                return beta;
            }

            beta = new BetaMemory();
            _betaMemoryCache[key] = beta;
            _betaMemoryCacheMisses++;
            Console.WriteLine($"[BetaRegistry] Cache MISS (created new): {patternSignature}");
            return beta;
        }

        /// <summary>
        /// Retrieves or creates a (JoinNode, BetaMemory) pair for a join pattern.
        /// Join pairs are atomic: both the join condition and the following beta memory are cached together.
        /// If a pair with the exact (previousNode, joinSignature) exists, it is reused (including its join condition).
        /// Otherwise, a new pair is created and cached.
        /// </summary>
        /// <param name="previousNode">The node before the join (typically a BetaMemory).</param>
        /// <param name="joinSignature">A unique string identifying the join pattern.</param>
        /// <param name="alphaMemory">The alpha memory containing the facts to join with.</param>
        /// <param name="joinCondition">The predicate determining which token-fact pairs match.</param>
        /// <returns>A cached or newly created (JoinNode, BetaMemory) pair.</returns>
        public (JoinNode joinNode, BetaMemory betaMemory) GetOrCreateJoinPair(
            IReteNode? previousNode,
            string joinSignature,
            string tokenName,
            AlphaMemory alphaMemory,
            Func<Token, object, bool> joinCondition)
        {
            if (string.IsNullOrEmpty(joinSignature))
                throw new ArgumentException("Join signature cannot be null or empty.", nameof(joinSignature));
            if (alphaMemory == null)
                throw new ArgumentNullException(nameof(alphaMemory));
            if (joinCondition == null)
                throw new ArgumentNullException(nameof(joinCondition));

            var key = (previousNode, joinSignature);
            if (_joinNodeCache.TryGetValue(key, out var pair))
            {
                _joinNodeCacheHits++;
                Console.WriteLine($"[BetaRegistry] Join HIT: {joinSignature}");
                return pair;
            }

            // Create new join pair
            var join = new JoinNode(previousNode, alphaMemory, tokenName, joinCondition);
            var beta = new BetaMemory();
            join.AddSuccessor(beta);
            pair = (join, beta);

            _joinNodeCache[key] = pair;
            _joinNodeCacheMisses++;
            Console.WriteLine($"[BetaRegistry] Join MISS (created new): {joinSignature}");
            return pair;
        }

        /// <summary>
        /// Connects a node to its predecessor if not already connected.
        /// This ensures that when a cached node is reused, its predecessor knows about it.
        /// Safe to call multiple times; idempotent.
        /// </summary>
        /// <param name="previousNode">The node that should connect to nodeToConnect.</param>
        /// <param name="nodeToConnect">The node to be connected.</param>
        public void ConnectNodeIfNeeded(IReteNode? previousNode, IReteNode nodeToConnect)
        {
            if (previousNode == null)
                return; // Root level, no predecessor

            // Check if already connected
            if (previousNode.Successors.Contains(nodeToConnect))
            {
                Console.WriteLine($"[BetaRegistry] Node already connected, skipping.");
                return;
            }

            // Connect based on predecessor type
            if (previousNode is BetaMemory beta)
            {
                beta.AddSuccessor(nodeToConnect);
                Console.WriteLine($"[BetaRegistry] Connected BetaMemory → {nodeToConnect.GetType().Name}");
            }
            else if (previousNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(nodeToConnect);
                Console.WriteLine($"[BetaRegistry] Connected CompositeBetaMemory → {nodeToConnect.GetType().Name}");
            }
            else if (previousNode is AlphaMemory alpha)
            {
                alpha.AddSuccessor(nodeToConnect);
                Console.WriteLine($"[BetaRegistry] Connected AlphaMemory → {nodeToConnect.GetType().Name}");
            }
            else if (previousNode is AlphaToBetaAdapter adapter)
            {
                adapter.AddSuccessor(nodeToConnect);
                Console.WriteLine($"[BetaRegistry] Connected AlphaToBetaAdapter → {nodeToConnect.GetType().Name}");
            }
            else
            {
                // Generic node type with AddSuccessor
                previousNode.AddSuccessor(nodeToConnect);
                Console.WriteLine($"[BetaRegistry] Connected {previousNode.GetType().Name} → {nodeToConnect.GetType().Name}");
            }
        }

        /// <summary>
        /// Clears all cached nodes. Useful for test isolation or engine reset.
        /// </summary>
        public void Clear()
        {
            _betaMemoryCache.Clear();
            _joinNodeCache.Clear();
            _betaMemoryCacheHits = 0;
            _betaMemoryCacheMisses = 0;
            _joinNodeCacheHits = 0;
            _joinNodeCacheMisses = 0;
            Console.WriteLine("[BetaRegistry] Cleared all cached nodes.");
        }

        /// <summary>
        /// Returns diagnostic statistics about cache performance.
        /// </summary>
        public string GetStatistics()
        {
            int betaTotal = _betaMemoryCacheHits + _betaMemoryCacheMisses;
            int joinTotal = _joinNodeCacheHits + _joinNodeCacheMisses;
            double betaHitRate = betaTotal > 0 ? (_betaMemoryCacheHits * 100.0) / betaTotal : 0;
            double joinHitRate = joinTotal > 0 ? (_joinNodeCacheHits * 100.0) / joinTotal : 0;

            return $@"
BetaNodeRegistry Statistics:
  BetaMemory Cache:  {_betaMemoryCacheHits} hits, {_betaMemoryCacheMisses} misses ({betaHitRate:F1}% hit rate)
  JoinNode Cache:    {_joinNodeCacheHits} hits, {_joinNodeCacheMisses} misses ({joinHitRate:F1}% hit rate)
  Total Cached Nodes: {_betaMemoryCache.Count} BetaMemories + {_joinNodeCache.Count} JoinPairs
";
        }

        /// <summary>
        /// Counts how many nodes are currently cached.
        /// </summary>
        public int GetCachedBetaNodeCount() => _betaMemoryCache.Count;
        public int GetCachedJoinPairCount() => _joinNodeCache.Count;
        public int GetTotalCachedNodeCount() => _betaMemoryCache.Count + (_joinNodeCache.Count * 2); // *2 for join + beta pair
    }
}