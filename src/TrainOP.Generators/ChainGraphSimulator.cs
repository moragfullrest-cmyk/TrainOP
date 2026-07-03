using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    internal static class ChainGraphSimulator
    {
        private sealed class LiveWagon
        {
            public LiveWagon(WagonBinding binding, string producedAtStation)
            {
                Binding = binding;
                ProducedAtStation = producedAtStation;
            }

            public WagonBinding Binding { get; }

            public string ProducedAtStation { get; }
        }

        private sealed class RemovedWagon
        {
            public RemovedWagon(string removedAtStation)
            {
                RemovedAtStation = removedAtStation;
            }

            public string RemovedAtStation { get; }
        }

        public static ChainSimulationResult Simulate(RouteChain chain)
        {
            var diagnostics = new List<Diagnostic>();
            var live = new Dictionary<string, LiveWagon>(StringComparer.Ordinal);
            var liveOrder = new List<string>();
            var removed = new Dictionary<string, RemovedWagon>(StringComparer.Ordinal);
            var seedWagons = new HashSet<string>(StringComparer.Ordinal);
            var consumedWagons = new HashSet<string>(StringComparer.Ordinal);
            var hasUnknownReturn = false;

            for (var i = 0; i < chain.Stations.Length; i++)
            {
                var station = chain.Stations[i];
                var handler = station.Handler;

                foreach (var input in handler.InputWagons)
                {
                    consumedWagons.Add(input.Name);

                    if (removed.TryGetValue(input.Name, out var removedInfo))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            TrainRouteDiagnostics.WagonRemovedButRequired,
                            input.Location,
                            input.Name,
                            removedInfo.RemovedAtStation,
                            station.StationName));
                        continue;
                    }

                    if (!live.TryGetValue(input.Name, out var liveWagon))
                    {
                        if (input.IsOptional)
                        {
                            continue;
                        }

                        if (i == 0)
                        {
                            // Wagons may be supplied by Travel(manifest) before the first station runs.
                            continue;
                        }

                        diagnostics.Add(Diagnostic.Create(
                            TrainRouteDiagnostics.MissingWagon,
                            input.Location,
                            station.StationName,
                            input.Name));
                        continue;
                    }

                    if (!TypesCompatible(liveWagon.Binding.TypeSymbol, input.TypeSymbol))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            TrainRouteDiagnostics.WagonTypeConflict,
                            input.Location,
                            input.Name,
                            liveWagon.Binding.TypeDisplay,
                            liveWagon.ProducedAtStation,
                            input.TypeDisplay,
                            station.StationName));
                    }
                }

                if (handler.ReturnShape.IsUnknown)
                {
                    hasUnknownReturn = true;
                    continue;
                }

                if (handler.ReturnShape.IsCargoManifest)
                {
                    diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.CargoManifestReplacement,
                        station.HandlerLocation,
                        station.StationName));
                    live.Clear();
                    liveOrder.Clear();
                    removed.Clear();
                    continue;
                }

                if (handler.ReturnShape.IsUnnamedValueTuple
                    && handler.InputWagons.Length > 0)
                {
                    var expected = string.Join(", ", handler.InputWagons.Select(w => w.Name));
                    diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.TupleReturnOrder,
                        station.HandlerLocation,
                        station.StationName,
                        expected));
                }

                ApplyReturn(station, handler, live, liveOrder, removed, seedWagons);
            }

            foreach (var seedWagon in seedWagons)
            {
                if (!consumedWagons.Contains(seedWagon) && chain.Stations.Length > 0)
                {
                    var seedStation = chain.Stations[0];
                    diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.UnusedSeedWagon,
                        seedStation.HandlerLocation,
                        seedWagon,
                        seedStation.StationName));
                }
            }

            var terminalWagons = ImmutableArray.CreateBuilder<WagonBinding>();
            foreach (var wagonName in liveOrder)
            {
                if (live.TryGetValue(wagonName, out var liveWagon))
                {
                    terminalWagons.Add(liveWagon.Binding);
                }
            }

            return new ChainSimulationResult(
                terminalWagons.ToImmutable(),
                hasUnknownReturn,
                diagnostics.ToImmutableArray());
        }

        private static void ApplyReturn(
            StationChainLink station,
            StationHandlerBinding handler,
            Dictionary<string, LiveWagon> live,
            List<string> liveOrder,
            Dictionary<string, RemovedWagon> removed,
            HashSet<string> seedWagons)
        {
            var returnedNames = new HashSet<string>(
                handler.ReturnShape.Members.Select(m => m.Name),
                StringComparer.Ordinal);

            if (handler.IsSeed)
            {
                foreach (var member in handler.ReturnShape.Members)
                {
                    if (!live.ContainsKey(member.Name))
                    {
                        liveOrder.Add(member.Name);
                    }

                    live[member.Name] = new LiveWagon(member, station.StationName);
                    seedWagons.Add(member.Name);
                    removed.Remove(member.Name);
                }

                return;
            }

            foreach (var input in handler.InputWagons)
            {
                if (!returnedNames.Contains(input.Name))
                {
                    if (input.IsByReference)
                    {
                        continue;
                    }

                    live.Remove(input.Name);
                    removed[input.Name] = new RemovedWagon(station.StationName);
                }
            }

            foreach (var member in handler.ReturnShape.Members)
            {
                if (!live.ContainsKey(member.Name))
                {
                    liveOrder.Add(member.Name);
                }

                live[member.Name] = new LiveWagon(member, station.StationName);
                removed.Remove(member.Name);
            }
        }

        private static bool TypesCompatible(ITypeSymbol existing, ITypeSymbol required)
        {
            if (existing == null || required == null)
            {
                return true;
            }

            if (SymbolEqualityComparer.Default.Equals(existing, required))
            {
                return true;
            }

            if (TryGetNullableUnderlying(required, out var requiredUnderlying)
                && SymbolEqualityComparer.Default.Equals(existing, requiredUnderlying))
            {
                return true;
            }

            if (TryGetNullableUnderlying(existing, out var existingUnderlying)
                && SymbolEqualityComparer.Default.Equals(existingUnderlying, required))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetNullableUnderlying(ITypeSymbol typeSymbol, out ITypeSymbol underlying)
        {
            underlying = null;
            if (typeSymbol is INamedTypeSymbol named
                && named.OriginalDefinition != null
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
            {
                underlying = named.TypeArguments[0];
                return true;
            }

            return false;
        }

    }

    internal sealed class ChainSimulationResult
    {
        public ChainSimulationResult(
            ImmutableArray<WagonBinding> terminalWagons,
            bool hasUnknownReturn,
            ImmutableArray<Diagnostic> diagnostics)
        {
            TerminalWagons = terminalWagons;
            HasUnknownReturn = hasUnknownReturn;
            Diagnostics = diagnostics;
        }

        public ImmutableArray<WagonBinding> TerminalWagons { get; }

        public bool HasUnknownReturn { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
