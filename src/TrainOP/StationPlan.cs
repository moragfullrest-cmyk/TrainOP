using System;
using System.Threading;
using System.Threading.Tasks;

namespace TrainOP
{
    /// <summary>
    /// Describes one station or service-station hop attached to a route and how it is invoked.
    /// </summary>
    internal sealed class StationPlan
    {
        /// <summary>
        /// Creates a plan for a synchronous signal-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, Signal> station)
        {
            StationName = stationName;
            Station = station;
        }

        /// <summary>
        /// Creates a plan for an asynchronous signal-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<Signal>> asyncStation)
        {
            StationName = stationName;
            AsyncStation = asyncStation;
        }

        /// <summary>
        /// Creates a plan for a synchronous manifest-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CargoManifest> throughStation)
        {
            StationName = stationName;
            ThroughStation = throughStation;
        }

        /// <summary>
        /// Creates a plan for an asynchronous manifest-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<CargoManifest>> throughAsyncStation)
        {
            StationName = stationName;
            ThroughAsyncStation = throughAsyncStation;
        }

        /// <summary>
        /// Creates a plan for a synchronous signal-returning station with cancellation support.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Signal> stationWithToken)
        {
            StationName = stationName;
            StationWithToken = stationWithToken;
        }

        /// <summary>
        /// Creates a plan for a synchronous manifest-returning station with cancellation support.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, CargoManifest> throughStationWithToken)
        {
            StationName = stationName;
            ThroughStationWithToken = throughStationWithToken;
        }

        /// <summary>
        /// Creates a plan for a service-station hop that runs only after a red signal.
        /// </summary>
        public StationPlan(ServiceStationPlan servicePlan)
        {
            if (servicePlan == null)
            {
                throw new ArgumentNullException(nameof(servicePlan));
            }

            StationName = servicePlan.StationName;
            ServicePlan = servicePlan;
        }

        /// <summary>
        /// Gets the station name.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Gets whether this hop is a service station (entered only after a red signal).
        /// </summary>
        public bool IsServiceStation => ServicePlan != null;

        /// <summary>
        /// Gets the service-station plan when this hop is recovery, otherwise null.
        /// </summary>
        public ServiceStationPlan ServicePlan { get; }

        /// <summary>
        /// Gets the synchronous signal-returning handler, if configured.
        /// </summary>
        public Func<CargoManifest, Signal> Station { get; }

        /// <summary>
        /// Gets the synchronous signal-returning handler with cancellation support, if configured.
        /// </summary>
        public Func<CargoManifest, CancellationToken, Signal> StationWithToken { get; }

        /// <summary>
        /// Gets the synchronous manifest-returning handler, if configured.
        /// </summary>
        public Func<CargoManifest, CargoManifest> ThroughStation { get; }

        /// <summary>
        /// Gets the synchronous manifest-returning handler with cancellation support, if configured.
        /// </summary>
        public Func<CargoManifest, CancellationToken, CargoManifest> ThroughStationWithToken { get; }

        /// <summary>
        /// Gets the asynchronous signal-returning handler, if configured.
        /// </summary>
        public Func<CargoManifest, CancellationToken, Task<Signal>> AsyncStation { get; }

        /// <summary>
        /// Gets the asynchronous manifest-returning handler, if configured.
        /// </summary>
        public Func<CargoManifest, CancellationToken, Task<CargoManifest>> ThroughAsyncStation { get; }

        /// <summary>
        /// Gets whether this hop requires asynchronous execution.
        /// </summary>
        public bool IsAsync =>
            AsyncStation != null
            || ThroughAsyncStation != null
            || (ServicePlan != null && ServicePlan.AsyncHandler != null);
    }
}
