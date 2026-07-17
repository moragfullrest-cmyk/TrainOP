namespace TrainOP
{
    /// <summary>
    /// Holds the manifest and optional terminal report after one station step.
    /// </summary>
    internal readonly struct StationStepResult
    {
        /// <summary>
        /// Creates a station step result.
        /// </summary>
        public StationStepResult(CargoManifest current, RouteReport terminalReport)
        {
            Current = current;
            TerminalReport = terminalReport;
        }

        /// <summary>
        /// Gets the manifest after the station step.
        /// </summary>
        public CargoManifest Current { get; }

        /// <summary>
        /// Gets the terminal report when the route stops early, or null to continue.
        /// </summary>
        public RouteReport TerminalReport { get; }
    }
}
