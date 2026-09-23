using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Single <c>.Station</c> / <c>.ServiceStation</c> step in a chain.
    /// </summary>
    internal sealed class StationLink : IRoutePart
    {
        /// <summary>
        /// Creates a station-link part from a materialized invocation.
        /// </summary>
        public StationLink(
            StationLinkKind kind,
            string stationName,
            Location stationNameLocation,
            Location handlerLocation,
            StationHandlerBinding handler,
            InvocationExpressionSyntax invocation)
        {
            Kind = kind;
            StationName = stationName;
            StationNameLocation = stationNameLocation;
            HandlerLocation = handlerLocation;
            Handler = handler;
            Invocation = invocation;
            Location = invocation?.GetLocation();
        }

        /// <summary>
        /// Station vs service-station role.
        /// </summary>
        public StationLinkKind Kind { get; }

        /// <summary>
        /// Resolved station name.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Location of the station-name argument (or handler location for service stations).
        /// </summary>
        public Location StationNameLocation { get; }

        /// <summary>
        /// Location of the handler expression.
        /// </summary>
        public Location HandlerLocation { get; }

        /// <summary>
        /// Bound handler schema.
        /// </summary>
        public StationHandlerBinding Handler { get; }

        /// <summary>
        /// Station / service-station invocation.
        /// </summary>
        public InvocationExpressionSyntax Invocation { get; }

        /// <inheritdoc />
        public Location Location { get; }

        /// <summary>
        /// Invocation location (alias of <see cref="Location"/> for call-site indexing).
        /// </summary>
        public Location InvocationLocation => Location;
    }
}
