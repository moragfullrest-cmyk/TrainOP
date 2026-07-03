using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Represents a detected TrainRoute station chain anchored at its creation site.
    /// </summary>
    internal sealed class RouteChain
    {
        /// <summary>
        /// Creates a route chain with an anchor location and ordered station links.
        /// </summary>
        public RouteChain(Location anchorLocation, ImmutableArray<StationChainLink> stations)
        {
            AnchorLocation = anchorLocation;
            Stations = stations;
        }

        public Location AnchorLocation { get; }

        public ImmutableArray<StationChainLink> Stations { get; }
    }
}
