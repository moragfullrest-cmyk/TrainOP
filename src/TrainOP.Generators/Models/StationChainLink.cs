using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Models
{
    internal sealed class StationChainLink
    {
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
