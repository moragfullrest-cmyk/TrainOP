using System;
using System.Collections.Generic;

namespace TrainOP
{
    /// <summary>
    /// Final execution report for a train route.
    /// </summary>
    public sealed class RouteReport
    {
        /// <summary>
        /// Creates a route report.
        /// </summary>
        public RouteReport(IReadOnlyList<StationVisit> visits, Signal terminalSignal, CargoManifest manifest)
        {
            Visits = visits ?? throw new ArgumentNullException(nameof(visits));
            TerminalSignal = terminalSignal ?? throw new ArgumentNullException(nameof(terminalSignal));
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            Manifest = new ReadOnlyManifest(manifest);
        }

        /// <summary>
        /// Gets the list of executed station visits.
        /// </summary>
        public IReadOnlyList<StationVisit> Visits { get; }

        /// <summary>
        /// Gets the final signal that ended route execution (control only).
        /// </summary>
        public Signal TerminalSignal { get; }

        /// <summary>
        /// Gets the terminal wagon snapshot for this run. Keys cannot be loaded or unloaded here.
        /// </summary>
        public ReadOnlyManifest Manifest { get; }

        /// <summary>
        /// Gets whether the route reached its destination with a green signal.
        /// </summary>
        public bool ReachedDestination => TerminalSignal.IsGreen;

        /// <summary>
        /// Gets the failure code when the route stopped with a red signal; otherwise null.
        /// </summary>
        public string FailureCode =>
            TerminalSignal is RedSignal red ? red.Issue.Code : null;

        /// <summary>
        /// Gets the failure message when the route stopped with a red signal; otherwise null.
        /// </summary>
        public string FailureMessage =>
            TerminalSignal is RedSignal red ? red.Issue.Message : null;

        /// <summary>
        /// Gets the issues of the terminal red signal, in stop order; otherwise empty.
        /// </summary>
        public IReadOnlyList<SignalIssue> FailureIssues =>
            TerminalSignal is RedSignal red ? red.Issues : Array.Empty<SignalIssue>();

        /// <summary>
        /// Gets a terminal wagon value by name from the report manifest.
        /// Throws when the wagon name is empty or missing.
        /// </summary>
        public object this[string wagonName]
        {
            get
            {
                return Get<object>(wagonName);
            }
        }

        /// <summary>
        /// Gets a typed terminal wagon value by name from the report manifest.
        /// Throws when the wagon name is empty, missing, or has incompatible type.
        /// </summary>
        public T Get<T>(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            if (!Manifest.TryGetWagon(wagonName, out var value))
            {
                throw new KeyNotFoundException($"Wagon '{wagonName}' was not found in the terminal report.");
            }

            return CargoManifest.CastWagonValue<T>(wagonName, value);
        }

        /// <summary>
        /// Tries to read a typed terminal wagon. Returns false when the name is missing.
        /// An empty name throws. A present value of the wrong type throws <see cref="InvalidCastException"/>.
        /// </summary>
        public bool TryGet<T>(string wagonName, out T value)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            if (!Manifest.TryGetWagon(wagonName, out var cargo))
            {
                value = default;
                return false;
            }

            value = CargoManifest.CastWagonValue<T>(wagonName, cargo);
            return true;
        }
    }
}
