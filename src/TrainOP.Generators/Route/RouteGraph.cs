using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Chain;

namespace TrainOP.Generators.Route
{
    /// <summary>
    /// Assembled route chains and chain-site bindings for a compilation.
    /// </summary>
    internal sealed class RouteGraph
    {
        public RouteGraph(
            ImmutableArray<RouteChain> chains,
            IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> chainIndex,
            ImmutableArray<RouteSite> stationSites,
            IReadOnlyDictionary<string, RouteChain> chainsByInvocationKey)
        {
            Chains = chains;
            ChainIndex = chainIndex;
            StationSites = stationSites;
            _chainsByInvocationKey = chainsByInvocationKey;
            _chainsByTree = BuildChainsByTree(chains);
            _chainedInvocationKeys = BuildChainedInvocationKeys(chainIndex);
        }

        private readonly IReadOnlyDictionary<string, RouteChain> _chainsByInvocationKey;
        private readonly IReadOnlyDictionary<string, ImmutableArray<RouteChain>> _chainsByTree;
        private readonly HashSet<string> _chainedInvocationKeys;

        public ImmutableArray<RouteChain> Chains { get; }

        public IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> ChainIndex { get; }

        public ImmutableArray<RouteSite> StationSites { get; }

        public static RouteGraph Empty { get; } = new RouteGraph(
            ImmutableArray<RouteChain>.Empty,
            new Dictionary<string, ImmutableArray<ChainSiteBinding>>(StringComparer.Ordinal),
            ImmutableArray<RouteSite>.Empty,
            new Dictionary<string, RouteChain>(StringComparer.Ordinal));

        /// <summary>
        /// Returns chains whose anchor or station invocations live in <paramref name="syntaxTree"/>.
        /// </summary>
        public ImmutableArray<RouteChain> GetChainsInTree(SyntaxTree syntaxTree)
        {
            if (syntaxTree == null)
            {
                return ImmutableArray<RouteChain>.Empty;
            }

            var path = StringHelpers.NormalizeFilePath(syntaxTree.FilePath);
            if (!string.IsNullOrEmpty(path)
                && _chainsByTree.TryGetValue(path, out var chains))
            {
                return chains;
            }

            if (string.IsNullOrEmpty(path))
            {
                return Chains
                    .Where(chain => ChainBelongsToTree(chain, syntaxTree))
                    .ToImmutableArray();
            }

            return ImmutableArray<RouteChain>.Empty;
        }

        private static bool ChainBelongsToTree(RouteChain chain, SyntaxTree syntaxTree)
        {
            if (chain.Anchor?.Root?.SyntaxTree == syntaxTree)
            {
                return true;
            }

            for (var i = 0; i < chain.Stations.Length; i++)
            {
                if (chain.Stations[i].Invocation?.SyntaxTree == syntaxTree)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when <paramref name="invocation"/> belongs to a detected route chain.
        /// </summary>
        public bool TryGetChainForInvocation(
            InvocationExpressionSyntax invocation,
            out RouteChain chain)
        {
            chain = null;
            if (invocation == null)
            {
                return false;
            }

            var key = ChainSiteBindingLookup.BuildLocationKey(invocation.GetLocation());
            return key.Length > 0 && _chainsByInvocationKey.TryGetValue(key, out chain);
        }

        /// <summary>
        /// Returns true when the invocation location is part of any detected chain.
        /// </summary>
        public bool IsChainedInvocation(Location invocationLocation)
        {
            if (invocationLocation == null)
            {
                return false;
            }

            var key = ChainSiteBindingLookup.BuildLocationKey(invocationLocation);
            return key.Length > 0 && _chainedInvocationKeys.Contains(key);
        }

        private static IReadOnlyDictionary<string, ImmutableArray<RouteChain>> BuildChainsByTree(
            ImmutableArray<RouteChain> chains)
        {
            var result = new Dictionary<string, List<RouteChain>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                var treePath = StringHelpers.NormalizeFilePath(
                    chain.AnchorLocation?.GetLineSpan().Path);
                if (string.IsNullOrEmpty(treePath))
                {
                    continue;
                }

                if (!result.TryGetValue(treePath, out var list))
                {
                    list = new List<RouteChain>();
                    result[treePath] = list;
                }

                if (!list.Contains(chain))
                {
                    list.Add(chain);
                }

                for (var stationIndex = 0; stationIndex < chain.Stations.Length; stationIndex++)
                {
                    var stationPath = StringHelpers.NormalizeFilePath(
                        chain.Stations[stationIndex].InvocationLocation?.GetLineSpan().Path);
                    if (string.IsNullOrEmpty(stationPath)
                        || string.Equals(stationPath, treePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!result.TryGetValue(stationPath, out var stationList))
                    {
                        stationList = new List<RouteChain>();
                        result[stationPath] = stationList;
                    }

                    if (!stationList.Contains(chain))
                    {
                        stationList.Add(chain);
                    }
                }
            }

            return result.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> BuildChainedInvocationKeys(
            IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> chainIndex)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bindings in chainIndex.Values)
            {
                for (var i = 0; i < bindings.Length; i++)
                {
                    var key = ChainSiteBindingLookup.BuildLocationKey(bindings[i].InvocationLocation);
                    if (key.Length > 0)
                    {
                        keys.Add(key);
                    }
                }
            }

            return keys;
        }
    }
}
