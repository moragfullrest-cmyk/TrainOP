using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TrainOP
{
    /// <summary>
    /// Shared merge logic for station handler return values into a manifest.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
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

            return ApplyCore(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                preserveManifestComposition: false);
        }

        /// <summary>
        /// Overlays a service-station return onto the live manifest without adding or removing wagons.
        /// Existing keys are updated from the return (or ref writeback); extra return members are applied
        /// only when the key already exists.
        /// </summary>
        public static CargoManifest ApplyOverlay(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
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

            return ApplyCore(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs: false,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                preserveManifestComposition: true);
        }

        private static CargoManifest ApplyCore(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues,
            bool preserveManifestComposition)
        {
            if (TryUnwrapGreenPayload(stationReturn, out var payload))
            {
                stationReturn = payload;
            }

            if (stationReturn is CargoManifest replacement)
            {
                if (preserveManifestComposition)
                {
                    OverlayExistingKeys(manifest, replacement);
                    return manifest;
                }

                return replacement;
            }

            if (stationReturn is RedFailure)
            {
                throw new InvalidOperationException("RedFailure must be handled by StationMerge.ToSignal.");
            }

            if (stationReturn is WhitePass)
            {
                return manifest;
            }

            if (wagonNames.Length == 0)
            {
                return preserveManifestComposition
                    ? OverlayNamedMembersOntoExisting(manifest, stationReturn, returnMemberNames)
                    : MergeAllReturnMembers(manifest, stationReturn, returnMemberNames);
            }

            ValidateRefWagonMetadata(wagonNames, byReferenceWagons, refLocalValues, requirePresent: false);

            // Parity with MergePlanBuilder: positional ItemN (or named return members) consumed for
            // input wagons must not be re-loaded as extra manifest keys.
            var consumedReturnMembers = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < wagonNames.Length; i++)
            {
                var wagonName = wagonNames[i];
                object wagonValue;
                string consumedMemberName;
                var found = TryResolveWagonValue(
                    stationReturn,
                    wagonName,
                    i,
                    returnMemberNames,
                    out wagonValue,
                    out consumedMemberName);
                if (!found
                    && WagonStationReturn.IsValueTuple(stationReturn)
                    && manifest.TryGetWagon(wagonName, out var missingMatchValue)
                    && missingMatchValue != null)
                {
                    found = TryResolveUniqueTupleElementByType(
                        stationReturn,
                        missingMatchValue.GetType(),
                        out wagonValue,
                        out consumedMemberName);
                }
                else if (found
                    && WagonStationReturn.IsValueTuple(stationReturn)
                    && wagonValue != null
                    && manifest.TryGetWagon(wagonName, out var existingValue)
                    && existingValue != null
                    && !WagonStationReturn.TypesCompatible(existingValue.GetType(), wagonValue.GetType()))
                {
                    found = TryResolveUniqueTupleElementByType(
                        stationReturn,
                        existingValue.GetType(),
                        out wagonValue,
                        out consumedMemberName);
                }
                if (found)
                {
                    TryLoadWagon(manifest, wagonName, wagonValue, preserveManifestComposition);
                    if (!string.IsNullOrEmpty(consumedMemberName))
                    {
                        consumedReturnMembers.Add(consumedMemberName);
                    }
                }
                else if (byReferenceWagons != null && byReferenceWagons[i])
                {
                    TryLoadWagon(manifest, wagonName, refLocalValues[i], preserveManifestComposition);
                }
                else if (!preserveManifestComposition && removeOmittedRegularInputs)
                {
                    manifest.UnloadWagon(wagonName);
                }
            }

            if (wagonNames.Length > 0 && stationReturn != null)
            {
                var extraMemberNames = returnMemberNames ?? WagonStationReturn.GetMemberNames(stationReturn);
                foreach (var memberName in extraMemberNames)
                {
                    if (consumedReturnMembers.Contains(memberName))
                    {
                        continue;
                    }

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
                        TryLoadWagon(manifest, memberName, extraValue, preserveManifestComposition);
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
        /// Converts a service-station return value to a signal, overlaying existing wagons only.
        /// </summary>
        public static Signal ToServiceSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            return ToServiceSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                returnMemberNames: null,
                byReferenceWagons,
                refLocalValues);
        }

        /// <summary>
        /// Converts a service-station return value to a signal, overlaying existing wagons only.
        /// </summary>
        public static Signal ToServiceSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            return StationAdapter.ToServiceSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                returnMemberNames,
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
            out object wagonValue,
            out string consumedMemberName)
        {
            wagonValue = null;
            consumedMemberName = null;
            if (stationReturn == null)
            {
                return false;
            }

            if (WagonStationReturn.TryGetMemberValue(stationReturn, wagonName, out wagonValue))
            {
                consumedMemberName = wagonName;
                return true;
            }

            if (returnMemberNames != null
                && wagonIndex < returnMemberNames.Length
                && WagonStationReturn.IsValueTuple(stationReturn)
                && !string.Equals(returnMemberNames[wagonIndex], wagonName, StringComparison.Ordinal)
                && WagonStationReturn.TryGetMemberValue(stationReturn, returnMemberNames[wagonIndex], out wagonValue))
            {
                consumedMemberName = returnMemberNames[wagonIndex];
                return true;
            }

            if (WagonStationReturn.IsValueTuple(stationReturn))
            {
                var ordinalName = "Item" + (wagonIndex + 1);
                if (WagonStationReturn.TryGetMemberValue(stationReturn, ordinalName, out wagonValue))
                {
                    consumedMemberName = ordinalName;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves a unique tuple element by type and reports the ItemN member that supplied it.
        /// </summary>
        private static bool TryResolveUniqueTupleElementByType(
            object stationReturn,
            Type expectedType,
            out object wagonValue,
            out string consumedMemberName)
        {
            wagonValue = null;
            consumedMemberName = null;
            if (!WagonStationReturn.TryGetUniqueTupleElementByType(stationReturn, expectedType, out wagonValue))
            {
                return false;
            }

            for (var ordinal = 0; ; ordinal++)
            {
                if (!WagonStationReturn.TryGetTupleElement(stationReturn, ordinal, out var element))
                {
                    break;
                }

                if (element == null)
                {
                    if (wagonValue != null)
                    {
                        continue;
                    }
                }
                else if (!Equals(element, wagonValue))
                {
                    continue;
                }

                consumedMemberName = "Item" + (ordinal + 1);
                return true;
            }

            return true;
        }

        /// <summary>
        /// Loads <paramref name="wagonValue"/> into an existing manifest key, or always when composition
        /// is allowed to change.
        /// </summary>
        private static void TryLoadWagon(
            CargoManifest manifest,
            string wagonName,
            object wagonValue,
            bool preserveManifestComposition)
        {
            if (preserveManifestComposition && !manifest.HasWagon(wagonName))
            {
                return;
            }

            manifest.LoadWagon(wagonName, wagonValue);
        }

        /// <summary>
        /// Copies values from <paramref name="source"/> onto keys that already exist on <paramref name="target"/>.
        /// </summary>
        private static void OverlayExistingKeys(CargoManifest target, CargoManifest source)
        {
            if (source == null || ReferenceEquals(target, source))
            {
                return;
            }

            var live = target.InspectWagons();
            var names = new string[live.Count];
            var index = 0;
            foreach (var pair in live)
            {
                names[index++] = pair.Key;
            }

            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                if (source.TryGetWagon(name, out var value))
                {
                    target.LoadWagon(name, value);
                }
            }
        }

        /// <summary>
        /// Loads named return members onto keys that already exist in the manifest.
        /// </summary>
        private static CargoManifest OverlayNamedMembersOntoExisting(
            CargoManifest manifest,
            object stationReturn,
            string[] returnMemberNames)
        {
            if (stationReturn == null)
            {
                return manifest;
            }

            var memberNames = returnMemberNames ?? WagonStationReturn.GetMemberNames(stationReturn);
            foreach (var memberName in memberNames)
            {
                if (manifest.HasWagon(memberName)
                    && WagonStationReturn.TryGetMemberValue(stationReturn, memberName, out var value))
                {
                    manifest.LoadWagon(memberName, value);
                }
            }

            return manifest;
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
