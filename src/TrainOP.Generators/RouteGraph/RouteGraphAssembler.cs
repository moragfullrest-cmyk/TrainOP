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
    /// Primary facade for stage 4 BuildChains (see also <see cref="BuildChainsStage"/>).
    /// </summary>
    internal static class RouteGraphAssembler
    {
        /// <summary>
        /// Builds route chains from discovered anchors via <see cref="LinearChainConnector"/>
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
                    RegisterAnchor(anchorByKey, site.ToAnchor(), compilation);
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
                if (!TryResolveDispatchIdentity(anchor, compilation, out var chainId, out var upstreamStationCount))
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
            RouteChainAnchor anchor,
            Compilation compilation,
            out string chainId,
            out int upstreamStationCount)
        {
            chainId = string.Empty;
            upstreamStationCount = 0;

            var semanticModel = compilation.GetSemanticModel(anchor.Root.SyntaxTree);
            if (TryToOriginPart(anchor, semanticModel, out var origin)
                && TryResolveDispatchFromOrigin(origin, compilation, out chainId, out upstreamStationCount))
            {
                return true;
            }

            // Identifier-rooted shared factory (ternary/switch join-assign): no FactoryCall root,
            // but FactoryMethod is stamped on the anchor — resolve dispatch without inventing call-site.
            if (anchor.FactoryMethod != null
                && FactoryDispatchMetadata.TryResolve(
                    anchor.FactoryMethod,
                    compilation,
                    out chainId,
                    out upstreamStationCount)
                && !string.IsNullOrEmpty(chainId))
            {
                return true;
            }

            // Residual (e.g. BranchJoin / creation) — key builder without factory upstream count.
            chainId = CallerChainKeyBuilder.Build(anchor, compilation);
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

            if (!LegacyRoutePartAdapter.TryToLegacyAnchor(origin, out var legacy))
            {
                return false;
            }

            chainId = CallerChainKeyBuilder.Build(legacy, compilation);
            upstreamStationCount = 0;
            return !string.IsNullOrEmpty(chainId);
        }

        private static RouteChain BuildChain(
            RouteChainAnchor anchor,
            Compilation compilation,
            IReadOnlyDictionary<string, RouteSite> stationByKey)
        {
            var semanticModel = compilation.GetSemanticModel(anchor.Root.SyntaxTree);

            if (!TryToOriginPart(anchor, semanticModel, out var origin))
            {
                return null;
            }

            return LinearChainConnector.TryConnect(
                    origin,
                    semanticModel,
                    stationByKey,
                    out _,
                    out var chain)
                ? chain
                : null;
        }

        /// <summary>
        /// Maps a legacy anchor to an origin part by root/port shape (no Kind switch).
        /// </summary>
        private static bool TryToOriginPart(
            RouteChainAnchor anchor,
            SemanticModel semanticModel,
            out IRoutePart origin)
        {
            origin = null;
            if (anchor?.Root == null || semanticModel == null)
            {
                return false;
            }

            if (anchor.Root is IdentifierNameSyntax identifier
                && LocalBindingMaterializer.TryMaterialize(
                    identifier,
                    semanticModel,
                    out var localBinding))
            {
                origin = localBinding;
                return true;
            }

            if (FactoryCall.TryFromLegacyAnchor(anchor, out var factoryCall))
            {
                origin = factoryCall;
                return true;
            }

            if (anchor.Root is ObjectCreationExpressionSyntax objectCreation)
            {
                origin = new CreationSeed(
                    objectCreation,
                    anchor.Location,
                    anchor.ContainingMethod);
                return true;
            }

            return false;
        }

        private static void RegisterAnchor(
            IDictionary<string, RouteChainAnchor> anchorByKey,
            RouteChainAnchor anchor,
            Compilation compilation)
        {
            if (anchor == null)
            {
                return;
            }

            var anchorKey = BuildAnchorKey(anchor, compilation);
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

        private static string BuildAnchorKey(RouteChainAnchor anchor, Compilation compilation)
        {
            var chainId = CallerChainKeyBuilder.Build(anchor, compilation);
            if (string.IsNullOrEmpty(chainId))
            {
                return string.Empty;
            }

            // Collapse all use-sites of the same origin into one chain key.
            // Origin stamp lives on Location (ctor / factory call site), not Root.SpanStart.
            var spanStart = RoutePartPreference.IsOriginKeyed(anchor)
                && anchor.Location != null
                    ? anchor.Location.SourceSpan.Start
                    : anchor.Root.SpanStart;

            return chainId + "@" + spanStart;
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
            var leftScore = RoutePartPreference.ScoreLegacyAnchor(left);
            var rightScore = RoutePartPreference.ScoreLegacyAnchor(right);
            return rightScore > leftScore ? right : left;
        }
    }
}
