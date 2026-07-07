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
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, null, null, null);
        }

        /// <summary>
        /// Merges a station return value into the manifest with return member names.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames)
        {
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, returnMemberNames, null, null);
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
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, null, byReferenceWagons, refLocalValues);
        }

        /// <summary>
        /// Merges a station return value into the manifest with return member names and ref wagon metadata.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
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

            for (var i = 0; i < wagonNames.Length; i++)
            {
                var wagonName = wagonNames[i];
                object wagonValue;
                var found = TryResolveWagonValue(
                    stationReturn,
                    wagonName,
                    i,
                    returnMemberNames,
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
            return StationAdapter.ToSignal(manifest, stationReturn, stationName, wagonNames, removeOmittedRegularInputs, null, null, null);
        }

        /// <summary>
        /// Converts a station return value to a signal with return member names.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames)
        {
            return StationAdapter.ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
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
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Converts a station return value to a signal with return member names and ref wagon metadata.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
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
                returnMemberNames,
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Resolves a wagon value from a station return by manifest wagon name or positional return member name.
        /// </summary>
        private static bool TryResolveWagonValue(
            object stationReturn,
            string wagonName,
            int wagonIndex,
            string[] returnMemberNames,
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

            if (returnMemberNames != null
                && wagonIndex < returnMemberNames.Length
                && !string.Equals(returnMemberNames[wagonIndex], wagonName, StringComparison.Ordinal)
                && WagonStationReturn.TryGetMemberValue(stationReturn, returnMemberNames[wagonIndex], out wagonValue))
            {
                return true;
            }

            if (WagonStationReturn.IsValueTuple(stationReturn))
            {
                return WagonStationReturn.TryGetMemberValue(
                    stationReturn,
                    "Item" + (wagonIndex + 1),
                    out wagonValue);
            }

            return false;
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
            return ToSignal(manifest, stationReturn, stationName, wagonNames, removeOmittedRegularInputs, null, null, null);
        }

        /// <summary>
        /// Converts a station return value to a signal with return member names.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames)
        {
            return ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
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
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Converts a station return value to a signal with return member names and ref wagon metadata.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
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
