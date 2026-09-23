using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Thin fluent peel: advance one Station / ServiceStation step.
    /// </summary>
    /// <remarks>
    /// Materialize of station links is owned by <see cref="StationLinkMaterializer"/>;
    /// this type only peels the next invocation and appends station links.
    /// </remarks>
    internal static class RouteChainPeel
    {
        /// <summary>
        /// Advances along a route chain by resolving the next station or service-station invocation.
        /// </summary>
        public static bool TryAdvanceChain(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            ImmutableArray<StationLink>.Builder stations,
            out ExpressionSyntax next,
            ImmutableArray<InvocationExpressionSyntax>.Builder chainedInvocations,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey = null)
        {
            next = current;

            // Builtin RedSignal-only ServiceStation is not a data-oriented overlay
            // (no RegisterStation ordinal). Still advance so factory-path simulation
            // can reach the fluent endpoint instead of reporting TOP013.
            if (TryGetDirectServiceStationInvocation(current, out var serviceInvocation))
            {
                if (TryCreateServiceStationLink(
                    serviceInvocation,
                    semanticModel,
                    stationSitesByKey,
                    out var serviceLink))
                {
                    stations?.Add(serviceLink);
                }

                chainedInvocations?.Add(serviceInvocation);
                next = serviceInvocation;
                return true;
            }

            if (!TryGetNextStationInvocation(current, out var stationInvocation))
            {
                return false;
            }

            if (TryCreateStationLink(stationInvocation, semanticModel, stationSitesByKey, out var stationLink))
            {
                stations?.Add(stationLink);
            }

            chainedInvocations?.Add(stationInvocation);
            next = stationInvocation;
            return true;
        }

        private static bool TryCreateServiceStationLink(
            InvocationExpressionSyntax serviceInvocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            out StationLink link)
        {
            return StationLinkMaterializer.TryMaterializeServiceStation(
                serviceInvocation,
                semanticModel,
                stationSitesByKey,
                out link);
        }

        private static bool TryCreateStationLink(
            InvocationExpressionSyntax stationInvocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            out StationLink link)
        {
            return StationLinkMaterializer.TryMaterializeStation(
                stationInvocation,
                semanticModel,
                stationSitesByKey,
                out link);
        }

        private static bool TryGetDirectServiceStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax serviceStationInvocation)
        {
            return TryGetDirectRouteHandlerInvocation(
                current,
                HandlerStationKind.ServiceStation,
                out serviceStationInvocation);
        }

        private static bool TryGetNextStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            return TryGetNextStationInvocationCore(current, out stationInvocation);
        }

        private static bool TryGetNextStationInvocationCore(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            stationInvocation = null;

            if (TryGetDirectStationInvocation(current, out stationInvocation))
            {
                return true;
            }

            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!IsTransparentRouteMethod(memberAccess.Name.Identifier.ValueText))
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
            {
                return false;
            }

            if (memberAccess.Parent is not InvocationExpressionSyntax transparentInvocation)
            {
                return false;
            }

            if (!ReferenceEquals(transparentInvocation.Expression, memberAccess))
            {
                return false;
            }

            return TryGetNextStationInvocationCore(transparentInvocation, out stationInvocation);
        }

        private static bool TryGetDirectStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            return TryGetDirectRouteHandlerInvocation(current, HandlerStationKind.Station, out stationInvocation);
        }

        private static bool TryGetDirectRouteHandlerInvocation(
            ExpressionSyntax current,
            HandlerStationKind stationKind,
            out InvocationExpressionSyntax routeHandlerInvocation)
        {
            routeHandlerInvocation = null;

            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
            {
                return false;
            }

            if (memberAccess.Parent is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            if (!ReferenceEquals(invocation.Expression, memberAccess))
            {
                return false;
            }

            if (!StationSyntaxHelper.TryParseRouteHandlerInvocation(invocation, out var parsedKind, out _)
                || parsedKind != stationKind)
            {
                return false;
            }

            routeHandlerInvocation = invocation;
            return true;
        }

        private static bool IsTransparentRouteMethod(string methodName)
        {
            return StationSyntaxHelper.IsServiceStationMethodName(methodName);
        }
    }
}
