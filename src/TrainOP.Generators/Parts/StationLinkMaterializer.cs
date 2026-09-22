using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Materializes a <see cref="StationLink"/> from a Station / ServiceStation invocation.
    /// </summary>
    internal static class StationLinkMaterializer
    {
        /// <summary>
        /// Attempts to materialize either a Station or ServiceStation link.
        /// </summary>
        public static bool TryMaterialize(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            out StationLink link)
        {
            if (TryMaterializeServiceStation(invocation, semanticModel, stationSitesByKey, out link))
            {
                return true;
            }

            return TryMaterializeStation(invocation, semanticModel, stationSitesByKey, out link);
        }

        /// <summary>
        /// Attempts to materialize without prebuilt sites (semantic parse only).
        /// </summary>
        public static bool TryMaterialize(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            out StationLink link)
        {
            return TryMaterialize(invocation, semanticModel, stationSitesByKey: null, out link);
        }

        /// <summary>
        /// Attempts to materialize a <c>.Station</c> link.
        /// </summary>
        public static bool TryMaterializeStation(
            InvocationExpressionSyntax stationInvocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            out StationLink link)
        {
            link = null;
            if (stationInvocation == null || semanticModel == null)
            {
                return false;
            }

            if (TryGetPrebuiltStationSite(
                    stationInvocation,
                    stationSitesByKey,
                    RouteSiteKind.Station,
                    out var site))
            {
                link = new StationLink(
                    StationLinkKind.Station,
                    site.StationName,
                    stationInvocation.ArgumentList.Arguments[0].GetLocation(),
                    site.HandlerLocation,
                    site.HandlerBinding,
                    stationInvocation);
                return true;
            }

            if (StationSyntaxHelper.TryGetDataStationInvocation(
                    stationInvocation,
                    semanticModel,
                    out var stationName,
                    out var handlerLocation,
                    out var handlerBinding))
            {
                link = new StationLink(
                    StationLinkKind.Station,
                    stationName,
                    stationInvocation.ArgumentList.Arguments[0].GetLocation(),
                    handlerLocation,
                    handlerBinding,
                    stationInvocation);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to materialize a <c>.ServiceStation</c> link.
        /// </summary>
        public static bool TryMaterializeServiceStation(
            InvocationExpressionSyntax serviceInvocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            out StationLink link)
        {
            link = null;
            if (serviceInvocation == null || semanticModel == null)
            {
                return false;
            }

            if (TryGetPrebuiltStationSite(
                    serviceInvocation,
                    stationSitesByKey,
                    RouteSiteKind.ServiceStation,
                    out var site))
            {
                link = new StationLink(
                    StationLinkKind.ServiceStation,
                    site.StationName,
                    site.HandlerLocation,
                    site.HandlerLocation,
                    site.HandlerBinding,
                    serviceInvocation);
                return true;
            }

            if (StationSyntaxHelper.TryGetDataServiceStationInvocation(
                    serviceInvocation,
                    semanticModel,
                    out var serviceStationName,
                    out var serviceHandlerLocation,
                    out var serviceHandlerBinding))
            {
                link = new StationLink(
                    StationLinkKind.ServiceStation,
                    serviceStationName,
                    serviceHandlerLocation,
                    serviceHandlerLocation,
                    serviceHandlerBinding,
                    serviceInvocation);
                return true;
            }

            return false;
        }

        private static bool TryGetPrebuiltStationSite(
            InvocationExpressionSyntax invocation,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            RouteSiteKind expectedKind,
            out RouteSite site)
        {
            site = null;
            if (stationSitesByKey == null || invocation == null)
            {
                return false;
            }

            var key = ChainSiteBindingLookup.BuildLocationKey(invocation.GetLocation());
            if (key.Length == 0
                || !stationSitesByKey.TryGetValue(key, out site)
                || site.Kind != expectedKind
                || site.HandlerBinding == null)
            {
                site = null;
                return false;
            }

            return true;
        }
    }
}
