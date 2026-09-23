using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Parts;

namespace TrainOP.Generators
{
    /// <summary>
    /// Backward root walk from a chain endpoint: <c>new</c> / local origin / factory / peel.
    /// </summary>
    /// <remarks>
    /// Local-origin lookup uses <see cref="RouteOriginWindow"/>.
    /// </remarks>
    internal static class RouteChainRootResolver
    {
        /// <summary>
        /// Walks backward from <paramref name="endpoint"/> through Station / ServiceStation
        /// receivers until a resolvable origin part is found.
        /// </summary>
        public static bool TryFindChainRootEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out IRoutePart origin)
        {
            origin = null;
            var current = endpoint;

            while (current != null)
            {
                current = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
                if (current == null)
                {
                    return false;
                }

                if (TryOriginFromCreation(current, semanticModel, out origin))
                {
                    return true;
                }

                if (current is IdentifierNameSyntax identifier
                    && TryOriginFromLocal(
                        identifier,
                        semanticModel,
                        out origin))
                {
                    return true;
                }

                if (TryResolveFactoryRoot(current, semanticModel, out var factoryCall))
                {
                    origin = factoryCall;
                    return true;
                }

                if (!TryGetChainMethodReceiver(current, out var receiver))
                {
                    return false;
                }

                current = receiver;
            }

            return false;
        }

        /// <summary>
        /// Resolves a bare / invocation factory root via <see cref="FactoryCallMaterializer"/>.
        /// </summary>
        public static bool TryResolveFactoryRoot(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            out FactoryCall factoryCall)
        {
            return FactoryCallMaterializer.TryMaterialize(current, semanticModel, out factoryCall);
        }

        /// <summary>
        /// If <paramref name="expression"/> is a Station / ServiceStation invocation,
        /// returns its receiver expression.
        /// </summary>
        public static bool TryGetChainMethodReceiver(
            ExpressionSyntax expression,
            out ExpressionSyntax receiver)
        {
            receiver = null;

            if (expression is not InvocationExpressionSyntax invocation
                || !StationSyntaxHelper.MatchesStationOrServiceStationShape(invocation, out var memberAccess))
            {
                return false;
            }

            receiver = memberAccess.Expression;
            return receiver != null;
        }

        /// <summary>
        /// Determines whether <paramref name="current"/> matches the chain endpoint (raw or unwrapped forms).
        /// </summary>
        public static bool MatchesChainEndpoint(
            ExpressionSyntax current,
            ExpressionSyntax endpoint,
            ExpressionSyntax unwrappedEndpoint)
        {
            if (ReferenceEquals(current, endpoint)
                || ReferenceEquals(current, unwrappedEndpoint))
            {
                return true;
            }

            var unwrappedCurrent = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
            if (ReferenceEquals(unwrappedCurrent, endpoint)
                || ReferenceEquals(unwrappedCurrent, unwrappedEndpoint))
            {
                return true;
            }

            var outermostCurrent = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
            var outermostEndpoint = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(endpoint);
            return ReferenceEquals(outermostCurrent, outermostEndpoint);
        }

        /// <summary>
        /// Finds a fluent chain root (<c>new</c> / factory) without resolving local-variable origins
        /// (avoids Collect ↔ EndingAt recursion).
        /// </summary>
        public static bool TryFindFluentChainRootWithoutLocalOrigins(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out ExpressionSyntax root)
        {
            root = null;
            var current = endpoint;

            while (current != null)
            {
                current = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
                if (current == null)
                {
                    return false;
                }

                if (TryOriginFromCreation(current, semanticModel, out var creation)
                    && RouteOriginPorts.TryGetRoot(creation, out root))
                {
                    return true;
                }

                if (TryResolveFactoryRoot(current, semanticModel, out var factoryCall))
                {
                    root = factoryCall.Root;
                    return true;
                }

                if (!TryGetChainMethodReceiver(current, out var receiver))
                {
                    return false;
                }

                current = receiver;
            }

            return false;
        }

        private static bool TryOriginFromCreation(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            out IRoutePart origin)
        {
            origin = null;
            if (current is not ObjectCreationExpressionSyntax objectCreation
                || !CreationSeedMaterializer.TryMaterialize(objectCreation, semanticModel, out var seed))
            {
                return false;
            }

            origin = seed;
            return true;
        }

        private static bool TryOriginFromLocal(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out IRoutePart origin)
        {
            origin = null;
            if (!LocalBindingMaterializer.TryMaterialize(identifier, semanticModel, out var binding))
            {
                return false;
            }

            origin = binding;
            return true;
        }
    }
}
