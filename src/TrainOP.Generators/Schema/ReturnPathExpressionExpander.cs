using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace TrainOP.Generators
{
    /// <summary>
    /// Expands conditional, coalesce, and switch expressions into per-branch return expressions.
    /// Shared by handler return inference, factory dispatch, and factory path simulation.
    /// </summary>
    internal static class ReturnPathExpressionExpander
    {
        /// <summary>
        /// Yields leaf expressions after peeling transparent casts and expanding
        /// <c>?:</c>, <c>??</c>, and switch expression arms.
        /// </summary>
        public static IEnumerable<ExpressionSyntax> Expand(ExpressionSyntax expression)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                yield break;
            }

            if (expression is ConditionalExpressionSyntax conditional)
            {
                foreach (var expanded in Expand(conditional.WhenTrue))
                {
                    yield return expanded;
                }

                foreach (var expanded in Expand(conditional.WhenFalse))
                {
                    yield return expanded;
                }

                yield break;
            }

            if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                foreach (var expanded in Expand(binary.Left))
                {
                    yield return expanded;
                }

                foreach (var expanded in Expand(binary.Right))
                {
                    yield return expanded;
                }

                yield break;
            }

            if (expression is SwitchExpressionSyntax switchExpression)
            {
                foreach (var arm in switchExpression.Arms)
                {
                    foreach (var expanded in Expand(arm.Expression))
                    {
                        yield return expanded;
                    }
                }

                yield break;
            }

            yield return expression;
        }
    }
}
