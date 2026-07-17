using System;
using System.Threading;
using System.Threading.Tasks;

namespace TrainOP
{
    /// <summary>
    /// Describes a service station that handles red signals.
    /// </summary>
    internal sealed class ServiceStationPlan
    {
        /// <summary>
        /// Creates a plan for a synchronous service station handler.
        /// </summary>
        public ServiceStationPlan(string stationName, Func<RedSignal, CancellationToken, Signal> syncHandler)
        {
            StationName = stationName;
            SyncHandler = syncHandler;
        }

        /// <summary>
        /// Creates a plan for an asynchronous service station handler.
        /// </summary>
        public ServiceStationPlan(string stationName, Func<RedSignal, CancellationToken, Task<Signal>> asyncHandler)
        {
            StationName = stationName;
            AsyncHandler = asyncHandler;
        }

        /// <summary>
        /// Gets the service station name.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Gets the synchronous red-signal handler, if configured.
        /// </summary>
        public Func<RedSignal, CancellationToken, Signal> SyncHandler { get; }

        /// <summary>
        /// Gets the asynchronous red-signal handler, if configured.
        /// </summary>
        public Func<RedSignal, CancellationToken, Task<Signal>> AsyncHandler { get; }
    }
}
