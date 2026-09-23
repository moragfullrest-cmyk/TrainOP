using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves data-oriented station handler arguments (lambdas, method groups, local functions).
    /// </summary>
    internal static class StationHandlerResolver
    {
        /// <summary>
        /// Resolves a data-oriented handler argument to a method symbol in the current compilation.
        /// Supports lambdas, anonymous methods, and method groups / local functions with source declarations.
        /// </summary>
        public static bool TryResolveHandler(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out ResolvedHandler resolved)
        {
            resolved = null;
            if (expression == null || semanticModel == null)
            {
                return false;
            }

            expression = UnwrapHandlerExpression(expression);

            if (expression is ParenthesizedLambdaExpressionSyntax
                || expression is SimpleLambdaExpressionSyntax)
            {
                var lambdaSyntax = (LambdaExpressionSyntax)expression;
                var lambdaSymbol = semanticModel.GetSymbolInfo(lambdaSyntax).Symbol as IMethodSymbol;
                if (lambdaSymbol == null)
                {
                    return false;
                }

                resolved = new ResolvedHandler(
                    HandlerKind.Lambda,
                    lambdaSymbol,
                    GetAnonymousFunctionBody(lambdaSyntax),
                    lambdaSyntax.GetLocation(),
                    expression);
                return true;
            }

            if (expression is AnonymousMethodExpressionSyntax anonymousMethod)
            {
                var anonymousSymbol = semanticModel.GetSymbolInfo(anonymousMethod).Symbol as IMethodSymbol;
                if (anonymousSymbol == null)
                {
                    return false;
                }

                resolved = new ResolvedHandler(
                    HandlerKind.AnonymousMethod,
                    anonymousSymbol,
                    GetAnonymousFunctionBody(anonymousMethod),
                    anonymousMethod.GetLocation(),
                    expression);
                return true;
            }

            if (!TryResolveMethodGroup(expression, semanticModel, out var methodSymbol))
            {
                return false;
            }

            if (!IsInspectableInCompilation(methodSymbol, semanticModel.Compilation))
            {
                return false;
            }

            resolved = new ResolvedHandler(
                HandlerKind.MethodGroup,
                methodSymbol,
                TryGetDeclaredMethodBody(methodSymbol),
                methodSymbol.Locations.Length > 0
                    ? methodSymbol.Locations[0]
                    : expression.GetLocation(),
                expression);
            return true;
        }

        private static ExpressionSyntax UnwrapHandlerExpression(ExpressionSyntax expression)
        {
            while (expression != null)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is CastExpressionSyntax cast)
                {
                    expression = cast.Expression;
                    continue;
                }

                break;
            }

            return expression;
        }

        private static bool TryResolveMethodGroup(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out IMethodSymbol methodSymbol)
        {
            methodSymbol = null;
            var symbolInfo = semanticModel.GetSymbolInfo(expression);
            if (TryPickUniqueMethod(symbolInfo.Symbol, symbolInfo.CandidateSymbols, out methodSymbol))
            {
                return true;
            }

            if (expression is IdentifierNameSyntax
                || expression is MemberAccessExpressionSyntax
                || expression is GenericNameSyntax
                || expression is MemberBindingExpressionSyntax)
            {
                var members = semanticModel.GetMemberGroup(expression);
                return TryPickUniqueMethod(null, members, out methodSymbol);
            }

            return false;
        }

        private static bool TryPickUniqueMethod(
            ISymbol primary,
            System.Collections.Immutable.ImmutableArray<ISymbol> candidates,
            out IMethodSymbol methodSymbol)
        {
            methodSymbol = null;

            if (IsHandlerMethodCandidate(primary as IMethodSymbol))
            {
                methodSymbol = (IMethodSymbol)primary;
                return true;
            }

            if (candidates.IsDefaultOrEmpty)
            {
                return false;
            }

            IMethodSymbol chosen = null;
            foreach (var candidate in candidates)
            {
                if (!IsHandlerMethodCandidate(candidate as IMethodSymbol))
                {
                    continue;
                }

                var method = (IMethodSymbol)candidate;
                if (chosen == null)
                {
                    chosen = method;
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(chosen, method))
                {
                    methodSymbol = null;
                    return false;
                }
            }

            methodSymbol = chosen;
            return methodSymbol != null;
        }

        private static bool IsHandlerMethodCandidate(IMethodSymbol method)
        {
            if (method == null)
            {
                return false;
            }

            return method.MethodKind == MethodKind.Ordinary
                || method.MethodKind == MethodKind.LocalFunction
                || method.MethodKind == MethodKind.ExplicitInterfaceImplementation;
        }

        private static bool IsInspectableInCompilation(IMethodSymbol method, Compilation compilation)
        {
            if (method == null || compilation == null)
            {
                return false;
            }

            if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            {
                return false;
            }

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.SyntaxTree != null
                    && compilation.ContainsSyntaxTree(reference.SyntaxTree))
                {
                    return true;
                }
            }

            return false;
        }

        private static CSharpSyntaxNode GetAnonymousFunctionBody(AnonymousFunctionExpressionSyntax syntax)
        {
            return syntax?.Body;
        }

        private static CSharpSyntaxNode TryGetDeclaredMethodBody(IMethodSymbol methodSymbol)
        {
            foreach (var reference in methodSymbol.DeclaringSyntaxReferences)
            {
                var node = reference.GetSyntax();
                if (node is MethodDeclarationSyntax methodDeclaration)
                {
                    return (CSharpSyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
                }

                if (node is LocalFunctionStatementSyntax localFunction)
                {
                    return (CSharpSyntaxNode)localFunction.Body ?? localFunction.ExpressionBody;
                }
            }

            return null;
        }
    }
}
