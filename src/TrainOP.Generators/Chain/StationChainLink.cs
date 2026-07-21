using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators.Chain
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
            StationHandlerBinding handler,
            InvocationExpressionSyntax invocation)
        {
            StationName = stationName;
            StationNameLocation = stationNameLocation;
            HandlerLocation = handlerLocation;
            Handler = handler;
            Invocation = invocation;
            InvocationLocation = invocation?.GetLocation();
        }

        public string StationName { get; }

        public Location StationNameLocation { get; }

        public Location HandlerLocation { get; }

        public StationHandlerBinding Handler { get; }

        public InvocationExpressionSyntax Invocation { get; }

        public Location InvocationLocation { get; }
    }
}
