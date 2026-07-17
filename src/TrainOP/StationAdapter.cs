using System;

namespace TrainOP
{
    /// <summary>
    /// Converts station handler return values into route signals for generated adapters.
    /// Special returns (RedFailure / GreenPass / Signal / payload / CargoManifest) are classified once.
    /// </summary>
    internal static class StationAdapter
    {
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

            if (TryConvertPassthroughSignal(manifest, stationReturn, stationName, out var passthrough))
            {
                return passthrough;
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
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (stationReturn is RedFailure fail)
            {
                return RailwaySignals.Red(
                    manifest,
                    new SignalIssue(fail.Code, fail.Message, stationName));
            }

            var merged = StationMerge.ApplyRefOnly(
                manifest,
                wagonNames,
                byReferenceWagons,
                refLocalValues);

            if (TryConvertPassthroughSignal(merged, stationReturn, stationName, out var passthrough))
            {
                return passthrough;
            }

            return RailwaySignals.Green(merged);
        }

        /// <summary>
        /// Handles returns that are already route-facing signals (or RedFailure / GreenPass requests).
        /// Shared by Station and ServiceStation conversion paths.
        /// </summary>
        private static bool TryConvertPassthroughSignal(
            CargoManifest manifest,
            object stationReturn,
            string stationName,
            out Signal signal)
        {
            if (stationReturn is RedFailure fail)
            {
                signal = RailwaySignals.Red(
                    manifest,
                    new SignalIssue(fail.Code, fail.Message, stationName));
                return true;
            }

            if (stationReturn is GreenPass)
            {
                signal = RailwaySignals.Green(manifest);
                return true;
            }

            if (stationReturn is Signal directSignal)
            {
                signal = directSignal;
                return true;
            }

            signal = null;
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
    }
}
