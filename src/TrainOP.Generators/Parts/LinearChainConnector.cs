using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Connects an origin part to linear <see cref="StationLink"/> steps (fluent + statement-local /
    /// SL-fold), validating each edge via <see cref="ChainConstructor"/>.
    /// </summary>
    /// <remarks>
    /// Primary linear path for <c>RouteGraphAssembler</c>. Station discovery uses
    /// <see cref="RouteOriginWindow"/> / <see cref="RouteChainPeel"/> helpers.
    /// </remarks>
    internal static class LinearChainConnector
    {
        /// <summary>
        /// Builds a linear chain from <paramref name="origin"/> by collecting stations and
        /// connecting Bind/Append edges.
        /// </summary>
        public static bool TryConnect(
            IRoutePart origin,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, StationLink> stationByKey,
            out ChainConstructor constructor,
            out RouteChain chain)
        {
            constructor = null;
            chain = null;
            if (semanticModel == null)
            {
                return false;
            }

            constructor = new ChainConstructor();

            return origin switch
            {
                LocalBinding localBinding => TryConnectLocalBinding(
                    localBinding,
                    semanticModel,
                    stationByKey,
                    constructor,
                    out chain),
                CreationSeed or FactoryCall => TryConnectFluentOrigin(
                    origin,
                    semanticModel,
                    stationByKey,
                    constructor,
                    out chain),
                _ => false
            };
        }

        /// <summary>
        /// Overload without prebuilt station sites.
        /// </summary>
        public static bool TryConnect(
            IRoutePart origin,
            SemanticModel semanticModel,
            out ChainConstructor constructor,
            out RouteChain chain)
        {
            return TryConnect(origin, semanticModel, stationByKey: null, out constructor, out chain);
        }

        private static bool TryConnectLocalBinding(
            LocalBinding localBinding,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, StationLink> stationByKey,
            ChainConstructor constructor,
            out RouteChain chain)
        {
            chain = null;

            if (localBinding.Origin != null)
            {
                if (!constructor.TryBind(localBinding.Origin, localBinding, out _))
                {
                    return false;
                }
            }
            else
            {
                constructor.Add(localBinding);
            }

            var stations = RouteOriginWindow.CollectLocalStatementStationLinks(
                localBinding.Identifier,
                semanticModel,
                stationByKey);

            return TryFinishChain(constructor, localBinding, stations, out chain);
        }

        private static bool TryConnectFluentOrigin(
            IRoutePart origin,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, StationLink> stationByKey,
            ChainConstructor constructor,
            out RouteChain chain)
        {
            chain = null;
            if (!RouteOriginPorts.TryGetRoot(origin, out var root))
            {
                return false;
            }

            // SL-3a/3b: fluent root assigned to a local — fold statement tail onto LocalBinding.
            if (RouteOriginWindow.TryFindLocalIdentifierAssignedFromFluentCreation(
                    root,
                    semanticModel,
                    out var assignedLocal))
            {
                var binding = new LocalBinding(
                    assignedLocal,
                    origin.Location,
                    origin,
                    RouteOriginPorts.GetContainingMethod(origin));

                if (!constructor.TryBind(origin, binding, out _))
                {
                    return false;
                }

                var collected = RouteOriginWindow.CollectLocalStatementStationLinks(
                    assignedLocal,
                    semanticModel,
                    stationByKey);

                return TryFinishChain(constructor, binding, collected, out chain);
            }

            // Pure fluent walk from origin root.
            constructor.Add(origin);

            var stations = ImmutableArray.CreateBuilder<StationLink>();
            var current = root;
            while (RouteChainPeel.TryAdvanceChain(
                current,
                semanticModel,
                stations,
                out current,
                null,
                stationByKey))
            {
            }

            return TryFinishChain(constructor, origin, stations.ToImmutable(), out chain);
        }

        private static bool TryFinishChain(
            ChainConstructor constructor,
            IRoutePart upstream,
            ImmutableArray<StationLink> stations,
            out RouteChain chain)
        {
            if (stations.Length == 0
                || !TryAppendStations(constructor, upstream, stations))
            {
                chain = null;
                return false;
            }

            return RouteOriginPorts.TryToRouteChain(constructor, out chain)
                && chain.Stations.Length > 0;
        }

        private static bool TryAppendStations(
            ChainConstructor constructor,
            IRoutePart upstream,
            ImmutableArray<StationLink> stations)
        {
            var currentUpstream = upstream;
            foreach (var link in stations)
            {
                if (!constructor.TryAppend(currentUpstream, link, out _))
                {
                    return false;
                }

                currentUpstream = link;
            }

            return true;
        }
    }
}
