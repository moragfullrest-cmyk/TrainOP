using System;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Handlers
{
    /// <summary>
    /// Canonical description of a handler's input parameters: wagons plus framework slots.
    /// Owns Station vs ServiceStation call/delegate ordering and wagon-name projections.
    /// </summary>
    internal sealed class HandlerInputParameters
    {
        /// <summary>
        /// Creates input parameters from wagons and framework-slot flags discovered by the schema builder.
        /// </summary>
        public HandlerInputParameters(
            ImmutableArray<WagonBinding> wagons,
            HandlerStationKind stationKind,
            bool includeManifest,
            bool includeRedSignal,
            bool includeSignalIssue,
            bool hasCancellationToken)
        {
            Wagons = wagons.IsDefault ? ImmutableArray<WagonBinding>.Empty : wagons;
            StationKind = stationKind;
            IncludeManifest = includeManifest;
            IncludeRedSignal = includeRedSignal;
            IncludeSignalIssue = includeSignalIssue;
            HasCancellationToken = hasCancellationToken;
            HasRefWagons = ComputeHasRefWagons(Wagons);
            CallOrder = BuildCallOrder(
                Wagons,
                stationKind,
                includeManifest,
                includeRedSignal,
                includeSignalIssue,
                hasCancellationToken);
        }

        /// <summary>Wagon inputs in declaration order.</summary>
        public ImmutableArray<WagonBinding> Wagons { get; }

        /// <summary>Station vs ServiceStation kind for this handler schema.</summary>
        public HandlerStationKind StationKind { get; }

        /// <summary>True when this schema is for ServiceStation (wagons-first call order).</summary>
        public bool IsServiceStation => StationKind.IsServiceStation();

        /// <summary>Handler accepts <c>CargoManifest</c>.</summary>
        public bool IncludeManifest { get; }

        /// <summary>Handler accepts <c>RedSignal</c>.</summary>
        public bool IncludeRedSignal { get; }

        /// <summary>Handler accepts <c>SignalIssue</c>.</summary>
        public bool IncludeSignalIssue { get; }

        /// <summary>Handler accepts <c>CancellationToken</c>.</summary>
        public bool HasCancellationToken { get; }

        /// <summary>True when at least one wagon is by-ref.</summary>
        public bool HasRefWagons { get; }

        /// <summary>
        /// Parameters in the order used by generated delegates and handler invocations.
        /// ServiceStation: wagons, RedSignal, token. Station: red/issue/manifest, wagons, token.
        /// </summary>
        public ImmutableArray<HandlerCallSlot> CallOrder { get; }

        /// <summary>Compares two wagon arrays for matching names in the same order.</summary>
        public static bool WagonNamesMatch(ImmutableArray<WagonBinding> left, ImmutableArray<WagonBinding> right)
        {
            if (left.IsDefault)
            {
                left = ImmutableArray<WagonBinding>.Empty;
            }

            if (right.IsDefault)
            {
                right = ImmutableArray<WagonBinding>.Empty;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Formats wagon names as a comma-separated diagnostic / grouping key.</summary>
        public static string FormatWagonNames(ImmutableArray<WagonBinding> wagons)
        {
            if (wagons.IsDefaultOrEmpty)
            {
                return "(none)";
            }

            var names = new string[wagons.Length];
            for (var i = 0; i < wagons.Length; i++)
            {
                names[i] = wagons[i].Name;
            }

            return string.Join(", ", names);
        }

        private static bool ComputeHasRefWagons(ImmutableArray<WagonBinding> wagons)
        {
            for (var i = 0; i < wagons.Length; i++)
            {
                if (wagons[i].IsByReference)
                {
                    return true;
                }
            }

            return false;
        }

        private static ImmutableArray<HandlerCallSlot> BuildCallOrder(
            ImmutableArray<WagonBinding> wagons,
            HandlerStationKind stationKind,
            bool includeManifest,
            bool includeRedSignal,
            bool includeSignalIssue,
            bool hasCancellationToken)
        {
            var slots = ImmutableArray.CreateBuilder<HandlerCallSlot>(
                wagons.Length + 4);

            if (stationKind.IsServiceStation())
            {
                AppendWagonSlots(slots, wagons);

                if (includeRedSignal)
                {
                    slots.Add(HandlerCallSlot.Special(HandlerInputKind.RedSignal));
                }
            }
            else
            {
                if (includeRedSignal)
                {
                    slots.Add(HandlerCallSlot.Special(HandlerInputKind.RedSignal));
                }

                if (includeSignalIssue)
                {
                    slots.Add(HandlerCallSlot.Special(HandlerInputKind.SignalIssue));
                }

                if (includeManifest)
                {
                    slots.Add(HandlerCallSlot.Special(HandlerInputKind.CargoManifest));
                }

                AppendWagonSlots(slots, wagons);
            }

            if (hasCancellationToken)
            {
                slots.Add(HandlerCallSlot.Special(HandlerInputKind.CancellationToken));
            }

            return slots.ToImmutable();
        }

        private static void AppendWagonSlots(
            ImmutableArray<HandlerCallSlot>.Builder slots,
            ImmutableArray<WagonBinding> wagons)
        {
            for (var i = 0; i < wagons.Length; i++)
            {
                slots.Add(HandlerCallSlot.ForWagon(i, wagons[i]));
            }
        }
    }
}
