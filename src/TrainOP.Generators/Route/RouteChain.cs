using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Route
{
    /// <summary>
    /// Detected TrainRoute station chain: origin part plus ordered station links.
    /// </summary>
    internal sealed class RouteChain
    {
        /// <summary>
        /// Creates a route chain from an origin part and ordered station links.
        /// </summary>
        public RouteChain(IRoutePart origin, ImmutableArray<StationLink> stations)
        {
            Origin = origin;
            Stations = stations.IsDefault ? ImmutableArray<StationLink>.Empty : stations;
        }

        /// <summary>
        /// Origin part (<see cref="CreationSeed"/>, <see cref="FactoryCall"/>,
        /// <see cref="LocalBinding"/>, or <see cref="JoinSeed"/>).
        /// </summary>
        public IRoutePart Origin { get; }

        /// <summary>
        /// Ordered station / service-station steps.
        /// </summary>
        public ImmutableArray<StationLink> Stations { get; }

        /// <summary>
        /// Fluent / statement root expression for this chain.
        /// </summary>
        public ExpressionSyntax Root =>
            RouteOriginPorts.TryGetRoot(Origin, out var root) ? root : null;

        /// <summary>
        /// Origin stamp location.
        /// </summary>
        public Location AnchorLocation => Origin?.Location;

        /// <summary>
        /// Factory method when the origin is a factory call or stamped local.
        /// </summary>
        public IMethodSymbol FactoryMethod => RouteOriginPorts.GetFactoryMethod(Origin);

        /// <summary>
        /// Initial / merged wagons from the origin part.
        /// </summary>
        public ImmutableArray<WagonBinding> InitialWagons => RouteOriginPorts.GetInitialWagons(Origin);

        /// <summary>
        /// Containing method stamped on the origin, when available.
        /// </summary>
        public IMethodSymbol ContainingMethod => RouteOriginPorts.GetContainingMethod(Origin);
    }
}
