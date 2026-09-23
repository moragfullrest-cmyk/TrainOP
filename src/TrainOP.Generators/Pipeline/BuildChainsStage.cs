using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Unified BuildChains surface over graph assembly and chain entry points.
    /// </summary>
    /// <remarks>
    /// <see cref="RouteGraphAssembler.Build"/> remains the graph facade.
    /// <see cref="FromStation"/> / <see cref="EndingAt"/> use <see cref="RouteChainWalker"/>;
    /// <see cref="FactoryExtension"/> uses <see cref="ExtensionChainConnector"/>.
    /// </remarks>
    internal static class BuildChainsStage
    {
        /// <summary>
        /// Assembles <see cref="RouteGraph"/> from discovered parts.
        /// </summary>
        public static RouteGraph Build(ImmutableArray<IRoutePart> parts, Compilation compilation)
        {
            return RouteGraphAssembler.Build(parts, compilation);
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
            return ExtensionChainConnector.TryConnectEndingAt(
                endpoint,
                semanticModel,
                compilation,
                out chain,
                out diagnostics);
        }
    }
}

