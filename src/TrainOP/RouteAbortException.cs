using System;

namespace TrainOP
{
    /// <summary>
    /// Raised when a service station throws during travel and the route cannot continue.
    /// The report is the state at the break: completed visits, the manifest as it was,
    /// and the red signal the service station was entered with.
    /// </summary>
    public sealed class RouteAbortException : Exception
    {
        /// <summary>
        /// Creates an abort for the service station that threw <paramref name="innerException"/>.
        /// </summary>
        public RouteAbortException(string stationName, RouteReport report, Exception innerException)
            : base(BuildMessage(stationName), innerException)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (innerException == null)
            {
                throw new ArgumentNullException(nameof(innerException));
            }

            StationName = stationName;
            Report = report ?? throw new ArgumentNullException(nameof(report));
        }

        /// <summary>
        /// Gets the service station that aborted the route.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Gets the route report at the moment the service station threw.
        /// </summary>
        public RouteReport Report { get; }

        private static string BuildMessage(string stationName)
        {
            return $"Service station '{stationName}' aborted the route.";
        }
    }
}
