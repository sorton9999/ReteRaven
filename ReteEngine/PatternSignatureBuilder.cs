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
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

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
            string conditionPart = PredicateIdentity(condition);
            return $"Where<{typeName}>({name}){conditionPart}";
        }
        
        /// <summary>
        /// Builds a stable identity string for an optional predicate, suitable for inclusion in a cache key.
        /// Two distinct delegate instances (even with logically identical bodies) produce distinct identities,
        /// while the same delegate instance always produces the same identity. A missing predicate produces a
        /// fixed "none" marker so that "no predicate" never accidentally collides with "some predicate" whose
        /// identity happens to be empty.
        /// </summary>
        /// <typeparam name="T">The fact type the predicate operates on.</typeparam>
        /// <param name="predicate">The optional predicate to identify.</param>
        /// <returns>A stable, collision-resistant identity string for the predicate.</returns>
        public static string PredicateIdentity<T>(Func<T, bool>? predicate)
        {
            return predicate != null ? predicate.Method.MethodHandle.Value.ToString() : "none";
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
           bool isSelfFilter = false,
           [CallerMemberName] string methodName = "")
        {
            var methodPointer = joinCondition.Method.MethodHandle.Value;
            string mode = isSelfFilter ? "self" : "join";

            // Generate a unique cache key that includes all factors affecting join semantics
            // This prevents false cache hits when conditions differ
            string cacheKey = $"{methodName}<{typeof(T).Name}>({name}:{mode}):{alpha.GetHashCode()}:{methodPointer}";

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
        /// Builds a deterministic cache signature for an anchored <see cref="AggregationGroupNode"/>, 
        /// allowing <see cref="BetaNodeRegistry"/> to share identical aggregation nodes across rules.
        /// </summary>
        /// <param name="alpha">The right-hand <see cref="AlphaMemory"/> supplying facts to aggregate.</param>
        /// <param name="joinCondition">The optional join predicate matching right-hand facts to left-hand tokens, or <see langword="null"/> if
        /// unfiltered.</param>
        /// <param name="aggregatorKind">A descriptor identifying the aggregation operation and return type (e.g., <c>"Count"</c>, <c>"Sum:Int32"</c>,
        /// <c>"Average:Decimal"</c>).</param>
        /// <param name="selector">The optional value-selector delegate used by operations like <c>Sum</c>, <c>Average</c>, <c>Min</c>, or <c>Max</c>,
        /// or <see langword="null"/> for <c>Count</c>.</param>
        /// <param name="outputVariableName">The token alias under which the computed aggregation result is stored.</param>
        /// <returns>A unique cache key string encoding the alpha memory, delegates, operation kind, and output alias.</returns>
        public static string GenerateAggregationSignature(
            AlphaMemory alpha,
            Delegate? joinCondition,
            string aggregatorKind,
            Delegate? selector,
            string outputVariableName)
        {
            var joinPtr = joinCondition?.Method.MethodHandle.Value ?? IntPtr.Zero;
            var selectorPtr = selector?.Method.MethodHandle.Value ?? IntPtr.Zero;

            return $"Group({alpha.GetHashCode()}):join={joinPtr}:agg={aggregatorKind}:sel={selectorPtr}:as={outputVariableName}";
        }

        /// <summary>
        /// Builds a deterministic cache signature for a standalone <see cref="GroupByNode{TRightFact, TKey}"/>, 
        /// allowing <see cref="BetaNodeRegistry"/> to share identical bucketed aggregations across rules.
        /// </summary>
        /// <param name="alpha">The <see cref="AlphaMemory"/> supplying facts to partition into buckets.</param>
        /// <param name="keySelector">The delegate that extracts the grouping key from each incoming fact.</param>
        /// <param name="filter">The optional single-fact predicate applied before bucketing, or <see langword="null"/> if all facts in
        /// <paramref name="alpha"/> are included.</param>
        /// <param name="aggregatorKind">A descriptor identifying the aggregation operation and return type (e.g., <c>"Count"</c>, <c>"Min:Int32"</c>,
        /// <c>"Max:Int32"</c>).</param>
        /// <param name="selector">The optional value-selector delegate for numeric aggregations, or <see langword="null"/> for <c>Count</c>.</param>
        /// <param name="keyName">The token alias under which each bucket's distinct grouping key is stored.</param>
        /// <param name="valueName">The token alias under which each bucket's computed aggregate value is stored.</param>
        /// <returns>A unique cache key string encoding the alpha memory, key selector, filter, operation kind, and output aliases.</returns>
        public static string GenerateGroupBySignature(
            AlphaMemory alpha,
            Delegate keySelector,
            Delegate? filter,
            string aggregatorKind,
            Delegate? selector,
            string keyName,
            string valueName)
        {
            var keyPtr = keySelector.Method.MethodHandle.Value;
            var filterPtr = filter?.Method.MethodHandle.Value ?? IntPtr.Zero;
            var selectorPtr = selector?.Method.MethodHandle.Value ?? IntPtr.Zero;

            return $"GroupBy({alpha.GetHashCode()}):key={keyPtr}:filter={filterPtr}:agg={aggregatorKind}:sel={selectorPtr}:as=({keyName},{valueName})";
        }


        /// <summary>
        /// Generate a cache key for the given filter predicate.  The predicates Method Handle is used along
        /// with the calling method name.
        /// </summary>
        /// <param name="filter">The predicate to generate the key for</param>
        /// <param name="methodName">The calling method name</param>
        /// <returns>The signature string</returns>
        public static string GenerateFilterSignature(
            Func<Token, bool> filter,
            [CallerMemberName] string methodName = "")
        {
            var methodPointer = filter.Method.MethodHandle.Value;
            return $"{methodName}:{methodPointer}";
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


        private static string CanonicalizeExpression(Expression expr)
        {
            var visitor = new CanonicalExpressionVisitor();
            visitor.Visit(expr);
            return visitor.GetSignature();
        }

        private sealed class CanonicalExpressionVisitor : ExpressionVisitor
        {
            private readonly StringBuilder _sb = new();

            public string GetSignature() => _sb.ToString();

            protected override Expression VisitLambda<T>(Expression<T> node)
            {
                _sb.Append("(=>");
                Visit(node.Body);
                _sb.Append(')');
                return node;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                // Normalize parameter names so 'f => f.Count > 10' 
                // matches 'x => x.Count > 10'
                _sb.Append($"$param:{node.Type.Name}");
                return node;
            }

            protected override Expression VisitBinary(BinaryExpression node)
            {
                _sb.Append('(');
                Visit(node.Left);
                _sb.Append($" {node.NodeType} ");
                Visit(node.Right);
                _sb.Append(')');
                return node;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                // Evaluate captured closure variables (e.g., local threshold variables)
                if (node.Expression is ConstantExpression constantExpr)
                {
                    object? container = constantExpr.Value;
                    object? value = node.Member switch
                    {
                        FieldInfo field => field.GetValue(container),
                        PropertyInfo prop => prop.GetValue(container),
                        _ => null
                    };
                    _sb.Append($"[Const:{value ?? "null"}]");
                    return node;
                }

                Visit(node.Expression);
                _sb.Append($".{node.Member.Name}");
                return node;
            }

            protected override Expression VisitConstant(ConstantExpression node)
            {
                _sb.Append($"[Const:{node.Value ?? "null"}]");
                return node;
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Object != null)
                {
                    Visit(node.Object);
                    _sb.Append('.');
                }
                _sb.Append($"{node.Method.Name}(");
                for (int i = 0; i < node.Arguments.Count; i++)
                {
                    if (i > 0) _sb.Append(',');
                    Visit(node.Arguments[i]);
                }
                _sb.Append(')');
                return node;
            }
        }
    }
}