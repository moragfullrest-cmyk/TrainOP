using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Route;
namespace TrainOP.Generators
{
    /// <summary>
    /// Assembles <see cref="RouteGraph"/> instances from discovered <see cref="RouteSite"/> nodes.
    /// </summary>
    internal static class RouteGraphAssembler
    {
        /// <summary>
        /// Builds route chains from discovered anchors via forward chain walk;
        /// station sites supply pre-resolved handler bindings for <see cref="RouteChainWalker.TryAdvanceChain"/>.
        /// </summary>
        public static RouteGraph Build(ImmutableArray<RouteSite> sites, Compilation compilation)
        {
            if (compilation == null || sites.IsDefaultOrEmpty)
            {
                return RouteGraph.Empty;
            }

            var stationSites = ImmutableArray.CreateBuilder<RouteSite>();
            var anchorByKey = new Dictionary<string, RouteChainAnchor>(StringComparer.Ordinal);
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

                if (site.Kind == RouteSiteKind.Anchor)
                {
                    RegisterAnchor(anchorByKey, site.ToAnchor());
                }
            }

            var chains = ImmutableArray.CreateBuilder<RouteChain>();
            var chainIndex = new Dictionary<string, List<ChainSiteBinding>>(StringComparer.Ordinal);
            var chainsByInvocationKey = new Dictionary<string, RouteChain>(StringComparer.Ordinal);

            foreach (var anchor in anchorByKey.Values)
            {
                var chain = BuildChain(anchor, compilation, stationByKey);
                if (chain == null)
                {
                    continue;
                }

                chains.Add(chain);
                var chainId = CallerChainKeyBuilder.Build(anchor);
                for (var stationIndex = 0; stationIndex < chain.Stations.Length; stationIndex++)
                {
                    var station = chain.Stations[stationIndex];
                    if (station.InvocationLocation == null)
                    {
                        continue;
                    }

                    var binding = new ChainSiteBinding(
                        chainId,
                        stationIndex,
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

        private static RouteChain BuildChain(
            RouteChainAnchor anchor,
            Compilation compilation,
            IReadOnlyDictionary<string, RouteSite> stationByKey)
        {
            var semanticModel = compilation.GetSemanticModel(anchor.Root.SyntaxTree);
            var stations = ImmutableArray.CreateBuilder<StationChainLink>();
            var current = anchor.Root;

            while (RouteChainWalker.TryAdvanceChain(
                current,
                semanticModel,
                stations,
                out current,
                null,
                stationByKey)) ;

            return stations.Count > 0
                ? new RouteChain(anchor, stations.ToImmutable())
                : null;
        }

        private static void RegisterAnchor(
            IDictionary<string, RouteChainAnchor> anchorByKey,
            RouteChainAnchor anchor)
        {
            if (anchor == null)
            {
                return;
            }

            var anchorKey = BuildAnchorKey(anchor);
            if (string.IsNullOrEmpty(anchorKey))
            {
                return;
            }

            if (!anchorByKey.TryGetValue(anchorKey, out var existing))
            {
                anchorByKey[anchorKey] = anchor;
                return;
            }

            anchorByKey[anchorKey] = MergeAnchors(existing, anchor);
        }

        private static string BuildAnchorKey(RouteChainAnchor anchor)
        {
            var chainId = CallerChainKeyBuilder.Build(anchor);
            if (string.IsNullOrEmpty(chainId))
            {
                return string.Empty;
            }

            return chainId + "@" + anchor.Root.SpanStart;
        }

        private static RouteChainAnchor MergeAnchors(RouteChainAnchor left, RouteChainAnchor right)
        {
            var preferred = PreferAnchor(left, right);
            var other = ReferenceEquals(preferred, left) ? right : left;

            if (preferred.FactoryMethod == null && other.FactoryMethod != null)
            {
                return new RouteChainAnchor(
                    other.Kind,
                    other.Root,
                    other.Location,
                    other.ContainingMethod ?? preferred.ContainingMethod,
                    other.FactoryMethod,
                    other.InitialWagons.IsDefaultOrEmpty ? preferred.InitialWagons : other.InitialWagons);
            }

            return preferred;
        }

        private static RouteChainAnchor PreferAnchor(RouteChainAnchor left, RouteChainAnchor right)
        {
            var leftScore = GetAnchorPreferenceScore(left);
            var rightScore = GetAnchorPreferenceScore(right);
            return rightScore > leftScore ? right : left;
        }

        private static int GetAnchorPreferenceScore(RouteChainAnchor anchor)
        {
            return anchor.Kind switch
            {
                RouteChainAnchorKind.LocalVariable => 4,
                RouteChainAnchorKind.MethodInvocation => 3,
                RouteChainAnchorKind.FactorySchema => 3,
                RouteChainAnchorKind.ObjectCreation => 2,
                RouteChainAnchorKind.BranchJoin => 1,
                _ => 0,
            };
        }

    }
}
