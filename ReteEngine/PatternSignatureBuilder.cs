//-----------------------------------------------------------------------
// <copyright file="PatternSignatureBuilder.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using ReteCore;

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
            string typeName = typeof(T).Name;
            string conditionPart = condition != null
                ? $":{condition.GetHashCode()}"
                : "";
            return $"Where<{typeName}>({name}){conditionPart}";
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