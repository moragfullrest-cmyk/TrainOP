using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds <see cref="RouteChain"/> from station / endpoint / bare-factory ingress points.
    /// </summary>
    /// <remarks>
    /// Entry variants:
    /// <list type="bullet">
    /// <item><see cref="TryBuildChainFromStationInvocation"/> — forward from a station (join / fork downstream)</item>
    /// <item><see cref="TryBuildChainEndingAt"/> — root → endpoint inclusive</item>
    /// <item><see cref="TryBuildBareFactoryBranch"/> — bare factory leaf for join arms</item>
    /// </list>
    /// Prefer <see cref="BuildChainsStage"/> at call sites when folding setup.
    /// Peel / detect / origin-window live on <see cref="RouteChainPeel"/>,
    /// <see cref="RouteAnchorDetector"/>, <see cref="RouteOriginWindow"/>.
    /// </remarks>
    internal static class RouteChainWalker
    {
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

            var stations = ImmutableArray.CreateBuilder<StationLink>();

            if (StationSyntaxHelper.IsCandidateServiceStationInvocation(startStation)
                && StationSyntaxHelper.TryGetDataServiceStationInvocation(
                    startStation,
                    semanticModel,
                    out var serviceStationName,
                    out var serviceHandlerLocation,
                    out var serviceHandlerBinding))
            {
                stations.Add(new StationLink(
                    StationLinkKind.ServiceStation,
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
                stations.Add(new StationLink(
                    StationLinkKind.Station,
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

            var joinSeed = new JoinSeed(
                startStation,
                startStation,
                ImmutableArray<JoinArm>.Empty,
                validation: null);

            var current = (ExpressionSyntax)startStation;
            while (RouteChainPeel.TryAdvanceChain(current, semanticModel, stations, out current, null)) ;

            chain = new RouteChain(joinSeed, stations.ToImmutable());
            return true;
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
                || !RouteChainRootResolver.TryResolveFactoryRoot(
                    expression,
                    semanticModel,
                    out var factoryCall)
                || factoryCall.FactoryMethod == null)
            {
                return false;
            }

            if (!RouteFactoryResolver.TryResolve(
                    factoryCall.FactoryMethod,
                    semanticModel.Compilation,
                    expression.GetLocation(),
                    out var terminalWagons,
                    out var diagnostics)
                || !diagnostics.IsDefaultOrEmpty)
            {
                return false;
            }

            var wagons = terminalWagons.IsDefault ? factoryCall.InitialWagons : terminalWagons;
            var origin = new FactoryCall(
                factoryCall.Root,
                factoryCall.Location,
                factoryCall.Kind,
                factoryCall.FactoryMethod,
                wagons,
                GetContainingMethod(factoryCall.Root, semanticModel));
            chain = new RouteChain(origin, ImmutableArray<StationLink>.Empty);
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

            if (!RouteChainRootResolver.TryFindChainRootEndingAt(
                    target,
                    semanticModel,
                    out var origin)
                || !RouteOriginPorts.TryGetRoot(origin, out var root))
            {
                return false;
            }

            var stations = ImmutableArray.CreateBuilder<StationLink>();
            var current = root;

            while (!RouteChainRootResolver.MatchesChainEndpoint(current, endpoint, target))
            {
                if (!RouteChainPeel.TryAdvanceChain(current, semanticModel, stations, out var next, null))
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

            chain = new RouteChain(origin, stations.ToImmutable());
            return true;
        }

        private static void FoldStatementLocalStationsIfBareReturn(
            IdentifierNameSyntax localRoot,
            ExpressionSyntax target,
            SemanticModel semanticModel,
            ImmutableArray<StationLink>.Builder stations)
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
