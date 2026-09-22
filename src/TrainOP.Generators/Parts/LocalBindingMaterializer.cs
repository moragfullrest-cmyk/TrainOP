using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Materializes a <see cref="LocalBinding"/> from a local use with a known TrainRoute origin.
    /// </summary>
    /// <remarks>
    /// Covers bare <c>new</c>, bare factory (SL-1 / SL-2), and C-10/C-11 join-assign forks.
    /// Preceding origin comes from <see cref="RouteOriginWindow"/>.
    /// </remarks>
    internal static class LocalBindingMaterializer
    {
        /// <summary>
        /// Attempts to materialize a local binding at <paramref name="identifier"/>.
        /// </summary>
        public static bool TryMaterialize(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out LocalBinding binding)
        {
            binding = null;
            if (identifier == null || semanticModel == null)
            {
                return false;
            }

            if (!RouteOriginWindow.TryGetPrecedingTrainRouteOriginAssignment(
                    identifier,
                    semanticModel,
                    out var originExpression,
                    out _))
            {
                return false;
            }

            return TryMaterializeFromOrigin(
                identifier,
                originExpression,
                semanticModel,
                out binding);
        }

        /// <summary>
        /// Attempts to materialize when the origin expression is already known.
        /// </summary>
        public static bool TryMaterializeFromOrigin(
            IdentifierNameSyntax identifier,
            ExpressionSyntax originExpression,
            SemanticModel semanticModel,
            out LocalBinding binding)
        {
            binding = null;
            if (identifier == null || originExpression == null || semanticModel == null)
            {
                return false;
            }

            var containingMethod = GetContainingMethod(identifier, semanticModel);

            if (originExpression is ObjectCreationExpressionSyntax
                && CreationSeedMaterializer.TryMaterialize(originExpression, semanticModel, out var seed))
            {
                binding = new LocalBinding(
                    identifier,
                    seed.Location,
                    seed,
                    containingMethod);
                return true;
            }

            if (FactoryCallMaterializer.TryMaterialize(originExpression, semanticModel, out var factoryCall))
            {
                binding = new LocalBinding(
                    identifier,
                    factoryCall.Location,
                    factoryCall,
                    containingMethod);
                return true;
            }

            return TryMaterializeJoinedAssign(
                identifier,
                originExpression,
                containingMethod,
                semanticModel,
                out binding);
        }

        /// <summary>
        /// Attempts to materialize when <paramref name="node"/> is an identifier name.
        /// </summary>
        public static bool TryMaterialize(
            SyntaxNode node,
            SemanticModel semanticModel,
            out LocalBinding binding)
        {
            binding = null;
            return node is IdentifierNameSyntax identifier
                && TryMaterialize(identifier, semanticModel, out binding);
        }

        private static bool TryMaterializeJoinedAssign(
            IdentifierNameSyntax identifier,
            ExpressionSyntax originExpression,
            IMethodSymbol containingMethod,
            SemanticModel semanticModel,
            out LocalBinding binding)
        {
            binding = null;
            originExpression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(originExpression);
            if (!JoinChainsStage.IsForkingExpression(originExpression))
            {
                return false;
            }

            if (!RouteAnchorDetector.TryValidateLocalAssignJoin(
                    originExpression,
                    semanticModel,
                    out var validation)
                || !validation.CanMerge)
            {
                return false;
            }

            var forkLocation = originExpression.GetLocation();
            var mergedWagons = validation.MergedTerminalWagons.IsDefault
                ? ImmutableArray<WagonBinding>.Empty
                : validation.MergedTerminalWagons;

            if (!RouteAnchorDetector.TryGetSharedFactoryFromJoinArms(
                    originExpression,
                    semanticModel,
                    out var sharedFactory,
                    out var factoryKind,
                    out var factoryWagons))
            {
                binding = new LocalBinding(
                    identifier,
                    forkLocation,
                    origin: null,
                    containingMethod,
                    mergedWagons);
                return true;
            }

            var wagons = factoryWagons.IsDefaultOrEmpty ? mergedWagons : factoryWagons;
            if (TryStampSharedFactoryOrigin(
                    originExpression,
                    semanticModel,
                    sharedFactory,
                    factoryKind,
                    wagons,
                    containingMethod,
                    out var factoryOrigin))
            {
                binding = new LocalBinding(
                    identifier,
                    forkLocation,
                    factoryOrigin,
                    containingMethod,
                    factoryOrigin.InitialWagons);
                return true;
            }

            // Shared factory without a usable arm invocation root — stamp method on the binding.
            binding = new LocalBinding(
                identifier,
                forkLocation,
                origin: null,
                containingMethod,
                wagons,
                sharedFactory,
                factoryKind);
            return true;
        }

        /// <summary>
        /// Stamps a <see cref="FactoryCall"/> from the first join arm that matches
        /// <paramref name="sharedFactory"/> (detector already established shared identity).
        /// </summary>
        private static bool TryStampSharedFactoryOrigin(
            ExpressionSyntax forkExpression,
            SemanticModel semanticModel,
            IMethodSymbol sharedFactory,
            RouteChainAnchorKind factoryKind,
            ImmutableArray<WagonBinding> wagons,
            IMethodSymbol containingMethod,
            out FactoryCall factoryCall)
        {
            factoryCall = null;
            var branches = JoinChainsStage.DiscoverBranches(forkExpression, semanticModel);
            if (branches.IsDefaultOrEmpty)
            {
                return false;
            }

            for (var i = 0; i < branches.Length; i++)
            {
                if (!TryStampFromArmAnchor(
                        branches[i].Chain?.Anchor,
                        sharedFactory,
                        factoryKind,
                        wagons,
                        containingMethod,
                        out factoryCall))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TryStampFromArmAnchor(
            RouteChainAnchor armAnchor,
            IMethodSymbol sharedFactory,
            RouteChainAnchorKind factoryKind,
            ImmutableArray<WagonBinding> wagons,
            IMethodSymbol containingMethod,
            out FactoryCall factoryCall)
        {
            factoryCall = null;
            if (armAnchor?.FactoryMethod == null
                || !SymbolEqualityComparer.Default.Equals(armAnchor.FactoryMethod, sharedFactory))
            {
                return false;
            }

            if (FactoryCall.TryFromLegacyAnchor(armAnchor, out factoryCall))
            {
                if (factoryCall.InitialWagons.IsDefaultOrEmpty && !wagons.IsDefaultOrEmpty)
                {
                    factoryCall = new FactoryCall(
                        factoryCall.Root,
                        factoryCall.Location,
                        factoryCall.Kind,
                        factoryCall.FactoryMethod,
                        wagons,
                        containingMethod ?? factoryCall.ContainingMethod);
                }

                return true;
            }

            // Arm root is not an invocation — synthesize kind from legacy stamp.
            if (armAnchor.Root is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            factoryCall = new FactoryCall(
                invocation,
                armAnchor.Location,
                factoryKind == RouteChainAnchorKind.FactorySchema
                    ? FactoryCallKind.Schema
                    : FactoryCallKind.Inline,
                sharedFactory,
                wagons,
                containingMethod);
            return true;
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
