using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Thin BuildChains facade: peel via <see cref="RouteChainPeel"/>, detect via
    /// <see cref="RouteAnchorDetector"/> / Parts materializers; origin window via
    /// <see cref="RouteOriginWindow"/>; root walk via <see cref="RouteChainRootResolver"/>.
    /// </summary>
    /// <remarks>
    /// Stage 4 BuildChains entry variants (same stage, different ingress):
    /// <list type="bullet">
    /// <item><see cref="TryBuildChainFromStationInvocation"/> — forward from a station (join / fork downstream)</item>
    /// <item><see cref="TryBuildChainEndingAt"/> — root → endpoint inclusive</item>
    /// <item><see cref="TryBuildFactoryExtensionChain"/> — factory-extension ending at endpoint</item>
    /// <item>Forward from anchor via <see cref="TryAdvanceChain"/> → <see cref="RouteChainPeel"/></item>
    /// </list>
    /// Prefer <see cref="BuildChainsStage"/> at call sites when folding setup.
    /// </remarks>
    internal static class RouteChainWalker
    {
        /// <summary>
        /// Attempts to detect a route chain anchor at the given syntax node.
        /// </summary>
        internal static bool TryDetectAnchorSite(
            SyntaxNode node,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            return RouteAnchorDetector.TryDetect(node, semanticModel, out anchor);
        }

        /// <summary>
        /// BuildChains variant: builds a <see cref="RouteChain"/> starting at an already-identified
        /// Station invocation (inclusive) and continuing through further fluent stations.
        /// </summary>
        internal static bool TryBuildChainFromStationInvocation(
            InvocationExpressionSyntax startStation,
            SemanticModel semanticModel,
            out RouteChain chain)
        {
            chain = null;

            if (startStation == null || semanticModel == null)
            {
                return false;
            }

            var stations = ImmutableArray.CreateBuilder<StationChainLink>();

            if (StationSyntaxHelper.IsCandidateServiceStationInvocation(startStation)
                && StationSyntaxHelper.TryGetDataServiceStationInvocation(
                    startStation,
                    semanticModel,
                    out var serviceStationName,
                    out var serviceHandlerLocation,
                    out var serviceHandlerBinding))
            {
                stations.Add(new StationChainLink(
                    serviceStationName,
                    serviceHandlerLocation,
                    serviceHandlerLocation,
                    serviceHandlerBinding,
                    startStation));
            }
            else if (StationSyntaxHelper.TryGetDataStationInvocation(
                startStation,
                semanticModel,
                out var stationName,
                out var handlerLocation,
                out var handlerBinding))
            {
                stations.Add(new StationChainLink(
                    stationName,
                    startStation.ArgumentList.Arguments[0].GetLocation(),
                    handlerLocation,
                    handlerBinding,
                    startStation));
            }
            else
            {
                return false;
            }

            var anchor = new RouteChainAnchor(
                RouteChainAnchorKind.BranchJoin,
                startStation,
                startStation.GetLocation(),
                GetContainingMethod(startStation, semanticModel));

            var current = (ExpressionSyntax)startStation;
            while (TryAdvanceChain(current, semanticModel, stations, out current, null)) ;

            chain = new RouteChain(anchor, stations.ToImmutable());
            return true;
        }

        /// <summary>
        /// Determines whether <paramref name="expression"/> is a bare user-defined factory invocation
        /// (e.g. <c>GetRoute()</c>) with no inline fluent stations at the call site.
        /// </summary>
        internal static bool IsBareUserDefinedFactoryInvocation(
            ExpressionSyntax expression,
            SemanticModel semanticModel)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                return false;
            }

            return RouteChainRootResolver.TryResolveFactoryRoot(
                expression,
                semanticModel,
                out _,
                out _,
                out _,
                out _);
        }

        /// <summary>
        /// Resolves a bare factory leaf into a zero-station chain seeded with factory terminals
        /// (for <c>?:</c> / <c>??</c> / <c>switch</c> join arms).
        /// </summary>
        internal static bool TryBuildBareFactoryBranch(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out RouteChain chain,
            out ChainSimulationResult simulation)
        {
            chain = null;
            simulation = null;
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null
                || semanticModel == null
                || !IsBareUserDefinedFactoryInvocation(expression, semanticModel)
                || !RouteChainRootResolver.TryResolveFactoryRoot(
                    expression,
                    semanticModel,
                    out var root,
                    out var anchorKind,
                    out var factoryMethod,
                    out var initialWagons)
                || factoryMethod == null)
            {
                return false;
            }

            if (!RouteFactoryResolver.TryResolve(
                    factoryMethod,
                    semanticModel.Compilation,
                    expression.GetLocation(),
                    out var terminalWagons,
                    out var diagnostics)
                || !diagnostics.IsDefaultOrEmpty)
            {
                return false;
            }

            var wagons = terminalWagons.IsDefault ? initialWagons : terminalWagons;
            var anchor = new RouteChainAnchor(
                anchorKind,
                root,
                root.GetLocation(),
                GetContainingMethod(root, semanticModel),
                factoryMethod,
                wagons);
            chain = new RouteChain(anchor, ImmutableArray<StationChainLink>.Empty);
            simulation = new ChainSimulationResult(
                wagons,
                hasUnknownReturn: false,
                ImmutableArray<Diagnostic>.Empty);
            return true;
        }

        /// <summary>
        /// BuildChains variant: builds a <see cref="RouteChain"/> from a known chain root forward until
        /// <paramref name="endpoint"/> (inclusive), without continuing past a fork into an outer join.
        /// </summary>
        /// <remarks>
        /// Unlike endpoint-based chain building, linear assembly accepts bare <c>new TrainRoute()</c> / local endpoints
        /// with zero stations, and does not require a local identifier to already be a Station receiver.
        /// </remarks>
        internal static bool TryBuildChainEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out RouteChain chain)
        {
            chain = null;

            var target = ReceiverExpressionSyntaxPeel.UnwrapTransparent(endpoint);
            if (target == null)
            {
                return false;
            }

            if (!TryFindChainRootEndingAt(
                    target,
                    semanticModel,
                    out var root,
                    out var anchorKind,
                    out var factoryMethod,
                    out var initialWagons))
            {
                return false;
            }

            var anchorLocation = RouteOriginWindow.ResolveAnchorLocation(root, semanticModel);
            var anchor = new RouteChainAnchor(
                anchorKind,
                root,
                anchorLocation,
                GetContainingMethod(root, semanticModel),
                factoryMethod,
                initialWagons);

            var stations = ImmutableArray.CreateBuilder<StationChainLink>();
            var current = root;

            while (!RouteChainRootResolver.MatchesChainEndpoint(current, endpoint, target))
            {
                if (!TryAdvanceChain(current, semanticModel, stations, out var next, null))
                {
                    return false;
                }

                current = next;
            }

            // Bare `return route`: fold statement-local stations registered before the endpoint.
            if (stations.Count == 0 && root is IdentifierNameSyntax localRoot)
            {
                FoldStatementLocalStationsIfBareReturn(localRoot, target, semanticModel, stations);
            }

            chain = new RouteChain(anchor, stations.ToImmutable());
            return true;
        }

        /// <summary>
        /// BuildChains variant: builds a factory extension chain ending at <paramref name="endpoint"/>.
        /// </summary>
        internal static bool TryBuildFactoryExtensionChain(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            Compilation compilation,
            out RouteChain chain,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            return ExtensionChainConnector.TryConnectEndingAt(
                endpoint,
                semanticModel,
                compilation,
                out chain,
                out diagnostics);
        }

        /// <summary>
        /// Walks backward from <paramref name="endpoint"/> through Station / ServiceStation
        /// receivers until a resolvable chain root is found.
        /// </summary>
        internal static bool TryFindChainRootEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind,
            out IMethodSymbol factoryMethod,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            return RouteChainRootResolver.TryFindChainRootEndingAt(
                endpoint,
                semanticModel,
                out root,
                out anchorKind,
                out factoryMethod,
                out initialWagons);
        }

        /// <summary>
        /// Advances along a route chain by resolving the next station or service-station invocation.
        /// </summary>
        internal static bool TryAdvanceChain(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            ImmutableArray<StationChainLink>.Builder stations,
            out ExpressionSyntax next,
            ImmutableArray<InvocationExpressionSyntax>.Builder chainedInvocations,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey = null)
        {
            return RouteChainPeel.TryAdvanceChain(
                current,
                semanticModel,
                stations,
                out next,
                chainedInvocations,
                stationSitesByKey);
        }

        /// <summary>
        /// Validates a local-assign join fork (arms merge) for statement-local tails.
        /// </summary>
        internal static bool TryValidateLocalAssignJoin(
            ExpressionSyntax forkExpression,
            SemanticModel semanticModel,
            out BranchRouteJoinValidation validation)
        {
            return RouteAnchorDetector.TryValidateLocalAssignJoin(
                forkExpression,
                semanticModel,
                out validation);
        }

        /// <summary>
        /// Determines whether the invocation is the receiver of a route handler member access.
        /// </summary>
        internal static bool IsFactoryChainReceiver(InvocationExpressionSyntax factoryInvocation)
        {
            return RouteAnchorDetector.IsFactoryChainReceiver(factoryInvocation);
        }

        /// <summary>
        /// Determines whether the identifier is the receiver of a route handler member access.
        /// </summary>
        internal static bool IsLocalVariableChainReceiver(IdentifierNameSyntax identifier)
        {
            return RouteAnchorDetector.IsLocalVariableChainReceiver(identifier);
        }

        /// <summary>
        /// Finds the latest forking assignment RHS (<c>?:</c> / <c>??</c> / <c>switch</c>) to the local
        /// before its use site (used by join-set discovery for statement-local tails).
        /// </summary>
        internal static bool TryGetPrecedingForkingAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ExpressionSyntax forkExpression)
        {
            return RouteOriginWindow.TryGetPrecedingForkingAssignment(
                identifier,
                semanticModel,
                out forkExpression);
        }

        /// <summary>
        /// Finds the latest known-origin assignment to the local before its use site.
        /// </summary>
        internal static bool TryGetPrecedingTrainRouteOriginAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart)
        {
            return RouteOriginWindow.TryGetPrecedingTrainRouteOriginAssignment(
                identifier,
                semanticModel,
                out originExpression,
                out assignmentSpanStart);
        }

        /// <summary>
        /// Finds the latest known-origin assignment to the local before its use site,
        /// including the full assignment RHS (for fluent-RHS station collection).
        /// </summary>
        internal static bool TryGetPrecedingTrainRouteOriginAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            return RouteOriginWindow.TryGetPrecedingTrainRouteOriginAssignment(
                identifier,
                semanticModel,
                out originExpression,
                out assignmentSpanStart,
                out assignmentRhs);
        }

        /// <summary>
        /// Finds the latest direct <c>new TrainRoute()</c> assignment to the local before its use site.
        /// </summary>
        internal static bool TryGetPrecedingTrainRouteCreationAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ObjectCreationExpressionSyntax creation)
        {
            return RouteOriginWindow.TryGetPrecedingTrainRouteCreationAssignment(
                identifier,
                semanticModel,
                out creation);
        }

        /// <summary>
        /// Collects Station / ServiceStation links for a local origin window.
        /// </summary>
        internal static ImmutableArray<StationChainLink> CollectLocalStatementStationLinks(
            IdentifierNameSyntax localIdentifier,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey = null)
        {
            return RouteOriginWindow.CollectLocalStatementStationLinks(
                localIdentifier,
                semanticModel,
                stationSitesByKey);
        }

        /// <summary>
        /// Finds a syntax identifier for a local that was initialized/assigned from a fluent
        /// chain rooted at <paramref name="chainRoot"/>.
        /// </summary>
        internal static bool TryFindLocalIdentifierAssignedFromFluentCreation(
            ExpressionSyntax chainRoot,
            SemanticModel semanticModel,
            out IdentifierNameSyntax localIdentifier)
        {
            return RouteOriginWindow.TryFindLocalIdentifierAssignedFromFluentCreation(
                chainRoot,
                semanticModel,
                out localIdentifier);
        }

        private static void FoldStatementLocalStationsIfBareReturn(
            IdentifierNameSyntax localRoot,
            ExpressionSyntax target,
            SemanticModel semanticModel,
            ImmutableArray<StationChainLink>.Builder stations)
        {
            var collected = RouteOriginWindow.CollectLocalStatementStationLinks(localRoot, semanticModel);
            for (var i = 0; i < collected.Length; i++)
            {
                var link = collected[i];
                if (link.Invocation != null
                    && link.Invocation.SpanStart < target.SpanStart)
                {
                    stations.Add(link);
                }
            }
        }

        private static IMethodSymbol GetContainingMethod(SyntaxNode node, SemanticModel semanticModel)
        {
            var methodDeclaration = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                return null;
            }

            return semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        }
    }
}
