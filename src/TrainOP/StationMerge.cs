using System;

namespace TrainOP
{
    /// <summary>
    /// Shared merge logic for station handler return values into a manifest.
    /// </summary>
    public static class StationMerge
    {
        /// <summary>
        /// Merges a station return value into the manifest using wagon name mappings.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, null, null, null, null);
        }

        /// <summary>
        /// Merges a station return value into the manifest with tuple element ordinal mappings.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals)
        {
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, tupleElementOrdinals, null, null, null);
        }

        /// <summary>
        /// Merges a station return value into the manifest with tuple ordinals and return member names.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
            string[] returnMemberNames)
        {
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, tupleElementOrdinals, returnMemberNames, null, null);
        }

        /// <summary>
        /// Merges a station return value into the manifest with by-reference wagon metadata.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, null, null, byReferenceWagons, refLocalValues);
        }

        /// <summary>
        /// Merges a station return value into the manifest with full wagon and tuple metadata.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (wagonNames == null)
            {
                throw new ArgumentNullException(nameof(wagonNames));
            }

            if (TryUnwrapGreenPayload(stationReturn, out var payload))
            {
                stationReturn = payload;
            }

            if (stationReturn is CargoManifest replacement)
            {
                return replacement;
            }

            if (stationReturn is RedFailure)
            {
                throw new InvalidOperationException("RedFailure must be handled by StationMerge.ToSignal.");
            }

            if (stationReturn is GreenPass)
            {
                return manifest;
            }

            if (wagonNames.Length == 0)
            {
                return MergeAllReturnMembers(manifest, stationReturn, returnMemberNames);
            }

            if (byReferenceWagons != null && refLocalValues != null
                && (byReferenceWagons.Length != wagonNames.Length || refLocalValues.Length != wagonNames.Length))
            {
                throw new ArgumentException("Ref wagon metadata arrays must match wagonNames length.");
            }

            var effectiveTupleOrdinals = ResolveTupleOrdinals(stationReturn, tupleElementOrdinals);
            for (var i = 0; i < wagonNames.Length; i++)
            {
                var wagonName = wagonNames[i];
                object wagonValue;
                var found = TryResolveWagonValue(
                    stationReturn,
                    wagonName,
                    i,
                    effectiveTupleOrdinals,
                    out wagonValue);
                if (!found
                    && WagonStationReturn.IsValueTuple(stationReturn)
                    && manifest.HasWagon(wagonName))
                {
                    var existingWagons = manifest.InspectWagons();
                    if (existingWagons.TryGetValue(wagonName, out var existingValue)
                        && existingValue != null)
                    {
                        found = WagonStationReturn.TryGetUniqueTupleElementByType(
                            stationReturn,
                            existingValue.GetType(),
                            out wagonValue);
                    }
                }
                else if (found
                    && WagonStationReturn.IsValueTuple(stationReturn)
                    && manifest.HasWagon(wagonName)
                    && wagonValue != null)
                {
                    var existingWagons = manifest.InspectWagons();
                    if (existingWagons.TryGetValue(wagonName, out var existingValue)
                        && existingValue != null
                        && !WagonStationReturn.TypesCompatible(existingValue.GetType(), wagonValue.GetType()))
                    {
                        found = WagonStationReturn.TryGetUniqueTupleElementByType(
                            stationReturn,
                            existingValue.GetType(),
                            out wagonValue);
                    }
                }
                if (found)
                {
                    manifest = manifest.LoadWagon(wagonName, wagonValue);
                }
                else if (byReferenceWagons != null && byReferenceWagons[i])
                {
                    manifest = manifest.LoadWagon(wagonName, refLocalValues[i]);
                }
                else if (removeOmittedRegularInputs)
                {
                    manifest = manifest.UnloadWagon(wagonName);
                }
            }

            return manifest;
        }

        /// <summary>
        /// Merges a typed station return value into the manifest using wagon name mappings.
        /// </summary>
        public static CargoManifest Apply<T>(
            CargoManifest manifest,
            T stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return Apply(manifest, (object)stationReturn, wagonNames, removeOmittedRegularInputs);
        }

        /// <summary>
        /// Converts a station return value to a signal after merging into the manifest.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return StationAdapter.ToSignal(manifest, stationReturn, stationName, wagonNames, removeOmittedRegularInputs, null, null, null, null);
        }

        /// <summary>
        /// Converts a station return value to a signal with tuple element ordinal mappings.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals)
        {
            return StationAdapter.ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                tupleElementOrdinals,
                null,
                null,
                null);
        }

        /// <summary>
        /// Converts a station return value to a signal with tuple ordinals and return member names.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
            string[] returnMemberNames)
        {
            return StationAdapter.ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                tupleElementOrdinals,
                returnMemberNames,
                null,
                null);
        }

        /// <summary>
        /// Converts a station return value to a signal with by-reference wagon metadata.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            return StationAdapter.ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                null,
                null,
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Converts a station return value to a signal with full wagon and tuple metadata.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            return StationAdapter.ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                tupleElementOrdinals,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Returns tuple element ordinals when the station return is a value tuple.
        /// </summary>
        private static int[] ResolveTupleOrdinals(object stationReturn, int[] tupleElementOrdinals)
        {
            if (tupleElementOrdinals == null || !WagonStationReturn.IsValueTuple(stationReturn))
            {
                return null;
            }

            return tupleElementOrdinals;
        }

        /// <summary>
        /// Resolves a wagon value from a station return by name, index, or tuple ordinal.
        /// </summary>
        private static bool TryResolveWagonValue(
            object stationReturn,
            string wagonName,
            int wagonIndex,
            int[] tupleElementOrdinals,
            out object wagonValue)
        {
            wagonValue = null;
            if (stationReturn == null)
            {
                return false;
            }

            if (WagonStationReturn.TryGetMemberValue(stationReturn, wagonName, out wagonValue))
            {
                return true;
            }

            if (tupleElementOrdinals != null && wagonIndex < tupleElementOrdinals.Length)
            {
                var ordinal = tupleElementOrdinals[wagonIndex];
                if (ordinal < 0)
                {
                    return false;
                }

                return WagonStationReturn.TryGetTupleElement(stationReturn, ordinal, out wagonValue);
            }

            return WagonStationReturn.TryGetTupleElement(stationReturn, wagonIndex, out wagonValue);
        }

        /// <summary>
        /// Unwraps a green payload wrapper to its inner value.
        /// </summary>
        private static bool TryUnwrapGreenPayload(object stationReturn, out object payload)
        {
            if (stationReturn is IGreenPayload greenPayload)
            {
                payload = greenPayload.GetValue();
                return true;
            }

            payload = stationReturn;
            return false;
        }

        /// <summary>
        /// Loads all named return members into the manifest when no wagon mappings are configured.
        /// </summary>
        private static CargoManifest MergeAllReturnMembers(
            CargoManifest manifest,
            object stationReturn,
            string[] returnMemberNames)
        {
            if (stationReturn == null || returnMemberNames == null || returnMemberNames.Length == 0)
            {
                return manifest;
            }

            foreach (var memberName in returnMemberNames)
            {
                if (WagonStationReturn.TryGetMemberValue(stationReturn, memberName, out var value))
                {
                    manifest = manifest.LoadWagon(memberName, value);
                }
            }

            return manifest;
        }
    }

    /// <summary>
    /// Converts station handler return values into route signals for generated adapters.
    /// </summary>
    internal static class StationAdapter
    {
        /// <summary>
        /// Converts a station return value to a signal after merging into the manifest.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return ToSignal(manifest, stationReturn, stationName, wagonNames, removeOmittedRegularInputs, null, null, null, null);
        }

        /// <summary>
        /// Converts a station return value to a signal with tuple element ordinal mappings.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals)
        {
            return ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                tupleElementOrdinals,
                null,
                null,
                null);
        }

        /// <summary>
        /// Converts a station return value to a signal with tuple ordinals and return member names.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
            string[] returnMemberNames)
        {
            return ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                tupleElementOrdinals,
                returnMemberNames,
                null,
                null);
        }

        /// <summary>
        /// Converts a station return value to a signal with by-reference wagon metadata.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            return ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                null,
                null,
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Converts a station return value to a signal with full wagon and tuple metadata.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (stationReturn is Signal signal)
            {
                return signal;
            }

            if (stationReturn is RedFailure fail)
            {
                return RailwaySignals.Red(
                    manifest,
                    new SignalIssue(fail.Code, fail.Message, stationName));
            }

            if (stationReturn is GreenPass)
            {
                return RailwaySignals.Green(manifest);
            }

            if (TryUnwrapGreenPayload(stationReturn, out var payload))
            {
                stationReturn = payload;
            }

            if (stationReturn is CargoManifest replacement)
            {
                return RailwaySignals.Green(replacement);
            }

            var merged = StationMerge.Apply(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                tupleElementOrdinals,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues);
            return RailwaySignals.Green(merged);
        }

        /// <summary>
        /// Unwraps a green payload wrapper to its inner value.
        /// </summary>
        private static bool TryUnwrapGreenPayload(object stationReturn, out object payload)
        {
            if (stationReturn is IGreenPayload greenPayload)
            {
                payload = greenPayload.GetValue();
                return true;
            }

            payload = stationReturn;
            return false;
        }
    }
}
