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
        public static bool IsOriginPart(IRoutePart part) =>
            part is CreationSeed or FactoryCall or LocalBinding or JoinSeed;

        /// <summary>
        /// Fluent / statement root expression for an origin part.
        /// </summary>
        public static bool TryGetRoot(IRoutePart part, out ExpressionSyntax root)
        {
            root = part switch
            {
                LocalBinding { Identifier: { } identifier } => identifier,
                CreationSeed { Root: { } creationRoot } => creationRoot,
                FactoryCall { Root: { } factoryRoot } => factoryRoot,
                JoinSeed { DownstreamStation: { } downstream } => downstream,
                JoinSeed { ForkExpression: { } fork } => fork,
                _ => null
            };

            return root != null;
        }

        /// <summary>
        /// Effective factory method on an origin part (factory call, stamped local, or nested).
        /// </summary>
        public static IMethodSymbol GetFactoryMethod(IRoutePart part) =>
            part switch
            {
                FactoryCall factoryCall => factoryCall.FactoryMethod,
                LocalBinding localBinding => localBinding.FactoryMethod,
                _ => null
            };

        /// <summary>
        /// Effective initial / merged wagons on an origin part.
        /// </summary>
        public static ImmutableArray<WagonBinding> GetInitialWagons(IRoutePart part) =>
            part switch
            {
                FactoryCall factoryCall => factoryCall.InitialWagons,
                LocalBinding localBinding => localBinding.InitialWagons,
                JoinSeed joinSeed => joinSeed.MergedTerminalWagons,
                _ => ImmutableArray<WagonBinding>.Empty
            };

        /// <summary>
        /// Containing method when stamped on the origin part.
        /// </summary>
        public static IMethodSymbol GetContainingMethod(IRoutePart part) =>
            part switch
            {
                CreationSeed creationSeed => creationSeed.ContainingMethod,
                FactoryCall factoryCall => factoryCall.ContainingMethod,
                LocalBinding localBinding => localBinding.ContainingMethod,
                _ => null
            };

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
                switch (edge)
                {
                    case { Kind: PartEdgeKind.Append, To: StationLink stationLink }:
                        builder.Add(stationLink);
                        break;
                    case { Kind: PartEdgeKind.Extend, To: ExtensionTail extensionTail }:
                        builder.AddRange(extensionTail.Stations);
                        break;
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
