using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Models
{
    internal sealed class RouteChain
    {
        public RouteChain(Location anchorLocation, ImmutableArray<StationChainLink> stations)
        {
            AnchorLocation = anchorLocation;
            Stations = stations;
        }

        public Location AnchorLocation { get; }

        public ImmutableArray<StationChainLink> Stations { get; }
    }
}
