using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
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
            IReadOnlyDictionary<string, RouteSite> stationByKey,
            out ChainConstructor constructor,
            out RouteChain chain)
        {
            constructor = null;
            chain = null;
            if (origin == null || semanticModel == null)
            {
                return false;
            }

            constructor = new ChainConstructor();

            if (origin is LocalBinding localBinding)
            {
                return TryConnectLocalBinding(
                    localBinding,
                    semanticModel,
                    stationByKey,
                    constructor,
                    out chain);
            }

            if (origin is CreationSeed || origin is FactoryCall)
            {
                return TryConnectFluentOrigin(
                    origin,
                    semanticModel,
                    stationByKey,
                    constructor,
                    out chain);
            }

            return false;
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
            IReadOnlyDictionary<string, RouteSite> stationByKey,
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
            else if (!constructor.TryAdd(localBinding))
            {
                return false;
            }

            var legacyStations = RouteOriginWindow.CollectLocalStatementStationLinks(
                localBinding.Identifier,
                semanticModel,
                stationByKey);

            if (legacyStations.Length == 0
                || !TryAppendStations(constructor, localBinding, legacyStations))
            {
                return false;
            }

            return LegacyRoutePartAdapter.TryToRouteChain(constructor, out chain)
                && chain.Stations.Length > 0;
        }

        private static bool TryConnectFluentOrigin(
            IRoutePart origin,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationByKey,
            ChainConstructor constructor,
            out RouteChain chain)
        {
            chain = null;
            var root = GetFluentRoot(origin);
            if (root == null)
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
                    GetContainingMethod(origin));

                if (!constructor.TryBind(origin, binding, out _))
                {
                    return false;
                }

                var collected = RouteOriginWindow.CollectLocalStatementStationLinks(
                    assignedLocal,
                    semanticModel,
                    stationByKey);

                if (collected.Length == 0
                    || !TryAppendStations(constructor, binding, collected))
                {
                    return false;
                }

                return LegacyRoutePartAdapter.TryToRouteChain(constructor, out chain)
                    && chain.Stations.Length > 0;
            }

            // Pure fluent walk from origin root.
            if (!constructor.TryAdd(origin))
            {
                return false;
            }

            var stations = ImmutableArray.CreateBuilder<StationChainLink>();
            var current = root;
            while (RouteChainWalker.TryAdvanceChain(
                current,
                semanticModel,
                stations,
                out current,
                null,
                stationByKey))
            {
            }

            if (stations.Count == 0
                || !TryAppendStations(constructor, origin, stations.ToImmutable()))
            {
                return false;
            }

            return LegacyRoutePartAdapter.TryToRouteChain(constructor, out chain)
                && chain.Stations.Length > 0;
        }

        private static bool TryAppendStations(
            ChainConstructor constructor,
            IRoutePart upstream,
            ImmutableArray<StationChainLink> legacyStations)
        {
            var currentUpstream = upstream;
            foreach (var legacy in legacyStations)
            {
                var link = StationLink.FromStationChainLink(legacy);
                if (link == null)
                {
                    continue;
                }

                if (!constructor.TryAppend(currentUpstream, link, out _))
                {
                    return false;
                }

                currentUpstream = link;
            }

            return true;
        }

        private static ExpressionSyntax GetFluentRoot(IRoutePart origin)
        {
            switch (origin)
            {
                case CreationSeed seed:
                    return seed.Root;
                case FactoryCall factoryCall:
                    return factoryCall.Root;
                default:
                    return null;
            }
        }

        private static IMethodSymbol GetContainingMethod(IRoutePart origin)
        {
            switch (origin)
            {
                case CreationSeed seed:
                    return seed.ContainingMethod;
                case FactoryCall factoryCall:
                    return factoryCall.ContainingMethod;
                default:
                    return null;
            }
        }
    }
}
