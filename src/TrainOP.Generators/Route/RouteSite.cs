using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Route
{
    /// <summary>
    /// A single node in the TrainRoute discovery graph: either a chain anchor or a handler call site.
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
            RouteChainAnchorKind anchorKind,
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
            AnchorKind = anchorKind;
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

        public RouteChainAnchorKind AnchorKind { get; }

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
                default,
                null,
                null,
                default);
        }

        /// <summary>
        /// Creates a chain anchor discovered from syntax.
        /// </summary>
        public static RouteSite CreateAnchor(
            RouteChainAnchor anchor)
        {
            return new RouteSite(
                RouteSiteKind.Anchor,
                anchor.Root,
                anchor.Location,
                null,
                null,
                null,
                null,
                null,
                anchor.Kind,
                anchor.ContainingMethod,
                anchor.FactoryMethod,
                anchor.InitialWagons);
        }

        /// <summary>
        /// Projects this site into a <see cref="RouteChainAnchor"/> when it represents a chain root.
        /// </summary>
        public RouteChainAnchor ToAnchor()
        {
            return new RouteChainAnchor(
                AnchorKind,
                Expression,
                IdentityLocation,
                ContainingMethod,
                FactoryMethod,
                InitialWagons);
        }
    }
}
