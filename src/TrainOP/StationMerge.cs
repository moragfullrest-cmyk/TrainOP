using System;

namespace TrainOP
{
    /// <summary>
    /// Shared merge logic for station handler return values into a manifest.
    /// </summary>
    public static class StationMerge
    {
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

            ValidateRefWagonMetadata(wagonNames, byReferenceWagons, refLocalValues, requirePresent: false);

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
                    && manifest.TryGetWagon(wagonName, out var missingMatchValue)
                    && missingMatchValue != null)
                {
                    found = WagonStationReturn.TryGetUniqueTupleElementByType(
                        stationReturn,
                        missingMatchValue.GetType(),
                        out wagonValue);
                }
                else if (found
                    && WagonStationReturn.IsValueTuple(stationReturn)
                    && wagonValue != null
                    && manifest.TryGetWagon(wagonName, out var existingValue)
                    && existingValue != null
                    && !WagonStationReturn.TypesCompatible(existingValue.GetType(), wagonValue.GetType()))
                {
                    found = WagonStationReturn.TryGetUniqueTupleElementByType(
                        stationReturn,
                        existingValue.GetType(),
                        out wagonValue);
                }
                if (found)
                {
                    manifest.LoadWagon(wagonName, wagonValue);
                }
                else if (byReferenceWagons != null && byReferenceWagons[i])
                {
                    manifest.LoadWagon(wagonName, refLocalValues[i]);
                }
                else if (removeOmittedRegularInputs)
                {
                    manifest.UnloadWagon(wagonName);
                }
            }

            if (wagonNames.Length > 0 && stationReturn != null)
            {
                var extraMemberNames = returnMemberNames ?? WagonStationReturn.GetMemberNames(stationReturn);
                foreach (var memberName in extraMemberNames)
                {
                    var isInputWagon = false;
                    for (var j = 0; j < wagonNames.Length; j++)
                    {
                        if (string.Equals(wagonNames[j], memberName, StringComparison.Ordinal))
                        {
                            isInputWagon = true;
                            break;
                        }
                    }

                    if (!isInputWagon
                        && WagonStationReturn.TryGetMemberValue(stationReturn, memberName, out var extraValue))
                    {
                        manifest.LoadWagon(memberName, extraValue);
                    }
                }
            }

            return manifest;
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
        /// Writes ref wagon values back into the manifest without adding or removing cargo.
        /// </summary>
        public static CargoManifest ApplyRefOnly(
            CargoManifest manifest,
            string[] wagonNames,
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

            ValidateRefWagonMetadata(wagonNames, byReferenceWagons, refLocalValues, requirePresent: true);
            WritebackRefWagons(manifest, wagonNames, byReferenceWagons, refLocalValues);
            return manifest;
        }

        /// <summary>
        /// Converts a service-station return value to a signal using ref writeback only.
        /// </summary>
        public static Signal ToServiceSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            return StationAdapter.ToServiceSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Writes by-ref wagon locals back into the manifest where the ref flag is set.
        /// </summary>
        internal static void WritebackRefWagons(
            CargoManifest manifest,
            string[] wagonNames,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            if (byReferenceWagons == null || refLocalValues == null)
            {
                return;
            }

            for (var i = 0; i < wagonNames.Length; i++)
            {
                if (byReferenceWagons[i])
                {
                    manifest.LoadWagon(wagonNames[i], refLocalValues[i]);
                }
            }
        }

        /// <summary>
        /// Validates that ref metadata arrays match <paramref name="wagonNames"/> length when present.
        /// </summary>
        internal static void ValidateRefWagonMetadata(
            string[] wagonNames,
            bool[] byReferenceWagons,
            object[] refLocalValues,
            bool requirePresent)
        {
            if (requirePresent)
            {
                if (byReferenceWagons == null || refLocalValues == null
                    || byReferenceWagons.Length != wagonNames.Length
                    || refLocalValues.Length != wagonNames.Length)
                {
                    throw new ArgumentException("Ref wagon metadata arrays must match wagonNames length.");
                }

                return;
            }

            if (byReferenceWagons != null && refLocalValues != null
                && (byReferenceWagons.Length != wagonNames.Length || refLocalValues.Length != wagonNames.Length))
            {
                throw new ArgumentException("Ref wagon metadata arrays must match wagonNames length.");
            }
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
                && WagonStationReturn.IsValueTuple(stationReturn)
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
            if (stationReturn == null)
            {
                return manifest;
            }

            var memberNames = returnMemberNames ?? WagonStationReturn.GetMemberNames(stationReturn);
            if (memberNames.Length == 0)
            {
                return manifest;
            }

            foreach (var memberName in memberNames)
            {
                if (WagonStationReturn.TryGetMemberValue(stationReturn, memberName, out var value))
                {
                    manifest.LoadWagon(memberName, value);
                }
            }

            return manifest;
        }
    }
}
