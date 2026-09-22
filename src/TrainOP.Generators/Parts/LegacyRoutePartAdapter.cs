using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Maps first-class route parts to legacy <see cref="RouteChainAnchor"/> /
    /// <see cref="RouteChain"/> for emit/analyzer compatibility.
    /// </summary>
    /// <remarks>
    /// Callers may supply an explicit fluent <c>root</c>, or use overloads that read
    /// stamped roots on materialized parts.
    /// </remarks>
    internal static class LegacyRoutePartAdapter
    {
        /// <summary>
        /// Converts a materialized <see cref="CreationSeed"/> into a legacy ObjectCreation anchor.
        /// </summary>
        public static bool TryToLegacyAnchor(CreationSeed seed, out RouteChainAnchor anchor)
        {
            anchor = null;
            if (seed?.Root == null)
            {
                return false;
            }

            return TryToLegacyAnchor(seed, seed.Root, out anchor);
        }

        /// <summary>
        /// Converts a materialized <see cref="FactoryCall"/> into a legacy factory anchor.
        /// </summary>
        public static bool TryToLegacyAnchor(FactoryCall factoryCall, out RouteChainAnchor anchor)
        {
            anchor = null;
            if (factoryCall?.Root == null)
            {
                return false;
            }

            return TryToLegacyAnchor(factoryCall, factoryCall.Root, out anchor);
        }

        /// <summary>
        /// Converts a materialized <see cref="LocalBinding"/> into a legacy local/factory anchor.
        /// </summary>
        public static bool TryToLegacyAnchor(LocalBinding binding, out RouteChainAnchor anchor)
        {
            anchor = null;
            if (binding?.Identifier == null)
            {
                return false;
            }

            return TryToLegacyAnchor(binding, binding.Identifier, out anchor);
        }

        /// <summary>
        /// Converts a materialized <see cref="JoinSeed"/> into a legacy BranchJoin anchor.
        /// </summary>
        public static bool TryToLegacyAnchor(JoinSeed seed, out RouteChainAnchor anchor)
        {
            anchor = null;
            if (seed == null)
            {
                return false;
            }

            ExpressionSyntax root = seed.DownstreamStation != null
                ? (ExpressionSyntax)seed.DownstreamStation
                : seed.ForkExpression;
            if (root == null)
            {
                return false;
            }

            return TryToLegacyAnchor(seed, root, out anchor);
        }

        /// <summary>
        /// Converts any origin/join part into a legacy chain anchor using the part's stamped root.
        /// </summary>
        public static bool TryToLegacyAnchor(IRoutePart part, out RouteChainAnchor anchor)
        {
            anchor = null;
            if (part == null || !TryGetPartRoot(part, out var root))
            {
                return false;
            }

            return TryToLegacyAnchor(part, root, out anchor);
        }

        /// <summary>
        /// Converts an origin/join part into a legacy chain anchor.
        /// </summary>
        /// <returns>
        /// <see langword="false"/> when <paramref name="part"/> is not an anchorable origin
        /// (e.g. <see cref="StationLink"/>, <see cref="JoinArm"/>, <see cref="ExtensionTail"/>).
        /// </returns>
        public static bool TryToLegacyAnchor(
            IRoutePart part,
            ExpressionSyntax root,
            out RouteChainAnchor anchor)
        {
            anchor = null;
            if (part == null || root == null)
            {
                return false;
            }

            if (!TryMapAnchorKind(part, out var kind))
            {
                return false;
            }

            IMethodSymbol containingMethod = null;
            IMethodSymbol factoryMethod = null;
            ImmutableArray<WagonBinding> initialWagons = default;

            switch (part)
            {
                case CreationSeed creationSeed:
                    containingMethod = creationSeed.ContainingMethod;
                    break;
                case FactoryCall factoryCall:
                    containingMethod = factoryCall.ContainingMethod;
                    factoryMethod = factoryCall.FactoryMethod;
                    initialWagons = factoryCall.InitialWagons;
                    break;
                case LocalBinding localBinding:
                    containingMethod = localBinding.ContainingMethod;
                    factoryMethod = localBinding.FactoryMethod;
                    initialWagons = localBinding.InitialWagons;
                    break;
                case JoinSeed joinSeed:
                    initialWagons = joinSeed.MergedTerminalWagons;
                    break;
            }

            anchor = new RouteChainAnchor(
                kind,
                root,
                part.Location,
                containingMethod,
                factoryMethod,
                initialWagons);
            return true;
        }

        /// <summary>
        /// Builds a legacy <see cref="RouteChain"/> from a constructor snapshot,
        /// deriving root and stations from connected parts.
        /// </summary>
        public static bool TryToRouteChain(
            ChainConstructor constructor,
            out RouteChain chain)
        {
            chain = null;
            if (constructor == null
                || !TrySelectAnchorPart(constructor.Parts, out var anchorPart)
                || !TryGetPartRoot(anchorPart, out var anchorRoot))
            {
                return false;
            }

            var stations = CollectAppendedStations(constructor);
            return TryToRouteChain(constructor, anchorRoot, stations, out chain);
        }

        /// <summary>
        /// Builds a legacy <see cref="RouteChain"/> from a constructor snapshot.
        /// </summary>
        /// <param name="constructor">Connected parts and edges.</param>
        /// <param name="anchorRoot">Fluent root expression for the chosen anchor part.</param>
        /// <param name="stations">
        /// Prebuilt station links (empty until StationLink materialize supplies them).
        /// </param>
        /// <param name="chain">Legacy chain when an anchorable part is present.</param>
        public static bool TryToRouteChain(
            ChainConstructor constructor,
            ExpressionSyntax anchorRoot,
            ImmutableArray<StationChainLink> stations,
            out RouteChain chain)
        {
            chain = null;
            if (constructor == null || anchorRoot == null)
            {
                return false;
            }

            if (!TrySelectAnchorPart(constructor.Parts, out var anchorPart))
            {
                return false;
            }

            if (!TryToLegacyAnchor(anchorPart, anchorRoot, out var anchor))
            {
                return false;
            }

            if (stations.IsDefault)
            {
                stations = ImmutableArray<StationChainLink>.Empty;
            }

            chain = new RouteChain(anchor, stations);
            return true;
        }

        /// <summary>
        /// Maps a part to legacy <see cref="RouteChainAnchorKind"/> when it is an origin/join seed.
        /// </summary>
        public static bool TryMapAnchorKind(IRoutePart part, out RouteChainAnchorKind kind)
        {
            switch (part)
            {
                case CreationSeed _:
                    kind = RouteChainAnchorKind.ObjectCreation;
                    return true;
                case LocalBinding localBinding:
                    kind = localBinding.ToLegacyAnchorKind();
                    return true;
                case FactoryCall factoryCall:
                    kind = factoryCall.ToLegacyAnchorKind();
                    return true;
                case JoinSeed _:
                    kind = RouteChainAnchorKind.BranchJoin;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        /// <summary>
        /// Picks the preferred anchorable part (aligns with legacy PreferAnchor scores).
        /// </summary>
        internal static bool TrySelectAnchorPart(
            ImmutableArray<IRoutePart> parts,
            out IRoutePart anchorPart)
        {
            anchorPart = null;
            if (parts.IsDefaultOrEmpty)
            {
                return false;
            }

            IRoutePart best = null;
            var bestScore = int.MinValue;
            foreach (var part in parts)
            {
                if (!TryMapAnchorKind(part, out _))
                {
                    continue;
                }

                var score = RoutePartPreference.Score(part);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = part;
                }
            }

            if (best == null)
            {
                return false;
            }

            anchorPart = best;
            return true;
        }

        private static bool TryGetPartRoot(IRoutePart part, out ExpressionSyntax root)
        {
            switch (part)
            {
                case LocalBinding localBinding:
                    root = localBinding.Identifier;
                    return root != null;
                case CreationSeed creationSeed:
                    root = creationSeed.Root;
                    return root != null;
                case FactoryCall factoryCall:
                    root = factoryCall.Root;
                    return root != null;
                case JoinSeed joinSeed:
                    root = joinSeed.DownstreamStation != null
                        ? (ExpressionSyntax)joinSeed.DownstreamStation
                        : joinSeed.ForkExpression;
                    return root != null;
                default:
                    root = null;
                    return false;
            }
        }

        private static ImmutableArray<StationChainLink> CollectAppendedStations(ChainConstructor constructor)
        {
            var builder = ImmutableArray.CreateBuilder<StationChainLink>();
            foreach (var edge in constructor.Edges)
            {
                if (edge.Kind == PartEdgeKind.Append && edge.To is StationLink stationLink)
                {
                    builder.Add(stationLink.ToStationChainLink());
                    continue;
                }

                if (edge.Kind == PartEdgeKind.Extend && edge.To is ExtensionTail extensionTail)
                {
                    foreach (var link in extensionTail.Stations)
                    {
                        builder.Add(link.ToStationChainLink());
                    }
                }
            }

            return builder.ToImmutable();
        }
    }
}
