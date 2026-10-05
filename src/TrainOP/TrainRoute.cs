using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TrainOP
{
    /// <summary>
    /// Builder for a route made of stations.
    /// Use generated <see cref="Station"/> extensions for data-oriented handlers.
    /// Unsealed so a chain can declare its own descendant and hide <see cref="Travel()"/>
    /// with a chain-specific tuple. Forward <c>[CallerFilePath]</c>, <c>[CallerLineNumber]</c>,
    /// and <c>[CallerMemberName]</c> into this constructor so chain-dispatch matches <c>new</c>.
    /// </summary>
    public class TrainRoute
    {
        private readonly List<StationPlan> _route = new List<StationPlan>();
        private readonly string _callerChainKey;
        private int _chainRegistrationOrdinal;

        private static string BuildCallerChainKey(string filePath, int lineNumber, string memberName)
        {
            return CallerChainKeyFormat.Build(filePath, lineNumber, memberName);
        }

        /// <summary>
        /// Internal ctor stamping caller identity for chain-dispatch in caller mode.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute(
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = null)
        {
            _callerChainKey = BuildCallerChainKey(filePath, lineNumber, memberName);
        }

        /// <summary>
        /// Caller identity key used by generated chain-dispatch adapters in caller mode.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string CallerChainKey => _callerChainKey;

        /// <summary>
        /// Attaches a pure segment that starts at <paramref name="startIndex"/> and covers
        /// <paramref name="length"/> plans already registered on this route.
        /// The interpreter runs it instead of those hops, writes the manifest at the segment boundary,
        /// and records one visit per step.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute AttachSegment(
            int startIndex,
            int length,
            Func<CargoManifest, CancellationToken, SegmentVisitLog, int, Signal> runner)
        {
            if (runner == null)
            {
                throw new ArgumentNullException(nameof(runner));
            }

            if (startIndex < 0
                || length < 2
                || startIndex + length > _route.Count
                || _route[startIndex].IsServiceStation
                || _route[startIndex].IsAsync)
            {
                return this;
            }

            _route[startIndex].SegmentRunner = runner;
            _route[startIndex].SegmentLength = length;
            return this;
        }

        /// <summary>
        /// Returns the next chain registration ordinal (used as chainStationIndex).
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int NextChainRegistrationOrdinal()
        {
            return _chainRegistrationOrdinal++;
        }

        /// <summary>
        /// Registers a synchronous station that returns an updated manifest.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, CargoManifest> throughStation)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (throughStation == null)
            {
                throw new ArgumentNullException(nameof(throughStation));
            }

            AddPlan(new StationPlan(stationName, throughStation));
            return this;
        }

        /// <summary>
        /// Registers a synchronous station with cancellation support that returns an updated manifest.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, CancellationToken, CargoManifest> throughStationWithToken)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (throughStationWithToken == null)
            {
                throw new ArgumentNullException(nameof(throughStationWithToken));
            }

            AddPlan(new StationPlan(stationName, throughStationWithToken));
            return this;
        }

        /// <summary>
        /// Registers a synchronous station that returns a signal.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, Signal> station)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (station == null)
            {
                throw new ArgumentNullException(nameof(station));
            }

            AddPlan(new StationPlan(stationName, station));
            return this;
        }

        /// <summary>
        /// Registers a synchronous station with cancellation support that returns a signal.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, CancellationToken, Signal> stationWithToken)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (stationWithToken == null)
            {
                throw new ArgumentNullException(nameof(stationWithToken));
            }

            AddPlan(new StationPlan(stationName, stationWithToken));
            return this;
        }

        /// <summary>
        /// Registers an asynchronous station that returns an updated manifest.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, Task<CargoManifest>> throughAsyncStation)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (throughAsyncStation == null)
            {
                throw new ArgumentNullException(nameof(throughAsyncStation));
            }

            AddPlan(new StationPlan(stationName, (manifest, _) => throughAsyncStation(manifest)));
            return this;
        }

        /// <summary>
        /// Registers an asynchronous station with cancellation support that returns an updated manifest.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, CancellationToken, Task<CargoManifest>> throughAsyncStation)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (throughAsyncStation == null)
            {
                throw new ArgumentNullException(nameof(throughAsyncStation));
            }

            AddPlan(new StationPlan(stationName, throughAsyncStation));
            return this;
        }

        /// <summary>
        /// Registers an asynchronous station that returns a signal.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, Task<Signal>> asyncStation)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (asyncStation == null)
            {
                throw new ArgumentNullException(nameof(asyncStation));
            }

            AddPlan(new StationPlan(stationName, (manifest, _) => asyncStation(manifest)));
            return this;
        }

        /// <summary>
        /// Registers an asynchronous station with cancellation support that returns a signal.
        /// Reserved for source-generated adapters; do not call directly.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public TrainRoute RegisterStation(string stationName, Func<CargoManifest, CancellationToken, Task<Signal>> asyncStation)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (asyncStation == null)
            {
                throw new ArgumentNullException(nameof(asyncStation));
            }

            AddPlan(new StationPlan(stationName, asyncStation));
            return this;
        }

        /// <summary>
        /// Executes the route from an empty manifest.
        /// </summary>
        public RouteReport Travel()
        {
            return TravelCore(CancellationToken.None, recordVisits: true);
        }

        /// <summary>
        /// Executes the route from an empty manifest with cancellation support.
        /// </summary>
        public RouteReport Travel(CancellationToken cancellationToken)
        {
            return TravelCore(cancellationToken, recordVisits: true);
        }

        /// <summary>
        /// Executes the route without recording per-station visits.
        /// <see cref="RouteReport.Visits"/> is empty; terminal signal and manifest behave as in <see cref="Travel()"/>.
        /// </summary>
        public RouteReport TravelLight()
        {
            return TravelCore(CancellationToken.None, recordVisits: false);
        }

        /// <summary>
        /// Executes the route without recording per-station visits, with cancellation support.
        /// <see cref="RouteReport.Visits"/> is empty; terminal signal and manifest behave as in <see cref="Travel(CancellationToken)"/>.
        /// </summary>
        public RouteReport TravelLight(CancellationToken cancellationToken)
        {
            return TravelCore(cancellationToken, recordVisits: false);
        }

        /// <summary>
        /// Asynchronously executes the route from an empty manifest.
        /// </summary>
        public Task<RouteReport> TravelAsync()
        {
            return TravelCoreAsync(CancellationToken.None, recordVisits: true);
        }

        /// <summary>
        /// Asynchronously executes the route from an empty manifest with cancellation support.
        /// </summary>
        public Task<RouteReport> TravelAsync(CancellationToken cancellationToken)
        {
            return TravelCoreAsync(cancellationToken, recordVisits: true);
        }

        /// <summary>
        /// Asynchronously executes the route without recording per-station visits.
        /// <see cref="RouteReport.Visits"/> is empty; terminal signal and manifest behave as in <see cref="TravelAsync()"/>.
        /// </summary>
        public Task<RouteReport> TravelLightAsync()
        {
            return TravelCoreAsync(CancellationToken.None, recordVisits: false);
        }

        /// <summary>
        /// Asynchronously executes the route without recording per-station visits, with cancellation support.
        /// <see cref="RouteReport.Visits"/> is empty; terminal signal and manifest behave as in <see cref="TravelAsync(CancellationToken)"/>.
        /// </summary>
        public Task<RouteReport> TravelLightAsync(CancellationToken cancellationToken)
        {
            return TravelCoreAsync(cancellationToken, recordVisits: false);
        }

        /// <summary>
        /// Registers a service-station hop in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, Signal> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, _, __) => handler(red));
        }

        /// <summary>
        /// Registers a service-station hop with cancellation support in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, CancellationToken, Signal> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, _, token) => handler(red, token));
        }

        /// <summary>
        /// Registers a service-station hop in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, CargoManifest, Signal> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, manifest, _) => handler(red, manifest));
        }

        /// <summary>
        /// Registers a service-station hop with cancellation support in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(
            string stationName,
            Func<RedSignal, CargoManifest, CancellationToken, Signal> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(
                stationName,
                (RedSignal red, CargoManifest manifest, IReadOnlyList<StationVisit> _, CancellationToken token) =>
                    handler(red, manifest, token));
        }

        /// <summary>
        /// Registers a service-station hop with the visit journal and cancellation support in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// <paramref name="handler"/> receives visits already recorded before this hop.
        /// When the journal is disabled the list is empty. This hop is not in the list.
        /// </summary>
        public TrainRoute ServiceStation(
            string stationName,
            Func<RedSignal, CargoManifest, IReadOnlyList<StationVisit>, CancellationToken, Signal> handler)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            AddPlan(new StationPlan(new ServiceStationPlan(stationName, handler)));
            return this;
        }

        /// <summary>
        /// Registers an asynchronous service-station hop in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, Task<Signal>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, _, __) => handler(red));
        }

        /// <summary>
        /// Registers an asynchronous service-station hop with cancellation support in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, CancellationToken, Task<Signal>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, _, token) => handler(red, token));
        }

        /// <summary>
        /// Registers an asynchronous service-station hop in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, CargoManifest, Task<Signal>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, manifest, _) => handler(red, manifest));
        }

        /// <summary>
        /// Registers an asynchronous service-station hop with cancellation support in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// </summary>
        public TrainRoute ServiceStation(
            string stationName,
            Func<RedSignal, CargoManifest, CancellationToken, Task<Signal>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(
                stationName,
                (RedSignal red, CargoManifest manifest, IReadOnlyList<StationVisit> _, CancellationToken token) =>
                    handler(red, manifest, token));
        }

        /// <summary>
        /// Registers an asynchronous service-station hop with the visit journal and cancellation support in route order.
        /// Entered only when the previous hop left a red signal; skipped after green.
        /// <paramref name="handler"/> receives visits already recorded before this hop.
        /// When the journal is disabled the list is empty. This hop is not in the list.
        /// </summary>
        public TrainRoute ServiceStation(
            string stationName,
            Func<RedSignal, CargoManifest, IReadOnlyList<StationVisit>, CancellationToken, Task<Signal>> handler)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            AddPlan(new StationPlan(new ServiceStationPlan(stationName, handler)));
            return this;
        }

        private const string StationExceptionCode = "STATION_EXCEPTION";

        /// <summary>
        /// Executes all route hops synchronously and returns the final report.
        /// Regular stations run after green; service stations run after red.
        /// A bypassed hop is recorded as skipped when the journal is enabled.
        /// </summary>
        private RouteReport TravelCore(CancellationToken cancellationToken, bool recordVisits)
        {
            var current = new CargoManifest();
            var visits = recordVisits ? new List<StationVisit>(_route.Count) : null;
            Signal previous = RailwaySignals.Green();
            var canCancel = cancellationToken.CanBeCanceled;

            for (var i = 0; i < _route.Count; i++)
            {
                if (canCancel)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var plan = _route[i];
                if (TryRunSegment(plan, i, ref i, current, visits, cancellationToken, ref previous))
                {
                    continue;
                }

                if (!ShouldEnterHop(plan, previous))
                {
                    if (visits != null)
                    {
                        visits.Add(new StationVisit(plan.StationName, HopOutcome.Skipped, i, TimeSpan.Zero));
                    }

                    continue;
                }

                var started = Stopwatch.GetTimestamp();
                var signal = plan.IsServiceStation
                    ? ExecuteServiceStation(plan, (RedSignal)previous, ref current, visits, cancellationToken)
                    : ExecuteStation(plan, ref current, cancellationToken);
                previous = RecordHop(signal, plan.StationName, i, StationVisit.ElapsedSince(started), visits);
            }

            return new RouteReport(
                visits ?? (IReadOnlyList<StationVisit>)Array.Empty<StationVisit>(),
                previous.IsGreen ? RailwaySignals.Green() : previous,
                current);
        }

        /// <summary>
        /// Executes all route hops asynchronously and returns the final report.
        /// Regular stations run after green; service stations run after red.
        /// A bypassed hop is recorded as skipped when the journal is enabled.
        /// </summary>
        private async Task<RouteReport> TravelCoreAsync(CancellationToken cancellationToken, bool recordVisits)
        {
            var manifest = new ManifestHolder(new CargoManifest());
            var visits = recordVisits ? new List<StationVisit>(_route.Count) : null;
            Signal previous = RailwaySignals.Green();
            var canCancel = cancellationToken.CanBeCanceled;

            for (var i = 0; i < _route.Count; i++)
            {
                if (canCancel)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var plan = _route[i];
                if (TryRunSegment(plan, i, ref i, manifest.Current, visits, cancellationToken, ref previous))
                {
                    continue;
                }

                if (!ShouldEnterHop(plan, previous))
                {
                    if (visits != null)
                    {
                        visits.Add(new StationVisit(plan.StationName, HopOutcome.Skipped, i, TimeSpan.Zero));
                    }

                    continue;
                }

                var started = Stopwatch.GetTimestamp();
                var signal = plan.IsServiceStation
                    ? await ExecuteServiceStationAsync(
                        plan,
                        (RedSignal)previous,
                        manifest,
                        visits,
                        cancellationToken).ConfigureAwait(false)
                    : await ExecuteStationAsync(plan, manifest, cancellationToken).ConfigureAwait(false);
                previous = RecordHop(signal, plan.StationName, i, StationVisit.ElapsedSince(started), visits);
            }

            return new RouteReport(
                visits ?? (IReadOnlyList<StationVisit>)Array.Empty<StationVisit>(),
                previous.IsGreen ? RailwaySignals.Green() : previous,
                manifest.Current);
        }

        /// <summary>
        /// Holds the run manifest so async helpers can replace the reference without ref parameters.
        /// </summary>
        private sealed class ManifestHolder
        {
            public ManifestHolder(CargoManifest current)
            {
                Current = current ?? throw new ArgumentNullException(nameof(current));
            }

            public CargoManifest Current { get; set; }
        }

        /// <summary>
        /// Appends a hop to the plan.
        /// </summary>
        private void AddPlan(StationPlan plan)
        {
            _route.Add(plan);
        }

        /// <summary>
        /// Runs a pure segment stored on the first hop and advances the interpreter past it.
        /// A skipped entry stays a normal hop so each later plan is recorded on its own.
        /// </summary>
        private static bool TryRunSegment(
            StationPlan plan,
            int index,
            ref int i,
            CargoManifest manifest,
            List<StationVisit> visits,
            CancellationToken cancellationToken,
            ref Signal previous)
        {
            if (plan.SegmentRunner == null || plan.SegmentLength < 2 || !ShouldEnterHop(plan, previous))
            {
                return false;
            }

            var log = visits == null ? null : new SegmentVisitLog(visits);
            previous = plan.SegmentRunner(manifest, cancellationToken, log, index);
            i += plan.SegmentLength - 1;
            return true;
        }

        /// <summary>
        /// Regular hop after green; service hop after red; otherwise skip.
        /// </summary>
        private static bool ShouldEnterHop(StationPlan plan, Signal previous)
        {
            return plan.IsServiceStation ? !previous.IsGreen : previous.IsGreen;
        }

        /// <summary>
        /// Invokes one synchronous station plan and returns its signal.
        /// </summary>
        private static Signal ExecuteStation(
            StationPlan plan,
            ref CargoManifest current,
            CancellationToken cancellationToken)
        {
            if (plan.IsAsync)
            {
                throw new InvalidOperationException(
                    $"Route contains async station '{plan.StationName}'. Use TravelAsync instead of Travel.");
            }

            try
            {
                return ExecuteSyncStationHandlers(plan, ref current, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapStationException(plan, exception);
            }
        }

        /// <summary>
        /// Invokes one station plan and returns its signal.
        /// </summary>
        private static async Task<Signal> ExecuteStationAsync(
            StationPlan plan,
            ManifestHolder manifest,
            CancellationToken cancellationToken)
        {
            try
            {
                if (plan.Kind == StationInvokeKind.ThroughAsync)
                {
                    var nextManifest = await ((Func<CargoManifest, CancellationToken, Task<CargoManifest>>)plan.Handler)(
                            manifest.Current,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (nextManifest != null)
                    {
                        manifest.Current = nextManifest;
                    }

                    return RailwaySignals.Green();
                }

                if (plan.Kind == StationInvokeKind.SignalAsync)
                {
                    return await ((Func<CargoManifest, CancellationToken, Task<Signal>>)plan.Handler)(
                            manifest.Current,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                var current = manifest.Current;
                var signal = ExecuteSyncStationHandlers(plan, ref current, cancellationToken);
                manifest.Current = current;
                return signal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapStationException(plan, exception);
            }
        }

        /// <summary>
        /// Shared sync Through*/Station* dispatch used by Travel and the sync fallback of TravelAsync.
        /// </summary>
        private static Signal ExecuteSyncStationHandlers(
            StationPlan plan,
            ref CargoManifest current,
            CancellationToken cancellationToken)
        {
            switch (plan.Kind)
            {
                case StationInvokeKind.ThroughWithToken:
                {
                    var nextManifest = ((Func<CargoManifest, CancellationToken, CargoManifest>)plan.Handler)(
                        current,
                        cancellationToken);
                    if (nextManifest != null)
                    {
                        current = nextManifest;
                    }

                    return RailwaySignals.Green();
                }

                case StationInvokeKind.Through:
                {
                    var nextManifest = ((Func<CargoManifest, CargoManifest>)plan.Handler)(current);
                    if (nextManifest != null)
                    {
                        current = nextManifest;
                    }

                    return RailwaySignals.Green();
                }

                case StationInvokeKind.SignalWithToken:
                    return ((Func<CargoManifest, CancellationToken, Signal>)plan.Handler)(current, cancellationToken);

                case StationInvokeKind.Signal:
                    return ((Func<CargoManifest, Signal>)plan.Handler)(current);

                default:
                    throw new InvalidOperationException(
                        $"Station '{plan.StationName}' has invoke kind '{plan.Kind}', which is not a synchronous station.");
            }
        }

        /// <summary>
        /// Copies visits recorded before the current hop.
        /// A disabled journal yields an empty list, so a service station does not receive null.
        /// </summary>
        private static IReadOnlyList<StationVisit> JournalSoFar(List<StationVisit> visits)
        {
            if (visits == null || visits.Count == 0)
            {
                return Array.Empty<StationVisit>();
            }

            var copy = new StationVisit[visits.Count];
            for (var i = 0; i < visits.Count; i++)
            {
                copy[i] = visits[i];
            }

            return copy;
        }

        /// <summary>
        /// Invokes one synchronous service-station hop for the current red signal.
        /// </summary>
        private static Signal ExecuteServiceStation(
            StationPlan plan,
            RedSignal redSignal,
            ref CargoManifest current,
            List<StationVisit> visits,
            CancellationToken cancellationToken)
        {
            var servicePlan = plan.ServicePlan;
            if (servicePlan.AsyncHandler != null)
            {
                throw new InvalidOperationException(
                    $"Route contains async service station '{plan.StationName}'. Use TravelAsync instead of Travel.");
            }

            try
            {
                return servicePlan.SyncHandler(redSignal, current, JournalSoFar(visits), cancellationToken) ?? redSignal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw AbortRoute(plan.StationName, redSignal, current, visits, exception);
            }
        }

        /// <summary>
        /// Invokes one service-station hop for the current red signal.
        /// </summary>
        private static async Task<Signal> ExecuteServiceStationAsync(
            StationPlan plan,
            RedSignal redSignal,
            ManifestHolder manifest,
            List<StationVisit> visits,
            CancellationToken cancellationToken)
        {
            var servicePlan = plan.ServicePlan;
            try
            {
                Signal handled;
                if (servicePlan.AsyncHandler != null)
                {
                    handled = await servicePlan.AsyncHandler(
                            redSignal,
                            manifest.Current,
                            JournalSoFar(visits),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    handled = servicePlan.SyncHandler(
                        redSignal,
                        manifest.Current,
                        JournalSoFar(visits),
                        cancellationToken);
                }

                return handled ?? redSignal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw AbortRoute(plan.StationName, redSignal, manifest.Current, visits, exception);
            }
        }

        /// <summary>
        /// Stops travel because a service station threw. The throwing hop is not recorded.
        /// </summary>
        private static RouteAbortException AbortRoute(
            string stationName,
            RedSignal enteredWith,
            CargoManifest manifest,
            List<StationVisit> visits,
            Exception exception)
        {
            var report = new RouteReport(
                visits ?? (IReadOnlyList<StationVisit>)Array.Empty<StationVisit>(),
                enteredWith,
                manifest);
            return new RouteAbortException(stationName, report, exception);
        }

        /// <summary>
        /// Records the hop outcome, then normalizes the signal that becomes "previous" for the next hop.
        /// Lunar white stays <see cref="HopOutcome.White"/> in the journal and continues the route as green.
        /// </summary>
        private static Signal RecordHop(
            Signal signal,
            string stationName,
            int index,
            TimeSpan elapsed,
            List<StationVisit> visits)
        {
            EnsureStationSignal(signal, stationName);
            var outcome = signal is WhitePass
                ? HopOutcome.White
                : signal.IsGreen
                    ? HopOutcome.Green
                    : HopOutcome.Red;
            signal = NormalizeRequestSignal(signal, stationName);
            if (visits != null)
            {
                visits.Add(new StationVisit(stationName, outcome, index, elapsed));
            }

            if (!signal.IsGreen && !(signal is RedSignal))
            {
                throw new InvalidOperationException(
                    $"Station '{stationName}' returned unsupported non-green signal type '{signal.GetType().FullName}'.");
            }

            return signal;
        }

        /// <summary>
        /// Maps request-style signals (<see cref="WhitePass"/> / <see cref="RedFailure"/>) onto route signals.
        /// Rejects unknown non-green signal subtypes.
        /// </summary>
        private static Signal NormalizeRequestSignal(Signal signal, string stationName)
        {
            if (ReferenceEquals(signal, GreenSignal.Instance))
            {
                return signal;
            }

            if (signal is WhitePass)
            {
                return RailwaySignals.Green();
            }

            if (signal is RedFailure failure)
            {
                return failure.ToRedSignal(stationName);
            }

            if (signal is GreenSignal || signal is RedSignal)
            {
                return signal;
            }

            if (!signal.IsGreen)
            {
                throw new InvalidOperationException(
                    $"Station '{stationName}' returned unsupported non-green signal type '{signal.GetType().FullName}'.");
            }

            return signal;
        }

        /// <summary>
        /// Validates that a station returned a non-null signal.
        /// </summary>
        private static void EnsureStationSignal(Signal signal, string stationName)
        {
            if (signal == null)
            {
                throw new InvalidOperationException($"Station '{stationName}' returned null signal.");
            }
        }

        /// <summary>
        /// Converts an unhandled station exception into a red signal.
        /// </summary>
        private static Signal WrapStationException(StationPlan plan, Exception exception)
        {
            var issue = new SignalIssue(
                StationExceptionCode,
                $"Unhandled station exception: {exception.Message}",
                plan.StationName,
                exception);
            return RailwaySignals.Red(issue);
        }

    }
}
