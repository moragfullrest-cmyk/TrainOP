using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Route
{
    /// <summary>
    /// A single node in the TrainRoute discovery graph: either a chain origin or a handler call site.
    /// </summary>
    internal sealed class RouteSite
    {
        private RouteSite(
            RouteSiteKind kind,
            ExpressionSyntax expression,
            Location identityLocation,
            InvocationExpressionSyntax invocation,
            ExpressionSyntax receiver,
            string stationName,
            StationHandlerBinding handlerBinding,
            Location handlerLocation,
            IRoutePart originPart,
            IMethodSymbol containingMethod,
            IMethodSymbol factoryMethod,
            ImmutableArray<WagonBinding> initialWagons)
        {
            Kind = kind;
            Expression = expression;
            IdentityLocation = identityLocation;
            Invocation = invocation;
            Receiver = receiver;
            StationName = stationName;
            HandlerBinding = handlerBinding;
            HandlerLocation = handlerLocation;
            OriginPart = originPart;
            ContainingMethod = containingMethod;
            FactoryMethod = factoryMethod;
            InitialWagons = initialWagons;
        }

        public RouteSiteKind Kind { get; }

        public ExpressionSyntax Expression { get; }

        public Location IdentityLocation { get; }

        public InvocationExpressionSyntax Invocation { get; }

        public ExpressionSyntax Receiver { get; }

        public string StationName { get; }

        public StationHandlerBinding HandlerBinding { get; }

        public Location HandlerLocation { get; }

        /// <summary>
        /// Origin part when <see cref="Kind"/> is <see cref="RouteSiteKind.Anchor"/>.
        /// </summary>
        public IRoutePart OriginPart { get; }

        public IMethodSymbol ContainingMethod { get; }

        public IMethodSymbol FactoryMethod { get; }

        public ImmutableArray<WagonBinding> InitialWagons { get; }

        public bool IsStation =>
            Kind == RouteSiteKind.Station || Kind == RouteSiteKind.ServiceStation;

        /// <summary>
        /// Creates a station or service-station call site discovered from syntax.
        /// </summary>
        public static RouteSite CreateStation(
            RouteSiteKind kind,
            InvocationExpressionSyntax invocation,
            ExpressionSyntax receiver,
            string stationName,
            StationHandlerBinding handlerBinding,
            Location handlerLocation)
        {
            return new RouteSite(
                kind,
                invocation,
                invocation.GetLocation(),
                invocation,
                receiver,
                stationName,
                handlerBinding,
                handlerLocation,
                null,
                null,
                null,
                default);
        }

        /// <summary>
        /// Creates a chain-origin site from a materialized origin part.
        /// </summary>
        public static RouteSite CreateAnchor(IRoutePart origin)
        {
            if (origin == null || !RouteOriginPorts.TryGetRoot(origin, out var root))
            {
                return null;
            }

            return new RouteSite(
                RouteSiteKind.Anchor,
                root,
                origin.Location,
                null,
                null,
                null,
                null,
                null,
                origin,
                RouteOriginPorts.GetContainingMethod(origin),
                RouteOriginPorts.GetFactoryMethod(origin),
                RouteOriginPorts.GetInitialWagons(origin));
        }
    }
}
