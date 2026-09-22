using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Detects route chain anchors via Parts materializers (K4).
    /// </summary>
    internal static class RouteAnchorDetector
    {
        /// <summary>
        /// Attempts to detect a route chain anchor at the given syntax node.
        /// </summary>
        public static bool TryDetect(
            SyntaxNode node,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (node is ObjectCreationExpressionSyntax objectCreation
                && TryDetectObjectCreation(objectCreation, semanticModel, out anchor))
            {
                return true;
            }

            if (node is IdentifierNameSyntax identifier
                && TryDetectLocalVariable(identifier, semanticModel, out anchor))
            {
                return true;
            }

            if (node is InvocationExpressionSyntax factoryInvocation
                && TryDetectFactoryInvocation(factoryInvocation, semanticModel, out anchor))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the invocation is the receiver of a route handler member access.
        /// </summary>
        public static bool IsFactoryChainReceiver(InvocationExpressionSyntax factoryInvocation)
        {
            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(factoryInvocation);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            return StationSyntaxHelper.IsStationOrServiceStationMethodName(methodName);
        }

        /// <summary>
        /// Determines whether the identifier is the receiver of a route handler member access.
        /// </summary>
        public static bool IsLocalVariableChainReceiver(IdentifierNameSyntax identifier)
        {
            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(identifier);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            return StationSyntaxHelper.IsStationOrServiceStationMethodName(methodName);
        }

        /// <summary>
        /// Validates a local-assign join fork (arms merge) for statement-local tails.
        /// </summary>
        public static bool TryValidateLocalAssignJoin(
            ExpressionSyntax forkExpression,
            SemanticModel semanticModel,
            out BranchRouteJoinValidation validation)
        {
            validation = null;
            forkExpression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(forkExpression);
            if (!JoinChainsStage.IsForkingExpression(forkExpression))
            {
                return false;
            }

            var branches = JoinChainsStage.DiscoverBranches(forkExpression, semanticModel);
            var joinSet = new BranchRouteJoinSet(
                forkExpression,
                downstreamStation: null,
                branches);
            validation = BranchRouteJoinValidator.Validate(joinSet, semanticModel);
            return true;
        }

        /// <summary>
        /// When all join arms share one factory method, returns that factory and its legacy kind.
        /// </summary>
        public static bool TryGetSharedFactoryFromJoinArms(
            ExpressionSyntax forkExpression,
            SemanticModel semanticModel,
            out IMethodSymbol sharedFactory,
            out RouteChainAnchorKind anchorKind,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            sharedFactory = null;
            anchorKind = default;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            var branches = JoinChainsStage.DiscoverBranches(forkExpression, semanticModel);
            if (branches.IsDefaultOrEmpty)
            {
                return false;
            }

            IMethodSymbol candidate = null;
            RouteChainAnchorKind candidateKind = default;
            ImmutableArray<WagonBinding> candidateWagons = default;

            for (var i = 0; i < branches.Length; i++)
            {
                var branch = branches[i];
                if (!branch.IsResolved || branch.Chain?.Anchor?.FactoryMethod == null)
                {
                    return false;
                }

                var factory = branch.Chain.Anchor.FactoryMethod;
                if (candidate == null)
                {
                    candidate = factory;
                    candidateKind = branch.Chain.Anchor.Kind;
                    candidateWagons = branch.Chain.Anchor.InitialWagons;
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(candidate, factory))
                {
                    return false;
                }
            }

            if (candidate == null)
            {
                return false;
            }

            sharedFactory = candidate;
            anchorKind = candidateKind;
            initialWagons = candidateWagons;
            return true;
        }

        private static bool TryDetectObjectCreation(
            ObjectCreationExpressionSyntax objectCreation,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (!CreationSeedMaterializer.TryMaterialize(objectCreation, semanticModel, out var seed))
            {
                return false;
            }

            return LegacyRoutePartAdapter.TryToLegacyAnchor(seed, out anchor);
        }

        private static bool TryDetectLocalVariable(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (!IsLocalVariableChainReceiver(identifier))
            {
                return false;
            }

            return LocalBindingMaterializer.TryMaterialize(identifier, semanticModel, out var binding)
                && LegacyRoutePartAdapter.TryToLegacyAnchor(binding, out anchor);
        }

        private static bool TryDetectFactoryInvocation(
            InvocationExpressionSyntax factoryInvocation,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (!IsFactoryChainReceiver(factoryInvocation))
            {
                return false;
            }

            if (!FactoryCallMaterializer.TryMaterialize(factoryInvocation, semanticModel, out var factoryCall))
            {
                return false;
            }

            return LegacyRoutePartAdapter.TryToLegacyAnchor(factoryCall, out anchor);
        }
    }
}
