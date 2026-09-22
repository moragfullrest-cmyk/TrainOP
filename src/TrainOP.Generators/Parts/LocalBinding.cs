using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Local variable binding window after a known origin (legacy LocalVariable /
    /// factory kind when origin is a factory call / join-assign).
    /// </summary>
    internal sealed class LocalBinding : IRoutePart
    {
        private readonly ImmutableArray<WagonBinding> _initialWagons;
        private readonly IMethodSymbol _stampedFactoryMethod;
        private readonly RouteChainAnchorKind? _stampedFactoryKind;

        /// <summary>
        /// Creates a local binding from a materialized identifier and its origin part.
        /// </summary>
        /// <param name="identifier">Local use that starts fluent / statement traversal.</param>
        /// <param name="originLocation">
        /// Origin call-site location used for chain key stamping (ctor, factory, or join fork).
        /// </param>
        /// <param name="origin">
        /// Materialized origin: <see cref="CreationSeed"/>, <see cref="FactoryCall"/>,
        /// or <see langword="null"/> for join-assign without a single seed invocation.
        /// </param>
        /// <param name="containingMethod">Method containing the local use.</param>
        /// <param name="initialWagons">
        /// Terminal wagons when <paramref name="origin"/> does not carry them
        /// (e.g. non-factory join-assign merge).
        /// </param>
        /// <param name="factoryMethod">
        /// Shared factory stamp for join-assign when <paramref name="origin"/> is not a
        /// <see cref="FactoryCall"/> (rare; prefer Origin = FactoryCall).
        /// </param>
        /// <param name="factoryKind">
        /// Legacy factory kind accompanying <paramref name="factoryMethod"/> when Origin is null.
        /// </param>
        public LocalBinding(
            IdentifierNameSyntax identifier,
            Location originLocation,
            IRoutePart origin,
            IMethodSymbol containingMethod = null,
            ImmutableArray<WagonBinding> initialWagons = default,
            IMethodSymbol factoryMethod = null,
            RouteChainAnchorKind? factoryKind = null)
        {
            Identifier = identifier;
            Location = originLocation;
            Origin = origin;
            ContainingMethod = containingMethod;
            _initialWagons = initialWagons.IsDefault
                ? ImmutableArray<WagonBinding>.Empty
                : initialWagons;
            _stampedFactoryMethod = factoryMethod;
            _stampedFactoryKind = factoryKind;
        }

        /// <summary>
        /// Local identifier used as chain receiver / statement root.
        /// </summary>
        public IdentifierNameSyntax Identifier { get; }

        /// <inheritdoc />
        /// <remarks>Origin stamp location (not the identifier use site).</remarks>
        public Location Location { get; }

        /// <summary>
        /// Known origin that established this binding window.
        /// </summary>
        public IRoutePart Origin { get; }

        /// <summary>
        /// Method containing the local use, when available.
        /// </summary>
        public IMethodSymbol ContainingMethod { get; }

        /// <summary>
        /// Effective factory method: from <see cref="FactoryCall"/> origin or join stamp.
        /// </summary>
        public IMethodSymbol FactoryMethod =>
            (Origin as FactoryCall)?.FactoryMethod ?? _stampedFactoryMethod;

        /// <summary>
        /// Effective initial wagons: from <see cref="FactoryCall"/> origin when present,
        /// otherwise the join / residual stamp.
        /// </summary>
        public ImmutableArray<WagonBinding> InitialWagons =>
            Origin is FactoryCall factoryCall && !factoryCall.InitialWagons.IsDefaultOrEmpty
                ? factoryCall.InitialWagons
                : _initialWagons;

        /// <summary>
        /// Maps this binding to a legacy anchor kind (factory stamp, LocalVariable, or Origin port).
        /// </summary>
        public RouteChainAnchorKind ToLegacyAnchorKind()
        {
            if (Origin is FactoryCall factoryOrigin)
            {
                return factoryOrigin.ToLegacyAnchorKind();
            }

            if (_stampedFactoryKind.HasValue)
            {
                return _stampedFactoryKind.Value;
            }

            if (FactoryMethod != null)
            {
                return RouteChainAnchorKind.MethodInvocation;
            }

            return RouteChainAnchorKind.LocalVariable;
        }
    }
}
