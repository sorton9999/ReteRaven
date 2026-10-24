//-----------------------------------------------------------------------
// <copyright file="PatternSignatureBuilder.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using ReteCore;
using System;
using System.Runtime.CompilerServices;

namespace ReteEngine
{
    /// <summary>
    /// Builds stable signature strings for rule patterns to enable beta-node sharing.
    /// 
    /// Signatures are strings that uniquely identify a pattern. Two patterns with identical
    /// signatures will request the same cached node from the registry.
    /// 
    /// Key principle: Only exact delegate instances share (by reference equality using GetHashCode).
    /// This ensures:
    /// - Two rules reusing the exact same lambda instance → same signature → shared nodes
    /// - Two rules with different lambda instances → different signatures → separate nodes
    /// 
    /// This is intentional: condition predicates must be treated carefully. Only identically
    /// captured/referenced lambdas are considered equivalent.
    /// </summary>
    internal static class PatternSignatureBuilder
    {
        /// <summary>
        /// Creates a signature for a Where<T> pattern.
        /// Encodes: type name, pattern name, and optional condition predicate identity.
        /// </summary>
        /// <typeparam name="T">The fact type being matched.</typeparam>
        /// <param name="name">The pattern name (e.g., "P", "I").</param>
        /// <param name="condition">Optional initial condition predicate.</param>
        /// <returns>A unique signature string.</returns>
        public static string WhereSignature<T>(string name, Func<T, bool>? condition)
        {
            string? typeName = typeof(T).FullName;
            string conditionPart = condition != null
                ? $":{condition}"
                : "";
            return $"Where<{typeName}>({name}){conditionPart}";
        }

        /// <summary>
        /// Generates a cache key for join patterns and the token name to use when creating tokens.
        /// This method ensures synchronization between the cache key (used for beta memory registry)
        /// and the token name (used for late filter lookups), preventing mismatches when rules have
        /// late filters that reference facts by name.
        /// </summary>
        /// <typeparam name="T">The type of fact being joined.</typeparam>
        /// <param name="name">The logical name provided by the rule builder for this join.</param>
        /// <param name="alpha">The alpha memory instance for this type.</param>
        /// <param name="joinCondition">The join condition predicate function.</param>
        /// <returns>A tuple containing: (cacheKey, tokenName) where cacheKey is used for the beta 
        /// memory registry and tokenName is what should be stored in token's NamedFacts.</returns>
        public static (string cacheKey, string tokenName) GenerateJoinSignature<T>(
           string name,
           AlphaMemory alpha,
           Func<Token, T, bool> joinCondition,
           [CallerMemberName] string methodName = "")
        {
            var methodPointer = joinCondition.Method.MethodHandle.Value;

            // Generate a unique cache key that includes all factors affecting join semantics
            // This prevents false cache hits when conditions differ
            string cacheKey = $"{methodName}<{typeof(T).Name}>({name}):{alpha.GetHashCode()}:{methodPointer}";

            // The token name is always the simple provided name - this is what late filters look for
            // by calling token.Get<T>(name)
            string tokenName = name;

            return (cacheKey, tokenName);
        }

        /// <summary>
        /// Generate a cache key for a given array of join conditions.  The array of joins is used in
        /// .Or rules where all conditions are evaluated where any one that is true sets the rule as 
        /// true.  Each member of the array will be used to generate the key.  The (key-token name) 
        /// tuple is returned.  The token name is returned to act as a stand-in when late filters are
        /// used.
        /// </summary>
        /// <param name="name">The logical name provided by the rule builder for this join.</param>
        /// <param name="alpha">The alpha memory instance for this type.</param>
        /// <param name="joinConditions">The array of join condition predicate functions.</param>
        /// <returns>A tuple containing: (cacheKey, tokenName) where cacheKey is used for the beta 
        /// memory registry and tokenName is what should be stored in token's NamedFacts.</returns>
        public static (string cacheKey, string tokenName) GenerateJoinArraySignature<T>(
            string name,
            AlphaMemory alpha,
            Func<Token, T, bool>[] joinConditions,
            [CallerMemberName] string methodName = "")
        {
            var methodPointers = joinConditions
                .Select(cond => cond.Method.MethodHandle.Value.ToString())
                .OrderBy(p => p);
            string conditionKey = string.Join("|", methodPointers);

            string cacheKey = $"{methodName}<{typeof(T).Name}>({name}):{alpha.GetHashCode()}:{conditionKey}";
            string tokenName = name;

            return (cacheKey, tokenName);
        }

        /// <summary>
        /// Creates a signature for an And<T> join pattern.
        /// Encodes: type name, join name, and join condition predicate identity.
        /// 
        /// Note: joinCondition can be null or a delegate. Null joinConditions all share the same signature.
        /// Non-null joinConditions are compared by reference (GetHashCode), not by logical equivalence.
        /// </summary>
        /// <typeparam name="T">The fact type being joined.</typeparam>
        /// <param name="name">The join name.</param>
        /// <param name="joinCondition">Optional join predicate.</param>
        /// <returns>A unique signature string.</returns>
        public static string AndSignature<T>(string name, Func<Token, T, bool>? joinCondition)
        {
            string typeName = typeof(T).Name;
            string conditionPart = joinCondition != null
                ? $":{joinCondition.GetHashCode()}"
                : "";
            return $"And<{typeName}>({name}){conditionPart}";
        }

        /// <summary>
        /// Creates a signature for a Not<T> negation pattern.
        /// </summary>
        /// <typeparam name="T">The fact type being tested.</typeparam>
        /// <param name="name">The test name.</param>
        /// <param name="joinCondition">Optional join predicate.</param>
        /// <returns>A unique signature string.</returns>
        public static string NotSignature<T>(string name, Func<Token, T, bool>? joinCondition)
        {
            string typeName = typeof(T).Name;
            string conditionPart = joinCondition != null
                ? $":{joinCondition.GetHashCode()}"
                : "";
            return $"Not<{typeName}>({name}){conditionPart}";
        }

        /// <summary>
        /// Creates a signature for an Exists<T> existence pattern.
        /// </summary>
        /// <typeparam name="T">The fact type being tested.</typeparam>
        /// <param name="name">The test name.</param>
        /// <param name="joinCondition">Optional join predicate.</param>
        /// <returns>A unique signature string.</returns>
        public static string ExistsSignature<T>(string name, Func<Token, T, bool>? joinCondition)
        {
            string typeName = typeof(T).Name;
            string conditionPart = joinCondition != null
                ? $":{joinCondition.GetHashCode()}"
                : "";
            return $"Exists<{typeName}>({name}){conditionPart}";
        }

        /// <summary>
        /// Creates a signature for an AndNot<T> pattern (negated and).
        /// </summary>
        /// <typeparam name="T">The fact type being tested.</typeparam>
        /// <param name="name">The test name.</param>
        /// <param name="joinCondition">Optional join predicate.</param>
        /// <returns>A unique signature string.</returns>
        public static string AndNotSignature<T>(string name, Func<Token, T, bool>? joinCondition)
        {
            string typeName = typeof(T).Name;
            string conditionPart = joinCondition != null
                ? $":{joinCondition.GetHashCode()}"
                : "";
            return $"AndNot<{typeName}>({name}){conditionPart}";
        }
    }
}