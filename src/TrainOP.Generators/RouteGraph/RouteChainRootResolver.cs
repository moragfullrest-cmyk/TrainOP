using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Backward root walk from a chain endpoint: <c>new</c> / local origin / factory / peel.
    /// </summary>
    /// <remarks>
    /// Flattened from <see cref="RouteChainWalker.TryFindChainRootEndingAt"/> (post-Z0 nesting extract).
    /// Local-origin lookup uses <see cref="RouteOriginWindow"/>.
    /// </remarks>
    internal static class RouteChainRootResolver
    {
        /// <summary>
        /// Walks backward from <paramref name="endpoint"/> through Station / ServiceStation
        /// receivers until a resolvable chain root is found.
        /// </summary>
        public static bool TryFindChainRootEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind,
            out IMethodSymbol factoryMethod,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            root = null;
            anchorKind = default;
            factoryMethod = null;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            var current = endpoint;

            while (current != null)
            {
                current = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
                if (current == null)
                {
                    return false;
                }

                if (TryRootFromCreation(current, semanticModel, out root, out anchorKind))
                {
                    return true;
                }

                if (current is IdentifierNameSyntax identifier
                    && TryRootFromLocalOrigin(
                        identifier,
                        semanticModel,
                        out root,
                        out anchorKind,
                        out factoryMethod,
                        out initialWagons))
                {
                    return true;
                }

                if (TryResolveFactoryRoot(
                        current,
                        semanticModel,
                        out root,
                        out anchorKind,
                        out factoryMethod,
                        out initialWagons))
                {
                    return true;
                }

                if (!TryGetChainMethodReceiver(current, out var receiver))
                {
                    return false;
                }

                current = receiver;
            }

            return false;
        }

        /// <summary>
        /// Resolves a bare / invocation factory root via <see cref="FactoryCallMaterializer"/>.
        /// </summary>
        public static bool TryResolveFactoryRoot(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind,
            out IMethodSymbol factoryMethod,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            root = null;
            anchorKind = default;
            factoryMethod = null;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            if (!FactoryCallMaterializer.TryMaterialize(current, semanticModel, out var factoryCall))
            {
                return false;
            }

            root = factoryCall.Root;
            anchorKind = factoryCall.ToLegacyAnchorKind();
            factoryMethod = factoryCall.FactoryMethod;
            initialWagons = factoryCall.InitialWagons;
            return true;
        }

        /// <summary>
        /// If <paramref name="expression"/> is a Station / ServiceStation invocation,
        /// returns its receiver expression.
        /// </summary>
        public static bool TryGetChainMethodReceiver(
            ExpressionSyntax expression,
            out ExpressionSyntax receiver)
        {
            receiver = null;

            if (expression is not InvocationExpressionSyntax invocation
                || !StationSyntaxHelper.MatchesStationOrServiceStationShape(invocation, out var memberAccess))
            {
                return false;
            }

            receiver = memberAccess.Expression;
            return receiver != null;
        }

        /// <summary>
        /// Determines whether <paramref name="current"/> matches the chain endpoint (raw or unwrapped forms).
        /// </summary>
        public static bool MatchesChainEndpoint(
            ExpressionSyntax current,
            ExpressionSyntax endpoint,
            ExpressionSyntax unwrappedEndpoint)
        {
            if (ReferenceEquals(current, endpoint)
                || ReferenceEquals(current, unwrappedEndpoint))
            {
                return true;
            }

            var unwrappedCurrent = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
            if (ReferenceEquals(unwrappedCurrent, endpoint)
                || ReferenceEquals(unwrappedCurrent, unwrappedEndpoint))
            {
                return true;
            }

            var outermostCurrent = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
            var outermostEndpoint = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(endpoint);
            return ReferenceEquals(outermostCurrent, outermostEndpoint);
        }

        /// <summary>
        /// Finds a fluent chain root (<c>new</c> / factory) without resolving local-variable origins
        /// (avoids Collect ↔ EndingAt recursion).
        /// </summary>
        public static bool TryFindFluentChainRootWithoutLocalOrigins(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out ExpressionSyntax root)
        {
            root = null;
            var current = endpoint;

            while (current != null)
            {
                current = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
                if (current == null)
                {
                    return false;
                }

                if (TryRootFromCreation(current, semanticModel, out root, out _))
                {
                    return true;
                }

                if (TryResolveFactoryRoot(
                        current,
                        semanticModel,
                        out root,
                        out _,
                        out _,
                        out _))
                {
                    return true;
                }

                if (!TryGetChainMethodReceiver(current, out var receiver))
                {
                    return false;
                }

                current = receiver;
            }

            return false;
        }

        private static bool TryRootFromCreation(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind)
        {
            root = null;
            anchorKind = default;

            if (current is not ObjectCreationExpressionSyntax objectCreation
                || !StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel))
            {
                return false;
            }

            root = objectCreation;
            anchorKind = RouteChainAnchorKind.ObjectCreation;
            return true;
        }

        /// <summary>
        /// Local-origin root: priority <c>new</c> → factory (SL-2) → join assign (C-10/C-11).
        /// </summary>
        private static bool TryRootFromLocalOrigin(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind,
            out IMethodSymbol factoryMethod,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            root = null;
            anchorKind = default;
            factoryMethod = null;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            if (!RouteOriginWindow.TryGetPrecedingTrainRouteOriginAssignment(
                    identifier,
                    semanticModel,
                    out var originExpression,
                    out _))
            {
                return false;
            }

            if (originExpression is ObjectCreationExpressionSyntax)
            {
                root = identifier;
                anchorKind = RouteChainAnchorKind.LocalVariable;
                return true;
            }

            // SL-2a/2b: bare factory origin on a local (MethodInvocation / FactorySchema).
            if (TryResolveFactoryRoot(
                    originExpression,
                    semanticModel,
                    out _,
                    out anchorKind,
                    out factoryMethod,
                    out initialWagons))
            {
                root = identifier;
                return true;
            }

            return TryRootFromJoinAssign(
                identifier,
                originExpression,
                semanticModel,
                out root,
                out anchorKind,
                out factoryMethod,
                out initialWagons);
        }

        private static bool TryRootFromJoinAssign(
            IdentifierNameSyntax identifier,
            ExpressionSyntax originExpression,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind,
            out IMethodSymbol factoryMethod,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            root = null;
            anchorKind = default;
            factoryMethod = null;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            if (!RouteAnchorDetector.TryValidateLocalAssignJoin(
                    originExpression,
                    semanticModel,
                    out var joinValidation)
                || !joinValidation.CanMerge)
            {
                return false;
            }

            root = identifier;
            if (RouteAnchorDetector.TryGetSharedFactoryFromJoinArms(
                    originExpression,
                    semanticModel,
                    out factoryMethod,
                    out anchorKind,
                    out initialWagons))
            {
                if (initialWagons.IsDefaultOrEmpty)
                {
                    initialWagons = joinValidation.MergedTerminalWagons;
                }

                return true;
            }

            anchorKind = RouteChainAnchorKind.LocalVariable;
            initialWagons = joinValidation.MergedTerminalWagons;
            return true;
        }
    }
}
