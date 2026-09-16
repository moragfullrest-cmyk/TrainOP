using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Stage 4 entry: unified BuildChains surface.
    /// </summary>
    /// <remarks>
    /// <see cref="RouteGraphAssembler.Build"/> remains the graph facade. Walker entry points
    /// (<see cref="FromStation"/> / <see cref="EndingAt"/> / <see cref="FactoryExtension"/>)
    /// are variants of the same stage — not separate pipelines.
    /// </remarks>
    internal static class BuildChainsStage
    {
        /// <summary>
        /// Assembles <see cref="RouteGraph"/> from discovered sites (primary BuildChains entry).
        /// </summary>
        public static RouteGraph Build(ImmutableArray<RouteSite> sites, Compilation compilation)
        {
            return RouteGraphAssembler.Build(sites, compilation);
        }

        /// <summary>
        /// Variant: build a chain starting at an already-identified Station invocation (inclusive)
        /// and continuing through further fluent stations (join / factory fork downstream).
        /// </summary>
        public static bool FromStation(
            InvocationExpressionSyntax startStation,
            SemanticModel semanticModel,
            out RouteChain chain)
        {
            return RouteChainWalker.TryBuildChainFromStationInvocation(
                startStation,
                semanticModel,
                out chain);
        }

        /// <summary>
        /// Variant: build a chain from a known root forward until <paramref name="endpoint"/> (inclusive).
        /// </summary>
        public static bool EndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out RouteChain chain)
        {
            return RouteChainWalker.TryBuildChainEndingAt(endpoint, semanticModel, out chain);
        }

        /// <summary>
        /// Variant: build a factory-extension chain ending at <paramref name="endpoint"/>.
        /// </summary>
        public static bool FactoryExtension(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            Compilation compilation,
            out RouteChain chain,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            return RouteChainWalker.TryBuildFactoryExtensionChain(
                endpoint,
                semanticModel,
                compilation,
                out chain,
                out diagnostics);
        }
    }
}
