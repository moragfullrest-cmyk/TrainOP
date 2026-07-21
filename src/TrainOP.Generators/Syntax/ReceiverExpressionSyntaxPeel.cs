using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace TrainOP.Generators
{
    /// <summary>
    /// Peels transparent syntactic wrappers around TrainRoute chain receivers.
    /// </summary>
    /// <remarks>
    /// Transparent wrappers are parentheses, null-forgiving <c>!</c>, casts, <c>await</c>,
    /// and a single-argument <c>Task.FromResult</c> / <c>Task.FromResult&lt;T&gt;</c> used with <c>await</c>.
    /// Conditional, coalescing, switch, and other method invocations are not transparent.
    /// </remarks>
    internal static class ReceiverExpressionSyntaxPeel
    {
        /// <summary>
        /// Peels transparent wrappers inward to the core expression.
        /// </summary>
        public static ExpressionSyntax UnwrapTransparent(ExpressionSyntax expression)
        {
            while (expression != null)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfix
                    && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfix.Operand;
                    continue;
                }

                if (expression is CastExpressionSyntax cast)
                {
                    expression = cast.Expression;
                    continue;
                }

                if (expression is AwaitExpressionSyntax awaitExpression)
                {
                    expression = awaitExpression.Expression;
                    continue;
                }

                if (TryGetTaskFromResultArgument(expression, out var fromResultArgument))
                {
                    expression = fromResultArgument;
                    continue;
                }

                break;
            }

            return expression;
        }

        /// <summary>
        /// Walks outward through transparent wrappers (paren / <c>!</c> / cast / <c>await</c> /
        /// <c>Task.FromResult</c>) to the outermost equivalent expression.
        /// </summary>
        public static ExpressionSyntax WrapTransparentOutermost(ExpressionSyntax expression)
        {
            while (expression != null)
            {
                var parent = expression.Parent;

                if (parent is ParenthesizedExpressionSyntax parenthesized
                    && ReferenceEquals(parenthesized.Expression, expression))
                {
                    expression = parenthesized;
                    continue;
                }

                if (parent is PostfixUnaryExpressionSyntax postfix
                    && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)
                    && ReferenceEquals(postfix.Operand, expression))
                {
                    expression = postfix;
                    continue;
                }

                if (parent is CastExpressionSyntax cast
                    && ReferenceEquals(cast.Expression, expression))
                {
                    expression = cast;
                    continue;
                }

                if (parent is AwaitExpressionSyntax awaitExpression
                    && ReferenceEquals(awaitExpression.Expression, expression))
                {
                    expression = awaitExpression;
                    continue;
                }

                if (TryClimbTaskFromResultArgument(expression, out var fromResultInvocation))
                {
                    expression = fromResultInvocation;
                    continue;
                }

                break;
            }

            return expression;
        }

        /// <summary>
        /// Determines whether <paramref name="expression"/> is <c>Task.FromResult(...)</c> and returns its argument.
        /// </summary>
        private static bool TryGetTaskFromResultArgument(
            ExpressionSyntax expression,
            out ExpressionSyntax argument)
        {
            argument = null;

            if (expression is not InvocationExpressionSyntax invocation
                || invocation.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            if (!IsTaskFromResultInvocation(invocation))
            {
                return false;
            }

            argument = invocation.ArgumentList.Arguments[0].Expression;
            return argument != null;
        }

        /// <summary>
        /// Climbs from a <c>Task.FromResult</c> argument expression to the enclosing invocation.
        /// </summary>
        private static bool TryClimbTaskFromResultArgument(
            ExpressionSyntax expression,
            out InvocationExpressionSyntax fromResultInvocation)
        {
            fromResultInvocation = null;

            if (expression.Parent is not ArgumentSyntax argument
                || argument.Parent is not ArgumentListSyntax argumentList
                || argumentList.Parent is not InvocationExpressionSyntax invocation
                || argumentList.Arguments.Count != 1
                || !ReferenceEquals(argumentList.Arguments[0], argument))
            {
                return false;
            }

            if (!IsTaskFromResultInvocation(invocation))
            {
                return false;
            }

            fromResultInvocation = invocation;
            return true;
        }

        /// <summary>
        /// Determines whether an invocation is <c>Task.FromResult</c> or <c>Task.FromResult&lt;T&gt;</c>.
        /// </summary>
        private static bool IsTaskFromResultInvocation(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var methodName = memberAccess.Name switch
            {
                GenericNameSyntax genericName => genericName.Identifier.ValueText,
                IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                _ => null
            };

            if (!string.Equals(methodName, "FromResult", StringComparison.Ordinal))
            {
                return false;
            }

            return IsTaskTypeExpression(memberAccess.Expression);
        }

        /// <summary>
        /// Determines whether an expression refers to <c>Task</c> (simple or qualified name).
        /// </summary>
        private static bool IsTaskTypeExpression(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            if (expression is IdentifierNameSyntax identifier)
            {
                return string.Equals(identifier.Identifier.ValueText, "Task", StringComparison.Ordinal);
            }

            if (expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name is IdentifierNameSyntax typeName
                && string.Equals(typeName.Identifier.ValueText, "Task", StringComparison.Ordinal))
            {
                return true;
            }

            if (expression is QualifiedNameSyntax qualified
                && qualified.Right is IdentifierNameSyntax qualifiedRight
                && string.Equals(qualifiedRight.Identifier.ValueText, "Task", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }
    }
}
