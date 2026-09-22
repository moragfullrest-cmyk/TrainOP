using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Stage 1b entry: resolve chain anchors (<c>new</c> / local / factory /
    /// join-assign / external schema import) into parts, then legacy
    /// <see cref="RouteChainAnchor"/> / <see cref="RouteSite"/> via
    /// <see cref="LegacyRoutePartAdapter"/>.
    /// </summary>
    internal static class AnchorStage
    {
        /// <summary>
        /// Attempts to materialize a first-class route part at the given syntax node.
        /// </summary>
        internal static bool TryResolvePart(
            SyntaxNode node,
            SemanticModel semanticModel,
            out IRoutePart part)
        {
            part = null;
            if (node == null || semanticModel == null)
            {
                return false;
            }

            if (node is ObjectCreationExpressionSyntax objectCreation
                && CreationSeedMaterializer.TryMaterialize(objectCreation, semanticModel, out var seed))
            {
                part = seed;
                return true;
            }

            if (node is IdentifierNameSyntax identifier
                && RouteChainWalker.IsLocalVariableChainReceiver(identifier)
                && LocalBindingMaterializer.TryMaterialize(identifier, semanticModel, out var binding))
            {
                part = binding;
                return true;
            }

            if (node is InvocationExpressionSyntax factoryInvocation
                && RouteChainWalker.IsFactoryChainReceiver(factoryInvocation)
                && FactoryCallMaterializer.TryMaterialize(factoryInvocation, semanticModel, out var factoryCall))
            {
                part = factoryCall;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to resolve a route chain anchor at the given syntax node.
        /// External schema import is a factory-resolve variant, not a separate stage.
        /// </summary>
        internal static bool TryResolve(
            SyntaxNode node,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;
            return TryResolvePart(node, semanticModel, out var part)
                && LegacyRoutePartAdapter.TryToLegacyAnchor(part, out anchor);
        }

        /// <summary>
        /// Resolves an anchor <see cref="RouteSite"/> via parts → adapter → <see cref="RouteSite.CreateAnchor"/>.
        /// </summary>
        internal static bool TryResolveSite(
            SyntaxNode node,
            SemanticModel semanticModel,
            out RouteSite site)
        {
            site = null;
            if (!TryResolve(node, semanticModel, out var anchor))
            {
                return false;
            }

            site = RouteSite.CreateAnchor(anchor);
            return true;
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
