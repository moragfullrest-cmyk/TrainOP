using System;
using System.Collections.Generic;

namespace TrainOP
{
    /// <summary>
    /// Shared merge logic for station handler return values into a manifest.
    /// </summary>
    public static class StationMerge
    {
        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, null, null, null);
        }

        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals)
        {
            return Apply(manifest, stationReturn, wagonNames, removeOmittedRegularInputs, tupleElementOrdinals, null, null);
        }

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

        public static CargoManifest Apply(
            CargoManifest manifest,
            object stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
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

            if (TryUnwrapOk(stationReturn, out var payload))
            {
                stationReturn = payload;
            }

            if (stationReturn is CargoManifest replacement)
            {
                return replacement;
            }

            if (stationReturn is StationDataFail)
            {
                throw new InvalidOperationException("StationDataFail must be handled by StationMerge.ToSignal.");
            }

            if (stationReturn is StationDataSkip)
            {
                return manifest;
            }

            if (wagonNames.Length == 0)
            {
                return MergeAllReturnMembers(manifest, stationReturn);
            }

            if (byReferenceWagons != null && refLocalValues != null
                && (byReferenceWagons.Length != wagonNames.Length || refLocalValues.Length != wagonNames.Length))
            {
                throw new ArgumentException("Ref wagon metadata arrays must match wagonNames length.");
            }

            var usedTupleOrdinals = new HashSet<int>();
            for (var i = 0; i < wagonNames.Length; i++)
            {
                var wagonName = wagonNames[i];
                object wagonValue;
                var found = TryResolveWagonValue(
                    manifest,
                    stationReturn,
                    wagonName,
                    i,
                    tupleElementOrdinals,
                    usedTupleOrdinals,
                    out wagonValue);
                if (found)
                {
                    manifest = manifest.LoadCar(wagonName, wagonValue);
                }
                else if (byReferenceWagons != null && byReferenceWagons[i])
                {
                    manifest = manifest.LoadCar(wagonName, refLocalValues[i]);
                }
                else if (removeOmittedRegularInputs)
                {
                    manifest = manifest.UnloadCar(wagonName);
                }
            }

            return manifest;
        }

        public static CargoManifest Apply<T>(
            CargoManifest manifest,
            T stationReturn,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return Apply(manifest, (object)stationReturn, wagonNames, removeOmittedRegularInputs);
        }

        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return StationAdapter.ToSignal(manifest, stationReturn, stationName, wagonNames, removeOmittedRegularInputs);
        }

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
                tupleElementOrdinals);
        }

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
                byReferenceWagons,
                refLocalValues);
        }

        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
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
                byReferenceWagons,
                refLocalValues);
        }

        private static bool TryResolveWagonValue(
            CargoManifest manifest,
            object stationReturn,
            string wagonName,
            int wagonIndex,
            int[] tupleElementOrdinals,
            HashSet<int> usedTupleOrdinals,
            out object wagonValue)
        {
            wagonValue = null;
            if (stationReturn == null)
            {
                return false;
            }

            if (tupleElementOrdinals != null
                && wagonIndex < tupleElementOrdinals.Length
                && tupleElementOrdinals[wagonIndex] >= 0
                && WagonStationReturn.TryGetTupleElement(stationReturn, tupleElementOrdinals[wagonIndex], out wagonValue))
            {
                return true;
            }

            if (WagonStationReturn.TryGetMemberValue(stationReturn.GetType(), stationReturn, wagonName, out wagonValue))
            {
                return true;
            }

            if (WagonStationReturn.TryGetTupleElementMatchingManifestWagon(
                stationReturn,
                manifest,
                wagonName,
                usedTupleOrdinals,
                out wagonValue))
            {
                return true;
            }

            if (tupleElementOrdinals == null
                && WagonStationReturn.TryGetTupleElement(stationReturn, wagonIndex, out wagonValue))
            {
                return true;
            }

            return false;
        }

        private static bool TryUnwrapOk(object stationReturn, out object payload)
        {
            if (stationReturn is IStationDataOk ok)
            {
                payload = ok.GetValue();
                return true;
            }

            payload = stationReturn;
            return false;
        }

        private static CargoManifest MergeAllReturnMembers(CargoManifest manifest, object stationReturn)
        {
            if (stationReturn == null)
            {
                return manifest;
            }

            var type = stationReturn.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;

            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                manifest = manifest.LoadCar(property.Name, property.GetValue(stationReturn));
            }

            foreach (var field in type.GetFields(flags))
            {
                manifest = manifest.LoadCar(field.Name, field.GetValue(stationReturn));
            }

            return manifest;
        }
    }

    internal static class StationAdapter
    {
        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs)
        {
            return ToSignal(manifest, stationReturn, stationName, wagonNames, removeOmittedRegularInputs, null, null, null);
        }

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
                null);
        }

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

        public static Signal ToSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            string[] wagonNames,
            bool removeOmittedRegularInputs,
            int[] tupleElementOrdinals,
            bool[] byReferenceWagons,
            object[] refLocalValues)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (stationReturn is StationDataFail fail)
            {
                return RailwaySignals.Red(
                    manifest,
                    new SignalIssue(fail.Code, fail.Message, stationName));
            }

            if (stationReturn is StationDataSkip)
            {
                return RailwaySignals.Green(manifest);
            }

            if (TryUnwrapOk(stationReturn, out var payload))
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
                byReferenceWagons,
                refLocalValues);
            return RailwaySignals.Green(merged);
        }

        private static bool TryUnwrapOk(object stationReturn, out object payload)
        {
            if (stationReturn is IStationDataOk ok)
            {
                payload = ok.GetValue();
                return true;
            }

            payload = stationReturn;
            return false;
        }
    }
}
