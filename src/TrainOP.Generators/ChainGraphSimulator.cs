using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Simulates wagon flow through a route chain to detect missing, conflicting, or removed wagons.
    /// </summary>
    internal static class ChainGraphSimulator
    {
        /// <summary>
        /// Tracks a wagon currently present in the manifest during simulation.
        /// </summary>
        private sealed class LiveWagon
        {
            /// <summary>
            /// Creates a live wagon record for the given binding and producing station.
            /// </summary>
            public LiveWagon(WagonBinding binding, string producedAtStation)
            {
                Binding = binding;
                ProducedAtStation = producedAtStation;
            }

            public WagonBinding Binding { get; }

            public string ProducedAtStation { get; }
        }

        /// <summary>
        /// Records a wagon that was removed from the manifest at a specific station.
        /// </summary>
        private sealed class RemovedWagon
        {
            /// <summary>
            /// Creates a removed-wagon record for the station where removal occurred.
            /// </summary>
            public RemovedWagon(string removedAtStation)
            {
                RemovedAtStation = removedAtStation;
            }

            public string RemovedAtStation { get; }
        }

        /// <summary>
        /// Mutable state accumulated while walking a route chain.
        /// </summary>
        private sealed class SimulationState
        {
            public List<Diagnostic> Diagnostics { get; } = new();

            public Dictionary<string, LiveWagon> Live { get; } = new(StringComparer.Ordinal);

            public List<string> LiveOrder { get; } = new();

            public Dictionary<string, RemovedWagon> Removed { get; } = new(StringComparer.Ordinal);

            public HashSet<string> SeedWagons { get; } = new(StringComparer.Ordinal);

            public HashSet<string> ConsumedWagons { get; } = new(StringComparer.Ordinal);

            public bool HasUnknownReturn { get; set; }
        }

        /// <summary>
        /// Walks the chain station by station, updating live wagons and collecting diagnostics.
        /// </summary>
        public static ChainSimulationResult Simulate(RouteChain chain)
        {
            var state = new SimulationState();

            for (var i = 0; i < chain.Stations.Length; i++)
            {
                var station = chain.Stations[i];
                ProcessStationInputs(station, i, state);

                if (TryHandleSpecialReturn(station, state))
                {
                    continue;
                }

                ApplyReturn(
                    station,
                    station.Handler,
                    state.Live,
                    state.LiveOrder,
                    state.Removed,
                    state.SeedWagons);

                if (!station.Handler.ReturnShape.IsUnknown)
                {
                    state.HasUnknownReturn = false;
                }
            }

            ReportUnusedSeedWagons(chain, state);

            return new ChainSimulationResult(
                BuildTerminalWagons(state),
                state.HasUnknownReturn,
                state.Diagnostics.ToImmutableArray());
        }

        /// <summary>
        /// Validates required and optional input wagons at a station.
        /// </summary>
        private static void ProcessStationInputs(
            StationChainLink station,
            int stationIndex,
            SimulationState state)
        {
            if (state.HasUnknownReturn)
            {
                foreach (var input in station.Handler.InputWagons)
                {
                    state.ConsumedWagons.Add(input.Name);
                }

                return;
            }

            foreach (var input in station.Handler.InputWagons)
            {
                state.ConsumedWagons.Add(input.Name);

                if (state.Removed.TryGetValue(input.Name, out var removedInfo))
                {
                    state.Diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.WagonRemovedButRequired,
                        input.Location,
                        input.Name,
                        removedInfo.RemovedAtStation,
                        station.StationName));
                    continue;
                }

                if (!state.Live.TryGetValue(input.Name, out var liveWagon))
                {
                    if (input.IsOptional)
                    {
                        continue;
                    }

                    if (stationIndex == 0)
                    {
                        // Wagons may be supplied by Travel(manifest) before the first station runs.
                        continue;
                    }

                    state.Diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.MissingWagon,
                        input.Location,
                        station.StationName,
                        input.Name));
                    continue;
                }

                if (!TypesCompatible(liveWagon.Binding.TypeSymbol, input.TypeSymbol))
                {
                    state.Diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.WagonTypeConflict,
                        input.Location,
                        input.Name,
                        liveWagon.Binding.TypeDisplay,
                        liveWagon.ProducedAtStation,
                        input.TypeDisplay,
                        station.StationName));
                }
            }
        }

        /// <summary>
        /// Handles return shapes that skip or reset wagon state. Returns true when ApplyReturn should be skipped.
        /// </summary>
        private static bool TryHandleSpecialReturn(StationChainLink station, SimulationState state)
        {
            var handler = station.Handler;

            if (handler.ReturnShape.IsUnknown)
            {
                state.HasUnknownReturn = true;
                return true;
            }

            if (handler.ReturnShape.IsCargoManifest)
            {
                state.Diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.CargoManifestReplacement,
                    station.HandlerLocation,
                    station.StationName));
                state.Live.Clear();
                state.LiveOrder.Clear();
                state.Removed.Clear();
                return true;
            }

            if (handler.ReturnShape.IsVoid)
            {
                ApplyVoidReturn(station, handler, state.Live, state.Removed);
                state.HasUnknownReturn = false;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reports seed wagons that were produced but never consumed downstream.
        /// </summary>
        private static void ReportUnusedSeedWagons(RouteChain chain, SimulationState state)
        {
            if (chain.Stations.Length == 0)
            {
                return;
            }

            var seedStation = chain.Stations[0];
            foreach (var seedWagon in state.SeedWagons)
            {
                if (!state.ConsumedWagons.Contains(seedWagon))
                {
                    state.Diagnostics.Add(Diagnostic.Create(
                        TrainRouteDiagnostics.UnusedSeedWagon,
                        seedStation.HandlerLocation,
                        seedWagon,
                        seedStation.StationName));
                }
            }
        }

        /// <summary>
        /// Collects wagons still live at the end of the chain in production order.
        /// </summary>
        private static ImmutableArray<WagonBinding> BuildTerminalWagons(SimulationState state)
        {
            var terminalWagons = ImmutableArray.CreateBuilder<WagonBinding>();
            foreach (var wagonName in state.LiveOrder)
            {
                if (state.Live.TryGetValue(wagonName, out var liveWagon))
                {
                    terminalWagons.Add(liveWagon.Binding);
                }
            }

            return terminalWagons.ToImmutable();
        }

        /// <summary>
        /// Applies void-return semantics: non-ref inputs are removed and no wagons are produced.
        /// </summary>
        private static void ApplyVoidReturn(
            StationChainLink station,
            StationHandlerBinding handler,
            Dictionary<string, LiveWagon> live,
            Dictionary<string, RemovedWagon> removed)
        {
            if (handler.IsSeed)
            {
                return;
            }

            foreach (var input in handler.InputWagons)
            {
                if (input.IsByReference)
                {
                    continue;
                }

                live.Remove(input.Name);
                removed[input.Name] = new RemovedWagon(station.StationName);
            }
        }

        /// <summary>
        /// Applies a station handler return shape to the live and removed wagon state.
        /// </summary>
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

        /// <summary>
        /// Checks whether an existing wagon type is compatible with a required input type.
        /// </summary>
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

        /// <summary>
        /// Extracts the underlying type from a nullable value type, if applicable.
        /// </summary>
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

    /// <summary>
    /// Outcome of simulating wagon flow through a route chain.
    /// </summary>
    internal sealed class ChainSimulationResult
    {
        /// <summary>
        /// Creates a simulation result with terminal wagons, unknown-return flag, and diagnostics.
        /// </summary>
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
