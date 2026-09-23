using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Port helpers for origin / join parts: root expression, factory stamp, wagons, selection.
    /// </summary>
    internal static class RouteOriginPorts
    {
        /// <summary>
        /// Whether <paramref name="part"/> can serve as a chain origin (seed / binding / join).
        /// </summary>
        public static bool IsOriginPart(IRoutePart part)
        {
            return part is CreationSeed
                || part is FactoryCall
                || part is LocalBinding
                || part is JoinSeed;
        }

        /// <summary>
        /// Fluent / statement root expression for an origin part.
        /// </summary>
        public static bool TryGetRoot(IRoutePart part, out ExpressionSyntax root)
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

        /// <summary>
        /// Effective factory method on an origin part (factory call, stamped local, or nested).
        /// </summary>
        public static IMethodSymbol GetFactoryMethod(IRoutePart part)
        {
            switch (part)
            {
                case FactoryCall factoryCall:
                    return factoryCall.FactoryMethod;
                case LocalBinding localBinding:
                    return localBinding.FactoryMethod;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Effective initial / merged wagons on an origin part.
        /// </summary>
        public static ImmutableArray<WagonBinding> GetInitialWagons(IRoutePart part)
        {
            switch (part)
            {
                case FactoryCall factoryCall:
                    return factoryCall.InitialWagons;
                case LocalBinding localBinding:
                    return localBinding.InitialWagons;
                case JoinSeed joinSeed:
                    return joinSeed.MergedTerminalWagons;
                default:
                    return ImmutableArray<WagonBinding>.Empty;
            }
        }

        /// <summary>
        /// Containing method when stamped on the origin part.
        /// </summary>
        public static IMethodSymbol GetContainingMethod(IRoutePart part)
        {
            switch (part)
            {
                case CreationSeed creationSeed:
                    return creationSeed.ContainingMethod;
                case FactoryCall factoryCall:
                    return factoryCall.ContainingMethod;
                case LocalBinding localBinding:
                    return localBinding.ContainingMethod;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Picks the preferred origin part from a constructor snapshot (highest preference score).
        /// </summary>
        public static bool TrySelectOriginPart(
            ImmutableArray<IRoutePart> parts,
            out IRoutePart origin)
        {
            origin = null;
            if (parts.IsDefaultOrEmpty)
            {
                return false;
            }

            IRoutePart best = null;
            var bestScore = int.MinValue;
            foreach (var part in parts)
            {
                if (!IsOriginPart(part))
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

            origin = best;
            return true;
        }

        /// <summary>
        /// Collects Append / Extend station links from a constructor in edge order.
        /// </summary>
        public static ImmutableArray<StationLink> CollectStations(ChainConstructor constructor)
        {
            if (constructor == null)
            {
                return ImmutableArray<StationLink>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<StationLink>();
            foreach (var edge in constructor.Edges)
            {
                if (edge.Kind == PartEdgeKind.Append && edge.To is StationLink stationLink)
                {
                    builder.Add(stationLink);
                    continue;
                }

                if (edge.Kind == PartEdgeKind.Extend && edge.To is ExtensionTail extensionTail)
                {
                    builder.AddRange(extensionTail.Stations);
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        /// Builds a <see cref="Route.RouteChain"/> from a connected constructor snapshot.
        /// </summary>
        public static bool TryToRouteChain(
            ChainConstructor constructor,
            out Route.RouteChain chain)
        {
            chain = null;
            if (constructor == null
                || !TrySelectOriginPart(constructor.Parts, out var origin)
                || !TryGetRoot(origin, out _))
            {
                return false;
            }

            chain = new Route.RouteChain(origin, CollectStations(constructor));
            return true;
        }
    }
}
