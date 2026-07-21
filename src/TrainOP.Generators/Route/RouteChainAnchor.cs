using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Route
{
    /// <summary>
    /// Root expression and metadata for a detected TrainRoute station chain.
    /// </summary>
    internal sealed class RouteChainAnchor
    {
        /// <summary>
        /// Creates a chain anchor from its kind, root expression, and optional containing method.
        /// </summary>
        public RouteChainAnchor(
            RouteChainAnchorKind kind,
            ExpressionSyntax root,
            Location location,
            IMethodSymbol containingMethod = null,
            IMethodSymbol factoryMethod = null,
            ImmutableArray<WagonBinding> initialWagons = default)
        {
            Kind = kind;
            Root = root;
            Location = location;
            ContainingMethod = containingMethod;
            FactoryMethod = factoryMethod;
            InitialWagons = initialWagons.IsDefault ? ImmutableArray<WagonBinding>.Empty : initialWagons;
        }

        public RouteChainAnchorKind Kind { get; }

        /// <summary>
        /// Expression from which fluent chain traversal starts.
        /// </summary>
        public ExpressionSyntax Root { get; }

        public Location Location { get; }

        /// <summary>
        /// Method that contains the anchor expression, when available.
        /// </summary>
        public IMethodSymbol ContainingMethod { get; }

        /// <summary>
        /// Factory method invoked at the chain root when the anchor is a factory extension.
        /// </summary>
        public IMethodSymbol FactoryMethod { get; }

        /// <summary>
        /// Terminal wagons produced by the factory before the extension chain continues.
        /// </summary>
        public ImmutableArray<WagonBinding> InitialWagons { get; }
    }
}
