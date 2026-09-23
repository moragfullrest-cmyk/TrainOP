using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Detects route chain origins via Parts materializers.
    /// </summary>
    internal static class RouteAnchorDetector
    {
        /// <summary>
        /// Attempts to detect a route chain origin at the given syntax node.
        /// </summary>
        public static bool TryDetect(
            SyntaxNode node,
            SemanticModel semanticModel,
            out IRoutePart origin)
        {
            origin = null;

            if (node is ObjectCreationExpressionSyntax objectCreation
                && CreationSeedMaterializer.TryMaterialize(objectCreation, semanticModel, out var seed))
            {
                origin = seed;
                return true;
            }

            if (node is IdentifierNameSyntax identifier
                && IsLocalVariableChainReceiver(identifier)
                && LocalBindingMaterializer.TryMaterialize(identifier, semanticModel, out var binding))
            {
                origin = binding;
                return true;
            }

            if (node is InvocationExpressionSyntax factoryInvocation
                && IsFactoryChainReceiver(factoryInvocation)
                && FactoryCallMaterializer.TryMaterialize(factoryInvocation, semanticModel, out var factoryCall))
            {
                origin = factoryCall;
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

            var branches = BranchRouteGraphDiscoverer.Discover(forkExpression, semanticModel);
            var joinSet = new BranchRouteJoinSet(
                forkExpression,
                downstreamStation: null,
                branches);
            validation = BranchRouteJoinValidator.Validate(joinSet, semanticModel);
            return true;
        }

        /// <summary>
        /// When all join arms share one factory method, returns that factory and its call kind.
        /// </summary>
        public static bool TryGetSharedFactoryFromJoinArms(
            ExpressionSyntax forkExpression,
            SemanticModel semanticModel,
            out IMethodSymbol sharedFactory,
            out FactoryCallKind factoryKind,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            sharedFactory = null;
            factoryKind = default;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            var branches = BranchRouteGraphDiscoverer.Discover(forkExpression, semanticModel);
            if (branches.IsDefaultOrEmpty)
            {
                return false;
            }

            IMethodSymbol candidate = null;
            FactoryCallKind candidateKind = default;
            ImmutableArray<WagonBinding> candidateWagons = default;

            for (var i = 0; i < branches.Length; i++)
            {
                var branch = branches[i];
                var factory = branch.Chain?.FactoryMethod;
                if (!branch.IsResolved || factory == null)
                {
                    return false;
                }

                if (candidate == null)
                {
                    candidate = factory;
                    candidateKind = ResolveFactoryKind(branch.Chain.Origin);
                    candidateWagons = branch.Chain.InitialWagons;
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
            factoryKind = candidateKind;
            initialWagons = candidateWagons;
            return true;
        }

        private static FactoryCallKind ResolveFactoryKind(IRoutePart origin)
        {
            if (origin is FactoryCall factoryCall)
            {
                return factoryCall.Kind;
            }

            if (origin is LocalBinding localBinding && localBinding.FactoryKind.HasValue)
            {
                return localBinding.FactoryKind.Value;
            }

            return FactoryCallKind.Inline;
        }
    }
}
