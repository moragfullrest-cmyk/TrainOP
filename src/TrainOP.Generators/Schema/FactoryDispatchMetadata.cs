using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves factory caller-chain identity (ctor key + upstream station count) for extension dispatch.
    /// </summary>
    internal static class FactoryDispatchMetadata
    {
        /// <summary>
        /// Resolves dispatch identity from exported schema when present; otherwise from the factory body.
        /// </summary>
        public static bool TryResolve(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            out string callerChainKey,
            out int stationCount)
        {
            callerChainKey = string.Empty;
            stationCount = 0;
            if (factoryMethod == null || compilation == null)
            {
                return false;
            }

            if (ExternalRouteSchemaResolver.TryResolve(factoryMethod, compilation, out ExternalRouteSchema schema)
                && schema.HasDispatchIdentity)
            {
                callerChainKey = schema.CallerChainKey;
                stationCount = schema.StationCount;
                return true;
            }

            return TryResolveFromBody(factoryMethod, compilation, out callerChainKey, out stationCount);
        }

        /// <summary>
        /// Resolves dispatch identity by analyzing the factory method body in the current compilation.
        /// </summary>
        public static bool TryResolveFromBody(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            out string callerChainKey,
            out int stationCount)
        {
            return TryResolveFromBody(
                factoryMethod,
                compilation,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                out callerChainKey,
                out stationCount);
        }

        private static bool TryResolveFromBody(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            HashSet<IMethodSymbol> visiting,
            out string callerChainKey,
            out int stationCount)
        {
            callerChainKey = string.Empty;
            stationCount = 0;
            if (factoryMethod == null
                || compilation == null
                || !StationSyntaxHelper.IsTrainRoute(factoryMethod.ReturnType)
                || !visiting.Add(factoryMethod))
            {
                return false;
            }

            try
            {
                foreach (var reference in factoryMethod.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is not MethodDeclarationSyntax methodDeclaration
                        || !compilation.ContainsSyntaxTree(methodDeclaration.SyntaxTree))
                    {
                        continue;
                    }

                    var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
                    foreach (var expression in CollectReturnPathExpressions(methodDeclaration))
                    {
                        foreach (var leaf in ExpandReturnPathLeaves(expression))
                        {
                            if (TryResolveFromReturnExpression(
                                leaf,
                                semanticModel,
                                compilation,
                                visiting,
                                factoryMethod.Name,
                                out callerChainKey,
                                out stationCount))
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            finally
            {
                visiting.Remove(factoryMethod);
            }
        }

        private static bool TryResolveFromReturnExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            Compilation compilation,
            HashSet<IMethodSymbol> visiting,
            string factoryMemberName,
            out string callerChainKey,
            out int stationCount)
        {
            callerChainKey = string.Empty;
            stationCount = 0;
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                return false;
            }

            if (!RouteChainWalker.TryBuildChainEndingAt(expression, semanticModel, out var chain)
                || chain == null)
            {
                return false;
            }

            return TryResolveFromChain(
                chain,
                compilation,
                visiting,
                factoryMemberName,
                out callerChainKey,
                out stationCount);
        }

        private static bool TryResolveFromChain(
            RouteChain chain,
            Compilation compilation,
            HashSet<IMethodSymbol> visiting,
            string factoryMemberName,
            out string callerChainKey,
            out int stationCount)
        {
            callerChainKey = string.Empty;
            stationCount = 0;
            var anchor = chain.Anchor;
            if (anchor == null)
            {
                return false;
            }

            if (anchor.Kind == RouteChainAnchorKind.ObjectCreation
                || anchor.Kind == RouteChainAnchorKind.LocalVariable)
            {
                var memberName = string.IsNullOrEmpty(factoryMemberName)
                    ? anchor.ContainingMethod?.Name
                    : factoryMemberName;
                callerChainKey = CallerChainKeyBuilder.BuildFromLocation(anchor.Location, memberName);
                stationCount = chain.Stations.Length;
                return !string.IsNullOrEmpty(callerChainKey);
            }

            if ((anchor.Kind == RouteChainAnchorKind.MethodInvocation
                    || anchor.Kind == RouteChainAnchorKind.FactorySchema)
                && anchor.FactoryMethod != null)
            {
                if (!TryResolveNested(
                    anchor.FactoryMethod,
                    compilation,
                    visiting,
                    out callerChainKey,
                    out var upstreamCount))
                {
                    return false;
                }

                stationCount = upstreamCount + chain.Stations.Length;
                return !string.IsNullOrEmpty(callerChainKey);
            }

            return false;
        }

        private static bool TryResolveNested(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            HashSet<IMethodSymbol> visiting,
            out string callerChainKey,
            out int stationCount)
        {
            callerChainKey = string.Empty;
            stationCount = 0;

            if (ExternalRouteSchemaResolver.TryResolve(factoryMethod, compilation, out ExternalRouteSchema schema)
                && schema.HasDispatchIdentity)
            {
                callerChainKey = schema.CallerChainKey;
                stationCount = schema.StationCount;
                return true;
            }

            return TryResolveFromBody(factoryMethod, compilation, visiting, out callerChainKey, out stationCount);
        }

        private static IEnumerable<ExpressionSyntax> CollectReturnPathExpressions(
            MethodDeclarationSyntax methodDeclaration)
        {
            if (methodDeclaration.ExpressionBody?.Expression != null)
            {
                yield return methodDeclaration.ExpressionBody.Expression;
                yield break;
            }

            if (methodDeclaration.Body == null)
            {
                yield break;
            }

            foreach (var node in methodDeclaration.Body.DescendantNodes())
            {
                if (node is ReturnStatementSyntax returnStatement
                    && returnStatement.Expression != null)
                {
                    yield return returnStatement.Expression;
                }
            }
        }

        private static IEnumerable<ExpressionSyntax> ExpandReturnPathLeaves(ExpressionSyntax expression)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                yield break;
            }

            if (expression is ConditionalExpressionSyntax conditional)
            {
                foreach (var leaf in ExpandReturnPathLeaves(conditional.WhenTrue))
                {
                    yield return leaf;
                }

                foreach (var leaf in ExpandReturnPathLeaves(conditional.WhenFalse))
                {
                    yield return leaf;
                }

                yield break;
            }

            if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                foreach (var leaf in ExpandReturnPathLeaves(binary.Left))
                {
                    yield return leaf;
                }

                foreach (var leaf in ExpandReturnPathLeaves(binary.Right))
                {
                    yield return leaf;
                }

                yield break;
            }

            if (expression is SwitchExpressionSyntax switchExpression)
            {
                foreach (var arm in switchExpression.Arms)
                {
                    foreach (var leaf in ExpandReturnPathLeaves(arm.Expression))
                    {
                        yield return leaf;
                    }
                }

                yield break;
            }

            yield return expression;
        }
    }
}
