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
            return Apply(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                null,
                null,
                allocateDefaultItemNElements: false);
        }

        /// <summary>
        /// Merges a station return value into the manifest with return member names and default-ItemN allocation.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames,
            bool allocateDefaultItemNElements)
        {
            return Apply(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                null,
                null,
                allocateDefaultItemNElements);
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
            return Apply(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                null,
                byReferenceWagons,
                refLocalValues,
                allocateDefaultItemNElements: false);
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
            return Apply(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                allocateDefaultItemNElements: false);
        }

        /// <summary>
        /// Merges a station return value into the manifest with return member names, ref metadata,
        /// and optional allocation of default ItemN tuple elements as new wagons.
        /// </summary>
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues,
            bool allocateDefaultItemNElements)
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
                preserveManifestComposition: false,
                allocateDefaultItemNElements);
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
            return ApplyOverlay(
                manifest,
                stationReturn,
                wagonNames,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                allocateDefaultItemNElements: false);
        }

        /// <summary>
        /// Overlays a service-station return onto the live manifest without adding or removing wagons.
        /// </summary>
        public static CargoManifest ApplyOverlay(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues,
            bool allocateDefaultItemNElements)
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
                preserveManifestComposition: true,
                allocateDefaultItemNElements);
        }

        private static CargoManifest ApplyCore(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues,
            bool preserveManifestComposition,
            bool allocateDefaultItemNElements)
        {
            stationReturn = WagonStationReturn.UnwrapGreenPayloadReturn(stationReturn);

            if (TryApplySpecialReturn(
                manifest,
                stationReturn,
                wagonNames,
                returnMemberNames,
                preserveManifestComposition,
                allocateDefaultItemNElements,
                out var earlyResult))
            {
                return earlyResult;
            }

            ValidateRefWagonMetadata(wagonNames, byReferenceWagons, refLocalValues, requirePresent: false);

            // Named matches and ref writeback first; omitted non-ref inputs unload before ItemN allocation
            // so sequential unnamed tuples can reuse Item1… after the previous hop spent them.
            var consumedReturnMembers = ApplyNamedInputWagons(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                preserveManifestComposition,
                allocateDefaultItemNElements);

            ApplyExtraReturnMembers(
                manifest,
                stationReturn,
                wagonNames,
                returnMemberNames,
                consumedReturnMembers,
                preserveManifestComposition,
                allocateDefaultItemNElements);

            return manifest;
        }

        /// <summary>
        /// Handles CargoManifest replacement/overlay, RedFailure, WhitePass, and empty input-wagon maps.
        /// </summary>
        private static bool TryApplySpecialReturn(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            string[] returnMemberNames,
            bool preserveManifestComposition,
            bool allocateDefaultItemNElements,
            out CargoManifest result)
        {
            if (stationReturn is CargoManifest replacement)
            {
                if (preserveManifestComposition)
                {
                    OverlayExistingKeys(manifest, replacement);
                    result = manifest;
                    return true;
                }

                result = replacement;
                return true;
            }

            if (stationReturn is RedFailure)
            {
                throw new InvalidOperationException(
                    "RedFailure must be handled by StationAdapter.TryConvertPassthroughSignal.");
            }

            if (stationReturn is WhitePass)
            {
                result = manifest;
                return true;
            }

            if (wagonNames.Length == 0)
            {
                result = preserveManifestComposition
                    ? OverlayNamedMembersOntoExisting(manifest, stationReturn, returnMemberNames)
                    : MergeAllReturnMembers(
                        manifest,
                        stationReturn,
                        returnMemberNames,
                        allocateDefaultItemNElements);
                return true;
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Resolves named input wagons (and ref writeback / unload) from the station return.
        /// </summary>
        private static HashSet<string> ApplyNamedInputWagons(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues,
            bool preserveManifestComposition,
            bool allocateDefaultItemNElements)
        {
            var consumedReturnMembers = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < wagonNames.Length; i++)
            {
                var wagonName = wagonNames[i];
                object wagonValue;
                string consumedMemberName;
                var found = TryResolveWagonValueByName(
                    stationReturn,
                    wagonName,
                    returnMemberNames,
                    out wagonValue,
                    out consumedMemberName);
                if (found
                    && allocateDefaultItemNElements
                    && ShouldAllocateDefaultItemMember(consumedMemberName, IndexOfMember(returnMemberNames, consumedMemberName)))
                {
                    // Default ItemN accessors are never positional/name maps onto inputs.
                    found = false;
                    consumedMemberName = null;
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

            return consumedReturnMembers;
        }

        /// <summary>
        /// Loads leftover return members (named extras and default ItemN allocations).
        /// </summary>
        private static void ApplyExtraReturnMembers(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            string[] returnMemberNames,
            HashSet<string> consumedReturnMembers,
            bool preserveManifestComposition,
            bool allocateDefaultItemNElements)
        {
            if (stationReturn == null)
            {
                return;
            }

            var extraMemberNames = returnMemberNames ?? WagonStationReturn.GetMemberNames(stationReturn);
            for (var memberIndex = 0; memberIndex < extraMemberNames.Length; memberIndex++)
            {
                var memberName = extraMemberNames[memberIndex];
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

                var allocateAsItemN = allocateDefaultItemNElements
                    && ShouldAllocateDefaultItemMember(memberName, memberIndex);

                // Named returns that collide with input keys were already handled above.
                // Default ItemN elements must still allocate after spend/unload (parity with MergePlanBuilder).
                if (isInputWagon && !allocateAsItemN)
                {
                    continue;
                }

                if (!WagonStationReturn.TryGetMemberValue(
                    stationReturn,
                    memberName,
                    returnMemberNames,
                    out var extraValue))
                {
                    continue;
                }

                if (allocateAsItemN)
                {
                    if (preserveManifestComposition)
                    {
                        // ServiceStation cannot add wagons; skip (analyzer TOP015).
                        continue;
                    }

                    ItemWagonNames.LoadNextItemWagon(manifest, extraValue);
                    continue;
                }

                TryLoadWagon(manifest, memberName, extraValue, preserveManifestComposition);
            }
        }

        private static int IndexOfMember(string[] returnMemberNames, string memberName)
        {
            if (returnMemberNames == null || memberName == null)
            {
                return -1;
            }

            for (var i = 0; i < returnMemberNames.Length; i++)
            {
                if (string.Equals(returnMemberNames[i], memberName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Default ItemN elements (when the shape opted into allocation) become new <c>ItemN</c> wagons.
        /// </summary>
        private static bool ShouldAllocateDefaultItemMember(string memberName, int memberIndex)
        {
            if (memberIndex < 0)
            {
                return false;
            }

            return ItemWagonNames.TryParseItemIndex(memberName, out var parsed)
                && parsed == memberIndex + 1;
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
                null,
                allocateDefaultItemNElements: false);
        }

        /// <summary>
        /// Converts a station return value to a signal with return member names and default-ItemN allocation.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames,
            bool allocateDefaultItemNElements)
        {
            return StationAdapter.ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                null,
                null,
                allocateDefaultItemNElements);
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
                refLocalValues,
                allocateDefaultItemNElements: false);
        }

        /// <summary>
        /// Converts a station return value to a signal with return member names, ref metadata,
        /// and optional default-ItemN allocation.
        /// </summary>
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            string[] returnMemberNames,
            bool[] byReferenceWagons,
            object[] refLocalValues,
            bool allocateDefaultItemNElements)
        {
            return StationAdapter.ToSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                allocateDefaultItemNElements);
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
                refLocalValues,
                allocateDefaultItemNElements: false);
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
            object[] refLocalValues,
            bool allocateDefaultItemNElements)
        {
            return StationAdapter.ToServiceSignal(
                manifest,
                stationReturn,
                stationName,
                wagonNames,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                allocateDefaultItemNElements);
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
        /// Resolves a wagon value from a station return by exact member / wagon name,
        /// or by ordinal in <paramref name="returnMemberNames"/> for value tuples.
        /// </summary>
        private static bool TryResolveWagonValueByName(
            object stationReturn,
            string wagonName,
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

            if (WagonStationReturn.TryGetMemberValue(
                stationReturn,
                wagonName,
                returnMemberNames,
                out wagonValue))
            {
                consumedMemberName = wagonName;
                return true;
            }

            return false;
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
                    && WagonStationReturn.TryGetMemberValue(
                        stationReturn,
                        memberName,
                        returnMemberNames,
                        out var value))
                {
                    manifest.LoadWagon(memberName, value);
                }
            }

            return manifest;
        }

        /// <summary>
        /// Loads all named return members into the manifest when no wagon mappings are configured.
        /// Default ItemN elements (when allocation is enabled) receive sequential <c>ItemN</c> keys.
        /// </summary>
        private static CargoManifest MergeAllReturnMembers(
            CargoManifest manifest,
            object stationReturn,
            string[] returnMemberNames,
            bool allocateDefaultItemNElements)
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

            for (var i = 0; i < memberNames.Length; i++)
            {
                var memberName = memberNames[i];
                if (!WagonStationReturn.TryGetMemberValue(
                    stationReturn,
                    memberName,
                    returnMemberNames,
                    out var value))
                {
                    continue;
                }

                if (allocateDefaultItemNElements && ShouldAllocateDefaultItemMember(memberName, i))
                {
                    ItemWagonNames.LoadNextItemWagon(manifest, value);
                }
                else
                {
                    manifest.LoadWagon(memberName, value);
                }
            }

            return manifest;
        }
    }
}
