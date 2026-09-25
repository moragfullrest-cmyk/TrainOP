using System;
using System.Threading;
using System.Threading.Tasks;

namespace TrainOP
{
    /// <summary>
    /// Which single handler a <see cref="StationPlan"/> invokes. Set once at registration.
    /// </summary>
    internal enum StationInvokeKind
    {
        /// <summary>Synchronous signal-returning station.</summary>
        Signal,

        /// <summary>Synchronous signal-returning station with a cancellation token.</summary>
        SignalWithToken,

        /// <summary>Synchronous manifest-returning station.</summary>
        Through,

        /// <summary>Synchronous manifest-returning station with a cancellation token.</summary>
        ThroughWithToken,

        /// <summary>Asynchronous signal-returning station.</summary>
        SignalAsync,

        /// <summary>Asynchronous manifest-returning station.</summary>
        ThroughAsync,

        /// <summary>Service station entered only after a red signal.</summary>
        Service,
    }

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
            Kind = StationInvokeKind.Signal;
            Handler = station;
        }

        /// <summary>
        /// Creates a plan for an asynchronous signal-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<Signal>> asyncStation)
        {
            StationName = stationName;
            Kind = StationInvokeKind.SignalAsync;
            Handler = asyncStation;
        }

        /// <summary>
        /// Creates a plan for a synchronous manifest-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CargoManifest> throughStation)
        {
            StationName = stationName;
            Kind = StationInvokeKind.Through;
            Handler = throughStation;
        }

        /// <summary>
        /// Creates a plan for an asynchronous manifest-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<CargoManifest>> throughAsyncStation)
        {
            StationName = stationName;
            Kind = StationInvokeKind.ThroughAsync;
            Handler = throughAsyncStation;
        }

        /// <summary>
        /// Creates a plan for a synchronous signal-returning station with cancellation support.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Signal> stationWithToken)
        {
            StationName = stationName;
            Kind = StationInvokeKind.SignalWithToken;
            Handler = stationWithToken;
        }

        /// <summary>
        /// Creates a plan for a synchronous manifest-returning station with cancellation support.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, CargoManifest> throughStationWithToken)
        {
            StationName = stationName;
            Kind = StationInvokeKind.ThroughWithToken;
            Handler = throughStationWithToken;
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
            Kind = StationInvokeKind.Service;
            ServicePlan = servicePlan;
        }

        /// <summary>
        /// Gets the station name.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Gets the invoke kind chosen at registration.
        /// </summary>
        public StationInvokeKind Kind { get; }

        /// <summary>
        /// Gets the single station handler for this hop, or null for a service station.
        /// </summary>
        public Delegate Handler { get; }

        /// <summary>
        /// Gets whether this hop is a service station (entered only after a red signal).
        /// </summary>
        public bool IsServiceStation => Kind == StationInvokeKind.Service;

        /// <summary>
        /// Gets the service-station plan when this hop is recovery, otherwise null.
        /// </summary>
        public ServiceStationPlan ServicePlan { get; }

        /// <summary>
        /// Gets whether this hop requires asynchronous execution.
        /// </summary>
        public bool IsAsync =>
            Kind == StationInvokeKind.SignalAsync
            || Kind == StationInvokeKind.ThroughAsync
            || (ServicePlan != null && ServicePlan.AsyncHandler != null);
    }
}
