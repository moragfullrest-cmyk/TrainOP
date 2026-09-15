using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

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

            public bool HasUnknownReturn { get; set; }
        }

        /// <summary>
        /// Walks the chain station by station, updating live wagons and collecting diagnostics.
        /// </summary>
        public static ChainSimulationResult Simulate(RouteChain chain)
        {
            return SimulateCore(chain, initialWagons: default);
        }

        /// <summary>
        /// Walks the chain after seeding live wagons (e.g. merged terminals from a branch join).
        /// </summary>
        public static ChainSimulationResult Simulate(
            RouteChain chain,
            ImmutableArray<WagonBinding> initialWagons)
        {
            return SimulateCore(chain, initialWagons);
        }

        /// <summary>
        /// Shared simulation walk with optional initial live wagons.
        /// </summary>
        private static ChainSimulationResult SimulateCore(
            RouteChain chain,
            ImmutableArray<WagonBinding> initialWagons)
        {
            var state = new SimulationState();

            if (!initialWagons.IsDefaultOrEmpty)
            {
                foreach (var wagon in initialWagons)
                {
                    if (!state.Live.ContainsKey(wagon.Name))
                    {
                        state.LiveOrder.Add(wagon.Name);
                    }

                    state.Live[wagon.Name] = new LiveWagon(wagon, "<join>");
                }
            }

            for (var i = 0; i < chain.Stations.Length; i++)
            {
                var station = chain.Stations[i];
                ProcessStationInputs(station, state);

                if (TryHandleSpecialReturn(station, state))
                {
                    continue;
                }

                if (station.Handler.ReturnShape.HasDefaultItemNTupleElements)
                {
                    ReportTupleReturnDiagnostics(
                        state,
                        station.Handler.ReturnShape.TupleReturnLocations,
                        TrainRouteDiagnostics.DefaultItemNTupleReturn);
                }

                ApplyReturn(
                    station,
                    station.Handler,
                    state.Live,
                    state.LiveOrder,
                    state.Removed);

                if (!station.Handler.ReturnShape.IsUnknown)
                {
                    state.HasUnknownReturn = false;
                }
            }

            var terminalWagons = state.HasUnknownReturn
                ? ImmutableArray<WagonBinding>.Empty
                : state.LiveOrder
                    .Where(name => state.Live.ContainsKey(name))
                    .Select(name => state.Live[name].Binding)
                    .ToImmutableArray();

            return new ChainSimulationResult(
                terminalWagons,
                state.HasUnknownReturn,
                state.Diagnostics.ToImmutableArray());
        }

        /// <summary>
        /// Validates required and optional input wagons at a station.
        /// </summary>
        private static void ProcessStationInputs(
            StationChainLink station,
            SimulationState state)
        {
            if (state.HasUnknownReturn)
            {
                return;
            }

            foreach (var input in station.Handler.InputWagons)
            {
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

            // ServiceStation overlay does not add or remove wagons (ApplyReturn is a no-op).
            // The remaining route is type-checked without assuming recovery ran.
            if (handler.IsServiceStation)
            {
                ValidateServiceStationComposition(station, state);
                return true;
            }

            if (handler.ReturnShape.IsRuntimeSignalReturn)
            {
                state.Diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.RuntimeSignalReturn,
                    station.HandlerLocation,
                    station.StationName,
                    handler.ReturnShape.ReturnTypeDisplay ?? "TrainOP.Signal"));
                state.HasUnknownReturn = true;
                return true;
            }

            // RailwaySignals.Red / White (and Signal with no data payload) do not
            // mutate wagons. IsUnknown is set on those shapes for merge/codegen,
            // but terminal composition remains the live manifest.
            if (handler.ReturnShape.IsExplicitSignalReturn)
            {
                return true;
            }

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
        /// Reports composition-changing ServiceStation returns without mutating live wagon state.
        /// </summary>
        private static void ValidateServiceStationComposition(
            StationChainLink station,
            SimulationState state)
        {
            var handler = station.Handler;

            if (handler.ReturnShape.IsRuntimeSignalReturn)
            {
                state.Diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.RuntimeSignalReturn,
                    station.HandlerLocation,
                    station.StationName,
                    handler.ReturnShape.ReturnTypeDisplay ?? "TrainOP.Signal"));
                state.HasUnknownReturn = true;
                return;
            }

            if (handler.ReturnShape.IsExplicitSignalReturn)
            {
                return;
            }

            if (handler.ReturnShape.IsUnknown)
            {
                state.HasUnknownReturn = true;
                return;
            }

            if (handler.ReturnShape.IsCargoManifest)
            {
                state.Diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.ServiceStationCargoManifestReplacement,
                    station.HandlerLocation,
                    station.StationName));
                return;
            }

            if (handler.ReturnShape.IsVoid)
            {
                ReportServiceStationOmittedInputs(station, handler, state, returnedNames: null);
                return;
            }

            if (handler.ReturnShape.HasDefaultItemNTupleElements)
            {
                ReportTupleReturnDiagnostics(
                    state,
                    handler.ReturnShape.TupleReturnLocations,
                    TrainRouteDiagnostics.DefaultItemNTupleReturn);
            }

            var plan = MergePlanBuilder.Build(handler);
            var returnedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in plan.InputSlots)
            {
                if (slot.IsMapped)
                {
                    returnedNames.Add(slot.WagonName);
                }
            }

            ReportServiceStationOmittedInputs(station, handler, state, returnedNames);

            if (state.HasUnknownReturn)
            {
                return;
            }

            var membersByName = new Dictionary<string, WagonBinding>(StringComparer.Ordinal);
            foreach (var member in handler.ReturnShape.Members)
            {
                membersByName[member.Name] = member;
            }

            foreach (var extra in plan.ExtraSlots)
            {
                if (state.Live.ContainsKey(extra.ReturnMemberName))
                {
                    continue;
                }

                var location = membersByName.TryGetValue(extra.ReturnMemberName, out var member)
                    ? member.Location
                    : station.HandlerLocation;

                state.Diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.ServiceStationAddsWagon,
                    location ?? station.HandlerLocation,
                    station.StationName,
                    extra.ReturnMemberName));
            }
        }

        /// <summary>
        /// Reports TOP016 for non-ref ServiceStation inputs omitted from the return shape.
        /// </summary>
        private static void ReportServiceStationOmittedInputs(
            StationChainLink station,
            StationHandlerBinding handler,
            SimulationState state,
            HashSet<string> returnedNames)
        {
            foreach (var input in handler.InputWagons)
            {
                if (input.IsByReference)
                {
                    continue;
                }

                if (returnedNames != null && returnedNames.Contains(input.Name))
                {
                    continue;
                }

                state.Diagnostics.Add(Diagnostic.Create(
                    TrainRouteDiagnostics.ServiceStationRemovesWagon,
                    input.Location ?? station.HandlerLocation,
                    station.StationName,
                    input.Name));
            }
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
        /// Value-tuple ItemN members unroll into input wagon keys via <see cref="MergePlanBuilder"/>
        /// (parity with runtime merge); named tuple / anonymous members keep their names when they match.
        /// </summary>
        private static void ApplyReturn(
            StationChainLink station,
            StationHandlerBinding handler,
            Dictionary<string, LiveWagon> live,
            List<string> liveOrder,
            Dictionary<string, RemovedWagon> removed)
        {
            if (handler.IsServiceStation)
            {
                return;
            }

            var plan = MergePlanBuilder.Build(handler);
            var membersByName = new Dictionary<string, WagonBinding>(StringComparer.Ordinal);
            foreach (var member in handler.ReturnShape.Members)
            {
                membersByName[member.Name] = member;
            }

            var returnedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in plan.InputSlots)
            {
                if (slot.IsMapped)
                {
                    returnedNames.Add(slot.WagonName);
                }
            }

            foreach (var extra in plan.ExtraSlots)
            {
                returnedNames.Add(extra.ReturnMemberName);
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

            foreach (var slot in plan.InputSlots)
            {
                if (!slot.IsMapped
                    || !membersByName.TryGetValue(slot.ReturnMemberName, out var member))
                {
                    continue;
                }

                var binding = WithLiveWagonName(member, slot.WagonName);
                if (!live.ContainsKey(slot.WagonName))
                {
                    liveOrder.Add(slot.WagonName);
                }

                live[slot.WagonName] = new LiveWagon(binding, station.StationName);
                removed.Remove(slot.WagonName);
            }

            foreach (var extra in plan.ExtraSlots)
            {
                if (!membersByName.TryGetValue(extra.ReturnMemberName, out var member))
                {
                    continue;
                }

                if (!live.ContainsKey(member.Name))
                {
                    liveOrder.Add(member.Name);
                }

                live[member.Name] = new LiveWagon(member, station.StationName);
                removed.Remove(member.Name);
            }
        }

        /// <summary>
        /// Returns <paramref name="member"/> under <paramref name="wagonName"/> when ItemN (or other)
        /// return member names differ from the manifest wagon key.
        /// </summary>
        private static WagonBinding WithLiveWagonName(WagonBinding member, string wagonName)
        {
            if (string.Equals(member.Name, wagonName, StringComparison.Ordinal))
            {
                return member;
            }

            return new WagonBinding(
                wagonName,
                member.TypeDisplay,
                member.TypeSymbol,
                member.Location,
                member.IsByReference,
                member.IsOptional,
                member.PullTypeDisplay);
        }

        /// <summary>
        /// Checks whether an existing wagon type is compatible with a required input type.
        /// </summary>
        internal static bool TypesCompatible(ITypeSymbol existing, ITypeSymbol required)
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

        /// <summary>
        /// Reports tuple-return diagnostics at tuple literal locations.
        /// </summary>
        private static void ReportTupleReturnDiagnostics(
            SimulationState state,
            ImmutableArray<Location> tupleReturnLocations,
            DiagnosticDescriptor descriptor)
        {
            if (tupleReturnLocations.IsDefaultOrEmpty)
            {
                return;
            }

            for (var i = 0; i < tupleReturnLocations.Length; i++)
            {
                var location = tupleReturnLocations[i];
                if (location == null)
                {
                    continue;
                }

                state.Diagnostics.Add(Diagnostic.Create(descriptor, location));
            }
        }

    }
}
