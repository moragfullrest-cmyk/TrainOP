using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Parts;

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
            IReadOnlyDictionary<string, StationLink> stationSitesByKey = null)
        {
            next = current;

            // Builtin RedSignal-only ServiceStation is not a data-oriented overlay
            // (no RegisterStation ordinal). Still advance so factory-path simulation
            // can reach the fluent endpoint instead of reporting TOP013.
            if (TryGetDirectRouteHandlerInvocation(
                    current,
                    HandlerStationKind.ServiceStation,
                    out var serviceInvocation))
            {
                StationLinkMaterializer.TryMaterializeServiceStation(
                    serviceInvocation,
                    semanticModel,
                    stationSitesByKey,
                    out var serviceLink);
                RecordStep(stations, chainedInvocations, serviceInvocation, serviceLink);
                next = serviceInvocation;
                return true;
            }

            if (!TryGetNextStationInvocation(current, out var stationInvocation))
            {
                return false;
            }

            StationLinkMaterializer.TryMaterializeStation(
                stationInvocation,
                semanticModel,
                stationSitesByKey,
                out var stationLink);
            RecordStep(stations, chainedInvocations, stationInvocation, stationLink);
            next = stationInvocation;
            return true;
        }

        private static void RecordStep(
            ImmutableArray<StationLink>.Builder stations,
            ImmutableArray<InvocationExpressionSyntax>.Builder chainedInvocations,
            InvocationExpressionSyntax invocation,
            StationLink link)
        {
            if (link != null)
            {
                stations?.Add(link);
            }

            chainedInvocations?.Add(invocation);
        }

        private static bool TryGetNextStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            stationInvocation = null;

            if (TryGetDirectRouteHandlerInvocation(current, HandlerStationKind.Station, out stationInvocation))
            {
                return true;
            }

            if (StationSyntaxHelper.TryGetRouteHandlerInvocation(current, out var transparentInvocation)
                && transparentInvocation.Expression is MemberAccessExpressionSyntax memberAccess
                && IsTransparentRouteMethod(memberAccess.Name.Identifier.ValueText))
            {
                return TryGetNextStationInvocation(transparentInvocation, out stationInvocation);
            }

            return false;
        }

        private static bool TryGetDirectRouteHandlerInvocation(
            ExpressionSyntax current,
            HandlerStationKind stationKind,
            out InvocationExpressionSyntax routeHandlerInvocation)
        {
            routeHandlerInvocation = null;
            if (!StationSyntaxHelper.TryGetRouteHandlerInvocation(current, out var invocation)
                || !StationSyntaxHelper.TryParseRouteHandlerInvocation(invocation, out var parsedKind, out _)
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
