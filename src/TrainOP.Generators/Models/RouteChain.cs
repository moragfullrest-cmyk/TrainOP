using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Represents a detected TrainRoute station chain anchored at a root expression.
    /// </summary>
    internal sealed class RouteChain
    {
        /// <summary>
        /// Creates a route chain with an anchor and ordered station links.
        /// </summary>
        public RouteChain(RouteChainAnchor anchor, ImmutableArray<StationChainLink> stations)
        {
            Anchor = anchor;
            Stations = stations;
        }

        public RouteChainAnchor Anchor { get; }

        public Location AnchorLocation => Anchor.Location;

        public ImmutableArray<StationChainLink> Stations { get; }
    }
}
