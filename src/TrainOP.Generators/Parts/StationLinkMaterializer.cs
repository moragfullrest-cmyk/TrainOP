using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using TrainOP.Generators.Chain;

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
            IReadOnlyDictionary<string, StationLink> stationLinksByKey,
            out StationLink link)
        {
            if (TryMaterializeServiceStation(invocation, semanticModel, stationLinksByKey, out link))
            {
                return true;
            }

            return TryMaterializeStation(invocation, semanticModel, stationLinksByKey, out link);
        }

        /// <summary>
        /// Attempts to materialize without a prebuilt link cache (semantic parse only).
        /// </summary>
        public static bool TryMaterialize(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            out StationLink link)
        {
            return TryMaterialize(invocation, semanticModel, stationLinksByKey: null, out link);
        }

        /// <summary>
        /// Attempts to materialize a <c>.Station</c> link.
        /// </summary>
        public static bool TryMaterializeStation(
            InvocationExpressionSyntax stationInvocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, StationLink> stationLinksByKey,
            out StationLink link)
        {
            link = null;
            if (stationInvocation == null || semanticModel == null)
            {
                return false;
            }

            if (TryGetCachedStationLink(
                    stationInvocation,
                    stationLinksByKey,
                    StationLinkKind.Station,
                    out link))
            {
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
            IReadOnlyDictionary<string, StationLink> stationLinksByKey,
            out StationLink link)
        {
            link = null;
            if (serviceInvocation == null || semanticModel == null)
            {
                return false;
            }

            if (TryGetCachedStationLink(
                    serviceInvocation,
                    stationLinksByKey,
                    StationLinkKind.ServiceStation,
                    out link))
            {
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

        private static bool TryGetCachedStationLink(
            InvocationExpressionSyntax invocation,
            IReadOnlyDictionary<string, StationLink> stationLinksByKey,
            StationLinkKind expectedKind,
            out StationLink link)
        {
            link = null;
            if (stationLinksByKey == null || invocation == null)
            {
                return false;
            }

            var key = ChainSiteBindingLookup.BuildLocationKey(invocation.GetLocation());
            if (key.Length == 0
                || !stationLinksByKey.TryGetValue(key, out link)
                || link.Kind != expectedKind
                || link.Handler == null)
            {
                link = null;
                return false;
            }

            return true;
        }
    }
}
