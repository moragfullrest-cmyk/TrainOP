using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Indexes legacy station invocations that belong to detected route chains.
    /// </summary>
    internal static class ChainStationCallIndex
    {
        /// <summary>
        /// Builds a lookup from invocation locations to chain-site metadata.
        /// </summary>
        public static IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> Build(Compilation compilation)
        {
            var bindings = new Dictionary<string, List<ChainSiteBinding>>(StringComparer.Ordinal);

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var chains = ChainDetector.DetectChains(syntaxTree, semanticModel);
                for (var chainIndex = 0; chainIndex < chains.Length; chainIndex++)
                {
                    var chain = chains[chainIndex];
                    var chainId = CallerChainKeyBuilder.Build(chain.Anchor);
                    for (var stationIndex = 0; stationIndex < chain.Stations.Length; stationIndex++)
                    {
                        var station = chain.Stations[stationIndex];
                        if (station.InvocationLocation == null)
                        {
                            continue;
                        }

                        var returnMembers = HandlerOutputParameters.BuildReturnMemberNames(
                            station.Handler.ReturnShape);
                        var binding = new ChainSiteBinding(
                            chainId,
                            stationIndex,
                            station.StationName,
                            station.Invocation,
                            station.Handler,
                            returnMembers);

                        var locationKey = BuildLocationKey(station.InvocationLocation);
                        if (!bindings.TryGetValue(locationKey, out var list))
                        {
                            list = new List<ChainSiteBinding>();
                            bindings[locationKey] = list;
                        }

                        list.Add(binding);
                    }
                }
            }

            var result = new Dictionary<string, ImmutableArray<ChainSiteBinding>>(StringComparer.Ordinal);
            foreach (var kvp in bindings)
            {
                result[kvp.Key] = kvp.Value.ToImmutableArray();
            }

            return result;
        }

        /// <summary>
        /// Resolves all chain-site bindings for a station invocation location.
        /// </summary>
        public static bool TryResolveAll(
            IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> bindings,
            Location invocationLocation,
            out ImmutableArray<ChainSiteBinding> chainBindings)
        {
            chainBindings = default;
            if (invocationLocation == null || bindings == null || bindings.Count == 0)
            {
                return false;
            }

            if (bindings.TryGetValue(BuildLocationKey(invocationLocation), out chainBindings))
            {
                return chainBindings.Length > 0;
            }

            var matches = ImmutableArray.CreateBuilder<ChainSiteBinding>();
            foreach (var candidate in bindings.Values)
            {
                for (var i = 0; i < candidate.Length; i++)
                {
                    var item = candidate[i];
                    if (LocationsEqual(item.InvocationLocation, invocationLocation))
                    {
                        matches.Add(item);
                    }
                }
            }

            if (matches.Count == 0)
            {
                chainBindings = default;
                return false;
            }

            chainBindings = matches.ToImmutable();
            return true;
        }

        private static bool LocationsEqual(Location left, Location right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            var leftSpan = left.GetLineSpan();
            var rightSpan = right.GetLineSpan();
            return string.Equals(
                InterceptorLocationFormatter.NormalizeFilePath(leftSpan.Path),
                InterceptorLocationFormatter.NormalizeFilePath(rightSpan.Path),
                System.StringComparison.OrdinalIgnoreCase)
                && left.SourceSpan == right.SourceSpan;
        }

        /// <summary>
        /// Builds a stable lookup key for a source location.
        /// </summary>
        public static string BuildLocationKey(Location location)
        {
            if (location == null)
            {
                return string.Empty;
            }

            var lineSpan = location.GetLineSpan();
            var path = InterceptorLocationFormatter.NormalizeFilePath(lineSpan.Path);
            if (string.IsNullOrEmpty(path))
            {
                path = "unknown";
            }

            return path + "@" + location.SourceSpan.Start + "@" + location.SourceSpan.Length;
        }
    }
}
