namespace TrainOP
{
    /// <summary>
    /// Converts station handler return values into route signals for generated adapters.
    /// Special returns (RedFailure / WhitePass / Signal / payload / CargoManifest) are classified once.
    /// Cargo is written into the run manifest; signals carry control only.
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
            return ToSignal(
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
        /// Converts a station return value to a signal with optional default-ItemN allocation.
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
            if (TryConvertPassthroughSignal(stationReturn, stationName, out var passthrough))
            {
                return passthrough;
            }

            stationReturn = WagonStationReturn.UnwrapGreenPayloadReturn(stationReturn);

            if (stationReturn is CargoManifest replacement)
            {
                manifest.ReplaceWith(replacement);
                return RailwaySignals.Green();
            }

            var merged = StationMerge.Apply(
                manifest,
                stationReturn,
                wagonNames,
                removeOmittedRegularInputs,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                allocateDefaultItemNElements);
            if (!ReferenceEquals(merged, manifest))
            {
                manifest.ReplaceWith(merged);
            }

            return RailwaySignals.Green();
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
            return ToServiceSignal(
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
            if (TryConvertPassthroughSignal(stationReturn, stationName, out var passthrough))
            {
                return passthrough;
            }

            var merged = StationMerge.ApplyOverlay(
                manifest,
                stationReturn,
                wagonNames,
                returnMemberNames,
                byReferenceWagons,
                refLocalValues,
                allocateDefaultItemNElements);
            if (!ReferenceEquals(merged, manifest))
            {
                manifest.ReplaceWith(merged);
            }

            return RailwaySignals.Green();
        }

        /// <summary>
        /// Handles returns that are already route-facing signals (or RedFailure / WhitePass requests).
        /// Shared by Station and ServiceStation conversion paths.
        /// </summary>
        private static bool TryConvertPassthroughSignal(
            object stationReturn,
            string stationName,
            out Signal signal)
        {
            if (stationReturn is RedFailure fail)
            {
                signal = MapRedFailure(fail, stationName);
                return true;
            }

            if (stationReturn is WhitePass)
            {
                signal = RailwaySignals.Green();
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
        /// Maps a data-oriented red failure request to a route red signal.
        /// </summary>
        private static Signal MapRedFailure(RedFailure fail, string stationName)
        {
            var issue = new SignalIssue(fail.Code, fail.Message, stationName);
            return RailwaySignals.Red(issue, fail.PriorIssues);
        }

    }
}
