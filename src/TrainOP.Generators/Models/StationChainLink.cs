using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Links a station name and handler binding within a route chain.
    /// </summary>
    internal sealed class StationChainLink
    {
        /// <summary>
        /// Creates a chain link for a station invocation and its handler binding.
        /// </summary>
        public StationChainLink(
            string stationName,
            Location stationNameLocation,
            Location handlerLocation,
            StationHandlerBinding handler)
        {
            StationName = stationName;
            StationNameLocation = stationNameLocation;
            HandlerLocation = handlerLocation;
            Handler = handler;
        }

        public string StationName { get; }

        public Location StationNameLocation { get; }

        public Location HandlerLocation { get; }

        public StationHandlerBinding Handler { get; }
    }
}
