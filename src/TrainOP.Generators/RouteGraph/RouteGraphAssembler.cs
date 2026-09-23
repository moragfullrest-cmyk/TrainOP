using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Assembles <see cref="RouteGraph"/> instances from discovered <see cref="RouteSite"/> nodes.
    /// Prefer <see cref="BuildChainsStage"/> at call sites that only need the graph.
    /// </summary>
    internal static class RouteGraphAssembler
    {
        /// <summary>
        /// Builds route chains from discovered origins via <see cref="LinearChainConnector"/>
        /// (parts → Bind/Append), including join-assign locals.
        /// Station sites supply pre-resolved handler bindings for station materialize.
        /// </summary>
        public static RouteGraph Build(ImmutableArray<RouteSite> sites, Compilation compilation)
        {
            if (compilation == null || sites.IsDefaultOrEmpty)
            {
                return RouteGraph.Empty;
            }

            var stationSites = ImmutableArray.CreateBuilder<RouteSite>();
            var originByKey = new Dictionary<string, IRoutePart>(StringComparer.Ordinal);
            var stationByKey = new Dictionary<string, RouteSite>(StringComparer.Ordinal);

            for (var i = 0; i < sites.Length; i++)
            {
                var site = sites[i];
                if (site == null)
                {
                    continue;
                }

                if (site.IsStation)
                {
                    stationSites.Add(site);
                    var key = ChainSiteBindingLookup.BuildLocationKey(site.Invocation.GetLocation());
                    if (key.Length > 0)
                    {
                        stationByKey[key] = site;
                    }

                    continue;
                }

                if (site.Kind == RouteSiteKind.Anchor && site.OriginPart != null)
                {
                    RegisterOrigin(originByKey, site.OriginPart, compilation);
                }
            }

            var chains = ImmutableArray.CreateBuilder<RouteChain>();
            var chainIndex = new Dictionary<string, List<ChainSiteBinding>>(StringComparer.Ordinal);
            var chainsByInvocationKey = new Dictionary<string, RouteChain>(StringComparer.Ordinal);

            foreach (var origin in originByKey.Values)
            {
                var chain = BuildChain(origin, compilation, stationByKey);
                if (chain == null)
                {
                    continue;
                }

                chains.Add(chain);
                if (!TryResolveDispatchIdentity(origin, compilation, out var chainId, out var upstreamStationCount))
                {
                    // Factory schema without CallerChainKey/StationCount: do not emit wrong index-0 bindings.
                    continue;
                }

                for (var stationIndex = 0; stationIndex < chain.Stations.Length; stationIndex++)
                {
                    var station = chain.Stations[stationIndex];
                    if (station.InvocationLocation == null)
                    {
                        continue;
                    }

                    var binding = new ChainSiteBinding(
                        chainId,
                        upstreamStationCount + stationIndex,
                        station.StationName,
                        station.Invocation,
                        station.Handler);

                    var locationKey = ChainSiteBindingLookup.BuildLocationKey(station.InvocationLocation);
                    if (!chainIndex.TryGetValue(locationKey, out var list))
                    {
                        list = new List<ChainSiteBinding>();
                        chainIndex[locationKey] = list;
                    }

                    list.Add(binding);

                    var invocationKey = ChainSiteBindingLookup.BuildLocationKey(station.Invocation.GetLocation());
                    if (invocationKey.Length > 0)
                    {
                        chainsByInvocationKey[invocationKey] = chain;
                    }
                }
            }

            var immutableIndex = new Dictionary<string, ImmutableArray<ChainSiteBinding>>(StringComparer.Ordinal);
            foreach (var kvp in chainIndex)
            {
                immutableIndex[kvp.Key] = kvp.Value.ToImmutableArray();
            }

            return new RouteGraph(
                chains.ToImmutable(),
                immutableIndex,
                stationSites.ToImmutable(),
                chainsByInvocationKey);
        }

        private static bool TryResolveDispatchIdentity(
            IRoutePart origin,
            Compilation compilation,
            out string chainId,
            out int upstreamStationCount)
        {
            chainId = string.Empty;
            upstreamStationCount = 0;

            if (TryResolveDispatchFromOrigin(origin, compilation, out chainId, out upstreamStationCount))
            {
                return true;
            }

            // Residual (e.g. JoinSeed / creation) — key builder without factory upstream count.
            chainId = CallerChainKeyBuilder.Build(origin, compilation);
            upstreamStationCount = 0;
            return !string.IsNullOrEmpty(chainId);
        }

        private static bool TryResolveDispatchFromOrigin(
            IRoutePart origin,
            Compilation compilation,
            out string chainId,
            out int upstreamStationCount)
        {
            chainId = string.Empty;
            upstreamStationCount = 0;

            var factory = origin as FactoryCall
                ?? (origin as LocalBinding)?.Origin as FactoryCall;
            if (factory != null)
            {
                return factory.TryResolveDispatchIdentity(
                        compilation,
                        out chainId,
                        out upstreamStationCount)
                    && !string.IsNullOrEmpty(chainId);
            }

            // Join-assign local with stamped shared factory (no FactoryCall origin root).
            if (origin is LocalBinding localBinding && localBinding.FactoryMethod != null)
            {
                return FactoryDispatchMetadata.TryResolve(
                        localBinding.FactoryMethod,
                        compilation,
                        out chainId,
                        out upstreamStationCount)
                    && !string.IsNullOrEmpty(chainId);
            }

            return false;
        }

        private static RouteChain BuildChain(
            IRoutePart origin,
            Compilation compilation,
            IReadOnlyDictionary<string, RouteSite> stationByKey)
        {
            if (origin == null || !RouteOriginPorts.TryGetRoot(origin, out var root))
            {
                return null;
            }

            var semanticModel = compilation.GetSemanticModel(root.SyntaxTree);
            return LinearChainConnector.TryConnect(
                    origin,
                    semanticModel,
                    stationByKey,
                    out _,
                    out var chain)
                ? chain
                : null;
        }

        private static void RegisterOrigin(
            IDictionary<string, IRoutePart> originByKey,
            IRoutePart origin,
            Compilation compilation)
        {
            if (origin == null)
            {
                return;
            }

            var originKey = BuildOriginKey(origin, compilation);
            if (string.IsNullOrEmpty(originKey))
            {
                return;
            }

            if (!originByKey.TryGetValue(originKey, out var existing))
            {
                originByKey[originKey] = origin;
                return;
            }

            originByKey[originKey] = MergeOrigins(existing, origin);
        }

        private static string BuildOriginKey(IRoutePart origin, Compilation compilation)
        {
            var chainId = CallerChainKeyBuilder.Build(origin, compilation);
            if (string.IsNullOrEmpty(chainId)
                || !RouteOriginPorts.TryGetRoot(origin, out var root))
            {
                return string.Empty;
            }

            // Collapse all use-sites of the same origin into one chain key.
            // Origin stamp lives on Location (ctor / factory call site), not Root.SpanStart.
            var spanStart = RoutePartPreference.IsOriginKeyed(origin)
                && origin.Location != null
                    ? origin.Location.SourceSpan.Start
                    : root.SpanStart;

            return chainId + "@" + spanStart;
        }

        private static IRoutePart MergeOrigins(IRoutePart left, IRoutePart right)
        {
            var preferred = PreferOrigin(left, right);
            var other = ReferenceEquals(preferred, left) ? right : left;

            var preferredFactory = RouteOriginPorts.GetFactoryMethod(preferred);
            var otherFactory = RouteOriginPorts.GetFactoryMethod(other);
            if (preferredFactory == null && otherFactory != null)
            {
                return other;
            }

            return preferred;
        }

        private static IRoutePart PreferOrigin(IRoutePart left, IRoutePart right)
        {
            var leftScore = RoutePartPreference.Score(left);
            var rightScore = RoutePartPreference.Score(right);
            return rightScore > leftScore ? right : left;
        }
    }
}
