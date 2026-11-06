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
using System.Text;
using System.Threading.Tasks;

namespace ReteEngine
{
    /// <summary>
    /// A interface for building group operations in a Rete network. It allows the user to define conditions for grouping facts and specify 
    /// aggregation operations such as Sum, Count, and Average on the grouped data. The interface is generic and works with an initial fact 
    /// type TInitial, while the specific type of facts being grouped is determined by the implementation.
    /// </summary>
    /// <typeparam name="TInitial">The type of the initial fact in the Rete network.</typeparam>
    public interface IGroupBuilder<TInitial>
    {
        // The Where method needs to capture the specific type being passed by the converter
        IGroupBuilder<TInitial> Where<T>(Func<Token, T, bool> joinCondition);

        ReteBuilder<TInitial> Sum<T>(Func<T, int> selector, string asVariableName);
        ReteBuilder<TInitial> Sum<T>(Func<T, decimal> selector, string asVariableName);
        ReteBuilder<TInitial> Count<T>(string asVariableName);
        ReteBuilder<TInitial> Average<T>(Func<T, decimal> selector, string asVariableName);
    }

    /// <summary>
    /// The GroupBuilder class is a concrete implementation of the IGroupBuilder interface, providing functionality to define grouping 
    /// conditions and aggregation operations in a Rete network. It allows users to specify how facts should be grouped based on a join 
    /// condition and perform aggregations like Sum, Count, and Average on the grouped data. The class is generic, with TInitial 
    /// representing the type of the initial fact and TRightFact representing the type of facts being grouped.
    /// </summary>
    /// <typeparam name="TInitial">The type of the initial fact in the Rete network.</typeparam>
    /// <typeparam name="TRightFact">The type of the facts being grouped in the Rete network.</typeparam>
    public class GroupBuilder<TInitial, TRightFact> : IGroupBuilder<TInitial>
    {
        private readonly ReteBuilder<TInitial> _reteBuilder;
        private readonly AlphaMemory _alphaMemory; 
        private readonly string _alphaMemoryName;
        private Delegate? _storedJoinCondition;

        public GroupBuilder(ReteBuilder<TInitial> reteBuilder, AlphaMemory alphaMemory, string alphaMemoryName)
        {
            _reteBuilder = reteBuilder;
            _alphaMemory = alphaMemory;
            _alphaMemoryName = alphaMemoryName;
        }

        IGroupBuilder<TInitial> IGroupBuilder<TInitial>.Where<T>(Func<Token, T, bool> joinCondition)
        {
            if (typeof(T) != typeof(TRightFact))
            {
                throw new InvalidOperationException($"Type mismatch: Group is configured for {typeof(TRightFact).Name} but Where clause requested {typeof(T).Name}.");
            }

            _storedJoinCondition = joinCondition;
            return this;
        }

        // Handled with a clean decimal return type matching LINQ's expectations
        ReteBuilder<TInitial> IGroupBuilder<TInitial>.Sum<T>(Func<T, decimal> selector, string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
            {
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Sum requested {typeof(T).Name}.");
            }
            var typedSelector = (Func<TRightFact, decimal>)(object)selector;
            // 1. We wrap your exact request into a cleanly typed collection function
            Func<IEnumerable<TRightFact>, decimal> typedSumExpr = elements => elements.Sum(typedSelector);

            // 2. We bridge it to the node by casting the raw object list back to T
            Func<IEnumerable<object>, object> nodeAggregator = rawList =>
            {
                var typedCollection = rawList.Cast<TRightFact>();
                return typedSumExpr(typedCollection); // Returns decimal, safely boxed to object here
            };

            return CompileAggregationNode(nodeAggregator, asVariableName);
        }

        // Overload specifically for int properties
        ReteBuilder<TInitial> IGroupBuilder<TInitial>.Sum<T>(Func<T, int> selector, string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
            {
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Sum requested {typeof(T).Name}.");
            }
            var typedSelector = (Func<TRightFact, int>)(object)selector;
            Func<IEnumerable<TRightFact>, int> typedSumExpr = elements => elements.Sum(typedSelector);

            Func<IEnumerable<object>, object> nodeAggregator = rawList =>
            {
                var typedCollection = rawList.Cast<TRightFact>();
                return typedSumExpr(typedCollection); // Returns int, safely boxed to object here
            };

            return CompileAggregationNode(nodeAggregator, asVariableName);
        }
        ReteBuilder<TInitial> IGroupBuilder<TInitial>.Count<T>(string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
            {
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Count requested {typeof(T).Name}.");
            }
            // Explicit expression signature so LINQ matches accurately
            Func<IEnumerable<TRightFact>, int> typedCountExpr = elements => elements.Count();

            // The late-boxing adapter for your core agnostic Rete node
            Func<IEnumerable<object>, object> nodeAggregator = rawList =>
            {
                var typedCollection = rawList.Cast<TRightFact>();
                return typedCountExpr(typedCollection); // Safely boxes the int to object
            };

            return CompileAggregationNode(nodeAggregator, asVariableName);
        }
        // Overload for decimal properties
        ReteBuilder<TInitial> IGroupBuilder<TInitial>.Average<T>(Func<T, decimal> selector, string asVariableName)
        {
            if (typeof(T) != typeof(TRightFact))
            {
                throw new InvalidOperationException($"Type mismatch: Group is for {typeof(TRightFact).Name} but Count requested {typeof(T).Name}.");
            }
            var typedSelector = (Func<TRightFact, decimal>)(object)selector;
            Func<IEnumerable<TRightFact>, decimal> typedAvgExpr = elements =>
                elements.Any() ? elements.Average(typedSelector) : 0m;

            Func<IEnumerable<object>, object> nodeAggregator = rawList =>
            {
                var typedCollection = rawList.Cast<TRightFact>();
                return typedAvgExpr(typedCollection); // Safely boxes decimal to object
            };

            return CompileAggregationNode(nodeAggregator, asVariableName);
        }

        private ReteBuilder<TInitial> CompileAggregationNode(Func<IEnumerable<object>, object> aggregator, string variableName)
        {
            // Fetch your parent node reference
            var parentNode = _reteBuilder.GetLastNode();
            if (parentNode == null)
            {
                throw new InvalidOperationException("Cannot add a Group node without a preceding memory node.");
            }

            // Map the join condition down to an object delegate
            Func<Token, object, bool> wrappedJoin = (token, fact) =>
            {
                // 1. Verify that 'fact' is actually an instance of your domain entity (e.g. GInventory)
                // If it is, C# automatically casts it and assigns it to 'typedFact'
                if (fact is TRightFact typedFact)
                {
                    if (_storedJoinCondition is Func<Token, TRightFact, bool> typedCondition)
                    {
                        return typedCondition(token, typedFact); // Pass the safely-typed variable here!
                    }
                }

                // 2. If it's a primitive int, a token, or anything else, reject it safely without crashing
                return false;
            };
            /*
            Func<Token, object, bool> wrappedJoin = (token, fact) =>
            {
                if (_storedJoinCondition is Func<Token, TRightFact, bool> typedCondition)
                {
                    return typedCondition(token, (TRightFact)fact);
                }
                return false;
            };
            */

            var alphaMemory = _alphaMemory;

            // Your Rete Node remains 100% agnostic to math types!
            var groupNode = new AggregationGroupNode(
                (ILatentMemory)parentNode,
                _alphaMemory,
                wrappedJoin,
                variableName,
                aggregator
            );

            alphaMemory.AddSuccessor(groupNode);

            // Link up structural dependencies (Mirroring your And<T> workflow)
            if (parentNode is BetaMemory beta)
            {
                beta.AddSuccessor(groupNode);
            }
            else if (parentNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(groupNode);
            }

            var downstreamMemory = new BetaMemory();
            groupNode.AddSuccessor(downstreamMemory);

            _reteBuilder.SetLastNode(downstreamMemory);

            return _reteBuilder;
        }
    }

}
