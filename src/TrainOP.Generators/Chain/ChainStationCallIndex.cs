using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;

namespace TrainOP.Generators
{
    /// <summary>
    /// Location-key utilities for chain-site binding lookup.
    /// </summary>
    internal static class ChainStationCallIndex
    {
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
                StringHelpers.NormalizeFilePath(leftSpan.Path),
                StringHelpers.NormalizeFilePath(rightSpan.Path),
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
            var path = StringHelpers.NormalizeFilePath(lineSpan.Path);
            if (string.IsNullOrEmpty(path))
            {
                path = "unknown";
            }

            return path + "@" + location.SourceSpan.Start + "@" + location.SourceSpan.Length;
        }
    }
}
