using System;
using System.Collections.Generic;
using System.ComponentModel;
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
            : this(stationName, StationInvokeKind.Signal, station)
        {
        }

        /// <summary>
        /// Creates a plan for an asynchronous signal-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<Signal>> asyncStation)
            : this(stationName, StationInvokeKind.SignalAsync, asyncStation)
        {
        }

        /// <summary>
        /// Creates a plan for a synchronous manifest-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CargoManifest> throughStation)
            : this(stationName, StationInvokeKind.Through, throughStation)
        {
        }

        /// <summary>
        /// Creates a plan for an asynchronous manifest-returning station.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<CargoManifest>> throughAsyncStation)
            : this(stationName, StationInvokeKind.ThroughAsync, throughAsyncStation)
        {
        }

        /// <summary>
        /// Creates a plan for a synchronous signal-returning station with cancellation support.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Signal> stationWithToken)
            : this(stationName, StationInvokeKind.SignalWithToken, stationWithToken)
        {
        }

        /// <summary>
        /// Creates a plan for a synchronous manifest-returning station with cancellation support.
        /// </summary>
        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, CargoManifest> throughStationWithToken)
            : this(stationName, StationInvokeKind.ThroughWithToken, throughStationWithToken)
        {
        }

        private StationPlan(string stationName, StationInvokeKind kind, Delegate handler)
        {
            StationName = stationName;
            Kind = kind;
            Handler = handler;
        }

        /// <summary>
        /// Creates a plan for a synchronous service-station hop that runs only after a red signal.
        /// </summary>
        public StationPlan(
            string stationName,
            Func<RedSignal, CargoManifest, IReadOnlyList<StationVisit>, CancellationToken, Signal> syncHandler)
            : this(stationName)
        {
            if (syncHandler == null)
            {
                throw new ArgumentNullException(nameof(syncHandler));
            }

            SyncHandler = syncHandler;
        }

        /// <summary>
        /// Creates a plan for an asynchronous service-station hop that runs only after a red signal.
        /// </summary>
        public StationPlan(
            string stationName,
            Func<RedSignal, CargoManifest, IReadOnlyList<StationVisit>, CancellationToken, Task<Signal>> asyncHandler)
            : this(stationName)
        {
            if (asyncHandler == null)
            {
                throw new ArgumentNullException(nameof(asyncHandler));
            }

            AsyncHandler = asyncHandler;
        }

        private StationPlan(string stationName)
        {
            StationName = stationName;
            Kind = StationInvokeKind.Service;
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
        /// Gets the synchronous service-station handler, or null when this hop is not one.
        /// </summary>
        public Func<RedSignal, CargoManifest, IReadOnlyList<StationVisit>, CancellationToken, Signal> SyncHandler { get; }

        /// <summary>
        /// Gets the asynchronous service-station handler, or null when this hop is not one.
        /// </summary>
        public Func<RedSignal, CargoManifest, IReadOnlyList<StationVisit>, CancellationToken, Task<Signal>> AsyncHandler { get; }

        /// <summary>
        /// Runs this hop and the following <see cref="SegmentLength"/> - 1 hops as one pure segment.
        /// Null when the hop is ordinary.
        /// </summary>
        internal Func<CargoManifest, CancellationToken, SegmentVisitLog, int, Signal> SegmentRunner { get; set; }

        /// <summary>
        /// How many plan entries this segment covers, including this hop. One when there is no segment.
        /// </summary>
        internal int SegmentLength { get; set; } = 1;

        /// <summary>
        /// Gets whether this hop requires asynchronous execution.
        /// </summary>
        public bool IsAsync =>
            Kind == StationInvokeKind.SignalAsync
            || Kind == StationInvokeKind.ThroughAsync
            || AsyncHandler != null;
    }

    /// <summary>
    /// Visit journal handed to a pure segment. Generated code calls <see cref="Record"/>
    /// and does not name <c>List&lt;T&gt;</c>, which lives in <c>System.Collections</c>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class SegmentVisitLog
    {
        private readonly List<StationVisit> _visits;

        internal SegmentVisitLog(List<StationVisit> visits)
        {
            _visits = visits;
        }

        /// <summary>
        /// Appends one visit when the journal is enabled.
        /// </summary>
        public void Record(string stationName, HopOutcome outcome, int index, TimeSpan elapsed)
        {
            if (_visits == null)
            {
                return;
            }

            _visits.Add(new StationVisit(stationName, outcome, index, elapsed));
        }
    }
}
