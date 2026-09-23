using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Origin part: factory invocation (Inline vs Schema resolve).
    /// </summary>
    /// <remarks>
    /// Dispatch identity and caller-chain keys are owned here — callers must not invent
    /// a parallel kind enum for those purposes.
    /// </remarks>
    internal sealed class FactoryCall : IRoutePart
    {
        /// <summary>
        /// Creates a factory-call part from a materialized factory invocation.
        /// </summary>
        public FactoryCall(
            InvocationExpressionSyntax root,
            Location location,
            FactoryCallKind kind,
            IMethodSymbol factoryMethod,
            ImmutableArray<WagonBinding> initialWagons = default,
            IMethodSymbol containingMethod = null)
        {
            Root = root;
            Location = location;
            Kind = kind;
            FactoryMethod = factoryMethod;
            InitialWagons = initialWagons.IsDefault
                ? ImmutableArray<WagonBinding>.Empty
                : initialWagons;
            ContainingMethod = containingMethod;
        }

        /// <summary>
        /// Factory invocation expression at the chain root.
        /// </summary>
        public InvocationExpressionSyntax Root { get; }

        /// <inheritdoc />
        public Location Location { get; }

        /// <summary>
        /// Inline vs schema resolve path (internal variant; prefer port methods for dispatch/keys).
        /// </summary>
        public FactoryCallKind Kind { get; }

        /// <summary>
        /// Resolved factory method symbol.
        /// </summary>
        public IMethodSymbol FactoryMethod { get; }

        /// <summary>
        /// Terminal wagons produced by the factory before an extension continues.
        /// </summary>
        public ImmutableArray<WagonBinding> InitialWagons { get; }

        /// <summary>
        /// Method containing the factory invocation, when available.
        /// </summary>
        public IMethodSymbol ContainingMethod { get; }

        /// <summary>
        /// Whether this call resolves through exported schema (vs inline body).
        /// </summary>
        public bool UsesSchemaDispatch => Kind == FactoryCallKind.Schema;

        /// <summary>
        /// Resolves caller-chain key for an origin that carries a factory method
        /// (invocation root or identifier-rooted factory local). Never guesses call-site location.
        /// </summary>
        public static bool TryBuildCallerChainKeyFromOrigin(
            IRoutePart origin,
            Compilation compilation,
            out string callerChainKey)
        {
            callerChainKey = string.Empty;
            if (origin == null)
            {
                return false;
            }

            if (origin is FactoryCall factoryCall)
            {
                return factoryCall.TryBuildCallerChainKey(compilation, out callerChainKey);
            }

            if (origin is LocalBinding localBinding)
            {
                if (localBinding.Origin is FactoryCall nested)
                {
                    return nested.TryBuildCallerChainKey(compilation, out callerChainKey);
                }

                if (localBinding.FactoryMethod != null)
                {
                    return FactoryDispatchMetadata.TryResolve(
                            localBinding.FactoryMethod,
                            compilation,
                            out callerChainKey,
                            out _)
                        && !string.IsNullOrEmpty(callerChainKey);
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves caller-chain key via factory dispatch identity (schema or body).
        /// Empty when the factory has no resolvable dispatch metadata (do not guess call-site).
        /// </summary>
        public bool TryBuildCallerChainKey(Compilation compilation, out string callerChainKey)
        {
            if (!TryResolveDispatchIdentity(compilation, out callerChainKey, out _))
            {
                callerChainKey = string.Empty;
                return false;
            }

            return !string.IsNullOrEmpty(callerChainKey);
        }

        /// <summary>
        /// Resolves dispatch identity (caller-chain key + upstream station count) for this factory.
        /// </summary>
        public bool TryResolveDispatchIdentity(
            Compilation compilation,
            out string callerChainKey,
            out int stationCount)
        {
            callerChainKey = string.Empty;
            stationCount = 0;
            if (FactoryMethod == null || compilation == null)
            {
                return false;
            }

            return FactoryDispatchMetadata.TryResolve(
                FactoryMethod,
                compilation,
                out callerChainKey,
                out stationCount);
        }
    }
}
