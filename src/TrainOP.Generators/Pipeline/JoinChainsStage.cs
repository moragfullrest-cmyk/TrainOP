using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Unified JoinChains API for <c>?:</c> / <c>??</c> / <c>switch</c>
    /// branch joins and factory fork-join return paths.
    /// </summary>
    /// <remarks>
    /// Analyzer diagnostics and factory-path simulation both enter here so discovery,
    /// fork detection, and branch expand stay shared. TOP* diagnostic content is unchanged.
    /// </remarks>
    internal static class JoinChainsStage
    {
        /// <summary>
        /// Finds all join sets in <paramref name="tree"/> where a forking receiver feeds a shared
        /// downstream Station call.
        /// </summary>
        public static ImmutableArray<BranchRouteJoinSet> Find(SyntaxTree tree, SemanticModel model)
        {
            return BranchRouteJoinSetFinder.Find(tree, model);
        }

        /// <summary>
        /// Validates branch resolution, unknown terminals, and type compatibility across a join set.
        /// </summary>
        public static BranchRouteJoinValidation Validate(
            BranchRouteJoinSet joinSet,
            SemanticModel semanticModel = null)
        {
            return BranchRouteJoinValidator.Validate(joinSet, semanticModel);
        }

        /// <summary>
        /// Finds and validates a join set into <see cref="JoinedChain"/> IR.
        /// Prefers <see cref="JoinChainConnector"/> (arms → JoinSeed + validator).
        /// </summary>
        public static JoinedChain Join(
            BranchRouteJoinSet joinSet,
            SemanticModel semanticModel = null)
        {
            if (JoinChainConnector.TryConnect(joinSet, semanticModel, out _, out var joinSeed))
            {
                return new JoinedChain(joinSet, joinSeed.Validation);
            }

            var validation = Validate(joinSet, semanticModel);
            return new JoinedChain(joinSet, validation);
        }

        /// <summary>
        /// Collects joined-chain IR for every syntax tree in <paramref name="compilation"/>.
        /// </summary>
        public static ImmutableArray<JoinedChain> Collect(Compilation compilation)
        {
            if (compilation == null)
            {
                return ImmutableArray<JoinedChain>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<JoinedChain>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);
                foreach (var joinSet in Find(tree, semanticModel))
                {
                    builder.Add(Join(joinSet, semanticModel));
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        /// Determines whether an expression is a forking receiver (<c>?:</c> / <c>??</c> / <c>switch</c>).
        /// Shared by analyzer join discovery and factory fork-join.
        /// </summary>
        public static bool IsForkingExpression(ExpressionSyntax expression)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression is ConditionalExpressionSyntax || expression is SwitchExpressionSyntax)
            {
                return true;
            }

            return expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression);
        }

        /// <summary>
        /// Discovers leaf branch graphs under a forking receiver (thin wrap of discoverer).
        /// </summary>
        public static ImmutableArray<BranchRouteGraph> DiscoverBranches(
            ExpressionSyntax receiver,
            SemanticModel semanticModel)
        {
            return BranchRouteGraphDiscoverer.Discover(receiver, semanticModel);
        }

        /// <summary>
        /// Simulates factory return paths that fork then rejoin into a shared downstream station chain.
        /// Each resolved branch seeds the downstream walk with that branch's terminals (not a merge).
        /// </summary>
        public static bool TrySimulateFactoryForkJoin(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out ImmutableArray<FactoryPathSimulation> paths)
        {
            paths = ImmutableArray<FactoryPathSimulation>.Empty;
            if (!TryFindForkJoinAnchor(expression, out var forkReceiver, out var firstDownstreamStation))
            {
                return false;
            }

            var branches = DiscoverBranches(forkReceiver, semanticModel);
            if (branches.IsDefaultOrEmpty)
            {
                return false;
            }

            if (!BuildChainsStage.FromStation(
                firstDownstreamStation,
                semanticModel,
                out var downstreamChain))
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<FactoryPathSimulation>();
            foreach (var branch in branches)
            {
                var location = branch.BranchExpression?.GetLocation() ?? expression.GetLocation();
                if (!branch.IsResolved
                    || branch.Simulation == null
                    || branch.Simulation.HasUnknownReturn)
                {
                    builder.Add(new FactoryPathSimulation(
                        ImmutableArray<WagonBinding>.Empty,
                        hasUnknownReturn: true,
                        location));
                    continue;
                }

                var branchTerminals = TerminalSetAdapters.FromSimulation(branch.Simulation);
                var simulation = ChainGraphSimulator.Simulate(
                    downstreamChain,
                    branchTerminals.Wagons);
                var pathTerminals = TerminalSetAdapters.FromSimulation(
                    simulation,
                    TerminalSet.Origin.FactoryPath);
                builder.Add(new FactoryPathSimulation(
                    pathTerminals.Wagons,
                    pathTerminals.HasUnknownReturn,
                    location));
            }

            paths = builder.ToImmutable();
            return paths.Length > 0;
        }

        /// <summary>
        /// Locates a forking receiver that feeds one or more fluent stations ending at
        /// <paramref name="endpoint"/> (factory fork-join pattern).
        /// </summary>
        public static bool TryFindForkJoinAnchor(
            ExpressionSyntax endpoint,
            out ExpressionSyntax forkReceiver,
            out InvocationExpressionSyntax firstDownstreamStation)
        {
            forkReceiver = null;
            firstDownstreamStation = null;

            var current = ReceiverExpressionSyntaxPeel.UnwrapTransparent(endpoint);
            if (current == null || !IsStationInvocation(current))
            {
                return false;
            }

            while (true)
            {
                var invocation = (InvocationExpressionSyntax)current;
                var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
                var receiver = memberAccess.Expression;
                if (IsForkingExpression(ReceiverExpressionSyntaxPeel.UnwrapTransparent(receiver)))
                {
                    forkReceiver = receiver;
                    firstDownstreamStation = invocation;
                    return true;
                }

                if (receiver is not InvocationExpressionSyntax receiverInvocation
                    || !IsStationInvocation(receiverInvocation))
                {
                    return false;
                }

                current = receiverInvocation;
            }
        }

        private static bool IsStationInvocation(ExpressionSyntax expression)
        {
            return expression is InvocationExpressionSyntax invocation
                && StationSyntaxHelper.MatchesStationOrServiceStationShape(invocation, out _);
        }
    }
}
