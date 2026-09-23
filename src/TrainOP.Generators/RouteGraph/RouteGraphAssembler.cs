using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Assembles <see cref="RouteGraph"/> instances from discovered route parts.
    /// Prefer <see cref="BuildChainsStage"/> at call sites that only need the graph.
    /// </summary>
    internal static class RouteGraphAssembler
    {
        /// <summary>
        /// Builds route chains from discovered origins via <see cref="LinearChainConnector"/>
        /// (parts → Bind/Append), including join-assign locals.
        /// Station links supply pre-resolved handlers for station materialize.
        /// </summary>
        public static RouteGraph Build(ImmutableArray<IRoutePart> parts, Compilation compilation)
        {
            if (compilation == null || parts.IsDefaultOrEmpty)
            {
                return RouteGraph.Empty;
            }

            var stationLinks = ImmutableArray.CreateBuilder<StationLink>();
            var originByKey = new Dictionary<string, IRoutePart>(StringComparer.Ordinal);
            var stationByKey = new Dictionary<string, StationLink>(StringComparer.Ordinal);

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part is StationLink stationLink)
                {
                    RegisterStation(stationLinks, stationByKey, stationLink);
                    continue;
                }

                if (RouteOriginPorts.IsOriginPart(part))
                {
                    RegisterOrigin(originByKey, part, compilation);
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

                    RegisterBinding(chainIndex, station.InvocationLocation, binding);
                    RegisterByLocation(chainsByInvocationKey, station.Invocation.GetLocation(), chain);
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
                stationLinks.ToImmutable(),
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
            IReadOnlyDictionary<string, StationLink> stationByKey)
        {
            if (!RouteOriginPorts.TryGetRoot(origin, out var root))
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

        private static void RegisterStation(
            ImmutableArray<StationLink>.Builder stationLinks,
            IDictionary<string, StationLink> stationByKey,
            StationLink stationLink)
        {
            stationLinks.Add(stationLink);
            RegisterByLocation(stationByKey, stationLink.Invocation.GetLocation(), stationLink);
        }

        private static void RegisterBinding(
            IDictionary<string, List<ChainSiteBinding>> chainIndex,
            Location location,
            ChainSiteBinding binding)
        {
            var key = ChainSiteBindingLookup.BuildLocationKey(location);
            if (!chainIndex.TryGetValue(key, out var list))
            {
                list = new List<ChainSiteBinding>();
                chainIndex[key] = list;
            }

            list.Add(binding);
        }

        private static void RegisterByLocation<TValue>(
            IDictionary<string, TValue> index,
            Location location,
            TValue value)
        {
            var key = ChainSiteBindingLookup.BuildLocationKey(location);
            if (key.Length > 0)
            {
                index[key] = value;
            }
        }

        private static void RegisterOrigin(
            IDictionary<string, IRoutePart> originByKey,
            IRoutePart origin,
            Compilation compilation)
        {
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
