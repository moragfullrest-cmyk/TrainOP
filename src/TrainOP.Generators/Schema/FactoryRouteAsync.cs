using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Decides whether a factory route must run through <c>TravelAsync</c>.
    /// A route is async when any return path contains an async station, including a factory it continues.
    /// </summary>
    internal static class FactoryRouteAsync
    {
        [ThreadStatic]
        private static HashSet<IMethodSymbol> _visiting;

        /// <summary>
        /// True when <paramref name="chain"/> or the factory it continues contains an async station.
        /// </summary>
        public static bool ChainRequiresAsyncTravel(RouteChain chain, Compilation compilation)
        {
            if (chain == null)
            {
                return false;
            }

            if (HasAsyncStation(chain))
            {
                return true;
            }

            return MethodRequiresAsyncTravel(chain.FactoryMethod, compilation);
        }

        /// <summary>
        /// True when any station on <paramref name="chain"/> is async.
        /// </summary>
        public static bool HasAsyncStation(RouteChain chain)
        {
            if (chain == null)
            {
                return false;
            }

            var stations = chain.Stations;
            for (var i = 0; i < stations.Length; i++)
            {
                if (stations[i].Handler != null && stations[i].Handler.IsAsync)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="method"/> returns a route that contains an async station.
        /// The method body wins when it is in <paramref name="compilation"/>; otherwise the exported schema does.
        /// </summary>
        public static bool MethodRequiresAsyncTravel(IMethodSymbol method, Compilation compilation)
        {
            if (method == null
                || compilation == null
                || (!StationSyntaxHelper.IsTrainRouteFactoryReturnType(method.ReturnType)
                    && !StationSyntaxHelper.TryGetSingleOutTrainRouteParameter(method, out _)))
            {
                return false;
            }

            _visiting ??= new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            if (!_visiting.Add(method))
            {
                return false;
            }

            try
            {
                if (DeclaringSyntaxInCompilation(method, compilation))
                {
                    return AnySimulatedPathIsAsync(method, compilation);
                }

                if (ExternalRouteSchemaResolver.TryResolve(method, compilation, out ExternalRouteSchema schema))
                {
                    return schema.IsAsync;
                }

                return false;
            }
            finally
            {
                _visiting.Remove(method);
            }
        }

        private static bool AnySimulatedPathIsAsync(IMethodSymbol method, Compilation compilation)
        {
            var paths = StationSyntaxHelper.IsTrainRouteFactoryReturnType(method.ReturnType)
                ? RouteFactoryPathSimulator.SimulateAllReturnPaths(method, compilation)
                : SimulateOutPaths(method, compilation);

            if (paths.IsDefaultOrEmpty)
            {
                return false;
            }

            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].IsAsync)
                {
                    return true;
                }
            }

            return false;
        }

        private static ImmutableArray<FactoryPathSimulation> SimulateOutPaths(
            IMethodSymbol method,
            Compilation compilation)
        {
            if (!StationSyntaxHelper.TryGetSingleOutTrainRouteParameter(method, out var outParameter))
            {
                return ImmutableArray<FactoryPathSimulation>.Empty;
            }

            return RouteFactoryPathSimulator.SimulateAllOutParameterPaths(method, outParameter, compilation);
        }

        private static bool DeclaringSyntaxInCompilation(IMethodSymbol method, Compilation compilation)
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.SyntaxTree != null && compilation.ContainsSyntaxTree(reference.SyntaxTree))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
