using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteProgram
{
    using ReteCore;
    using System;
    using System.Linq;
    using System.Linq.Expressions;

    /// <summary>
    /// This class is responsible for analyzing a join expression provided by the user and extracting the key selector functions for both 
    /// sides of the join.  The Extract method takes a lambda expression representing the join condition 
    /// (e.g., (token, fact) => token.Property == fact.Property) and processes it to identify which part of the expression corresponds to 
    /// the Token and which part corresponds to the Fact. It then compiles these parts into Func delegates that can be used to extract the 
    /// join keys at runtime when processing tokens and facts in the Rete network. This allows for efficient indexing and matching of facts 
    /// based on the specified join condition.
    /// </summary>
    public class JoinKeyExtractor
    {
        /// <summary>
        /// Extracts the key selector functions from the provided join expression. The join expression must be a binary expression that compares 
        /// a property of the Token (first parameter) with a property of the Fact (second parameter) using the equality operator (==). The method 
        /// identifies which side of the expression corresponds to the Token and which side corresponds to the Fact, and then compiles each side 
        /// into a Func delegate that can be used to extract the join key at runtime. The resulting tuple contains two functions: one for extracting 
        /// the key from the Token and one for extracting the key from the Fact. These functions can be used to efficiently index and match facts in 
        /// the Rete network based on the specified join condition. If the expression does not meet the expected format (e.g., it is not an equality 
        /// comparison or does not involve both a Token and a Fact), the method throws an exception to indicate that the join expression is not supported 
        /// or is invalid.
        /// </summary>
        /// <param name="joinExpr">The expression to extract the keys from.</param>
        /// <returns>The tuple of the left and right keys extracted from the given expression.</returns>
        /// <exception cref="NotSupportedException">Thrown when the expression is not well-formed for extraction</exception>
        /// <exception cref="Exception">Thrown when the parameters are not well-formed.</exception>
        public (Func<Token, object> LeftKey, Func<object, object> RightKey) Extract(Expression<Func<Token, object, bool>> joinExpr)
        {
            // 1. Ensure the root is an '==' comparison
            if (joinExpr.Body is not BinaryExpression binary || binary.NodeType != ExpressionType.Equal)
            {
                throw new NotSupportedException("Only equality joins (==) can be indexed.");
            }

            // 2. Identify the parameters (token is index 0, fact is index 1)
            var tokenParam = joinExpr.Parameters[0];
            var factParam = joinExpr.Parameters[1];

            Expression leftPart = null;
            Expression rightPart = null;

            // 3. Determine which side of '==' belongs to which parameter
            if (IsParameterDependent(binary.Left, tokenParam) && IsParameterDependent(binary.Right, factParam))
            {
                leftPart = binary.Left;
                rightPart = binary.Right;
            }
            else if (IsParameterDependent(binary.Left, factParam) && IsParameterDependent(binary.Right, tokenParam))
            {
                leftPart = binary.Right;
                rightPart = binary.Left;
            }
            else
            {
                throw new Exception("Join expression must compare a property of Token with a property of Fact.");
            }

            // 4. Wrap and Compile into Funcs
            return (CompileSelector<Token>(leftPart, tokenParam),
                    CompileSelector<object>(rightPart, factParam));
        }

        /// <summary>
        /// A helper method to determine if a given expression depends on a specific parameter. This is used to identify which part 
        /// of the join expression corresponds to the Token and which part corresponds to the Fact. The method recursively checks if 
        /// the expression tree contains the specified parameter, which indicates that the expression is dependent on that parameter. 
        /// </summary>
        /// <param name="expr">The expression to analyze</param>
        /// <param name="param">The parameters of the given expression.</param>
        /// <returns></returns>
        private bool IsParameterDependent(Expression expr, ParameterExpression param)
        {
            // Recursively check if the expression tree uses the specified parameter
            if (expr is ParameterExpression p) return p == param;
            if (expr is MemberExpression m) return IsParameterDependent(m.Expression, param);
            if (expr is UnaryExpression u) return IsParameterDependent(u.Operand, param);
            return false;
        }

        /// <summary>
        /// A helper method to compile a given expression into a Func delegate that can be used to extract the join key at runtime. The 
        /// method takes an expression representing the property access for either the Token or the Fact and compiles it into a Func that 
        /// returns an object. This allows the extracted key to be used as a dictionary key for indexing in the Rete network.
        /// </summary>
        /// <typeparam name="T">The type of the input to the returned Func</typeparam>
        /// <param name="expr">The expression part of the Func.</param>
        /// <param name="param">The parameters to the expression.</param>
        /// <returns></returns>
        private Func<T, object> CompileSelector<T>(Expression expr, ParameterExpression param)
        {
            // Convert the expression to return 'object' so it fits our Dictionary key
            var converted = Expression.Convert(expr, typeof(object));
            return Expression.Lambda<Func<T, object>>(converted, param).Compile();
        }
    }
}
