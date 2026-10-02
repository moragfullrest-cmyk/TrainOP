using System;
using System.Collections.Generic;
using System.Text;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Compares optional-wagon substitutes across call sites that share one delegate signature.
    /// </summary>
    internal static class OptionalFallbackSites
    {
        /// <summary>
        /// Returns the expression assigned when the key is missing, or null when the wagon is required.
        /// </summary>
        public static string Render(WagonBinding wagon)
        {
            if (wagon == null || !wagon.IsOptional)
            {
                return null;
            }

            return wagon.OptionalFallback ?? ("default(" + wagon.TypeDisplay + ")");
        }

        /// <summary>
        /// True when any call site substitutes a different constant than the canonical handler.
        /// </summary>
        public static bool Differ(StationHandlerBinding canonical, IReadOnlyList<ChainSiteBinding> sites)
        {
            if (canonical == null || sites == null || sites.Count == 0)
            {
                return false;
            }

            var canonicalProfile = Profile(canonical);
            for (var i = 0; i < sites.Count; i++)
            {
                var schema = sites[i].Schema;
                if (schema == null)
                {
                    continue;
                }

                if (!string.Equals(Profile(schema), canonicalProfile, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Sites to pass into codegen, or null when one substitute covers the signature.
        /// </summary>
        public static IReadOnlyList<ChainSiteBinding> WhenTheyDiffer(
            StationHandlerBinding canonical,
            IReadOnlyList<ChainSiteBinding> sites)
        {
            if (!Differ(canonical, sites))
            {
                return null;
            }

            return sites;
        }

        private static string Profile(StationHandlerBinding schema)
        {
            var wagons = schema.Wagons;
            var builder = new StringBuilder();
            for (var i = 0; i < wagons.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append(Render(wagons[i]) ?? "-");
            }

            return builder.ToString();
        }
    }
}
