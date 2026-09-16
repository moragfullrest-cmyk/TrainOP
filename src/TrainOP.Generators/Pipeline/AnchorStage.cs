using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Stage 1b entry: resolve chain anchors (<c>new</c> / local / factory /
    /// external schema import) into <see cref="RouteChainAnchor"/>.
    /// </summary>
    internal static class AnchorStage
    {
        /// <summary>
        /// Attempts to resolve a route chain anchor at the given syntax node.
        /// External schema import is a factory-resolve variant, not a separate stage.
        /// Thin wrap of <see cref="RouteChainWalker.TryDetectAnchorSite"/>.
        /// </summary>
        internal static bool TryResolve(
            SyntaxNode node,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            return RouteChainWalker.TryDetectAnchorSite(node, semanticModel, out anchor);
        }

        /// <summary>
        /// Resolves factory terminal wagons via inline analysis or exported schema lookup.
        /// Thin wrap of <see cref="RouteFactoryResolver.TryResolve"/> (includes
        /// <see cref="ExternalRouteSchemaResolver"/> for cross-assembly factories).
        /// </summary>
        internal static bool TryResolveFactory(
            IMethodSymbol factoryMethod,
            Compilation compilation,
            Location diagnosticLocation,
            out ImmutableArray<WagonBinding> terminalWagons,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            return RouteFactoryResolver.TryResolve(
                factoryMethod,
                compilation,
                diagnosticLocation,
                out terminalWagons,
                out diagnostics);
        }
    }
}
