using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TrainOP
{
    /// <summary>
    /// Mutable shared storage for wagon values between route stations.
    /// </summary>
    public sealed class CargoManifest
    {
        private readonly Dictionary<string, object> _wagons;

        /// <summary>
        /// Creates an empty cargo manifest.
        /// </summary>
        public CargoManifest()
        {
            _wagons = new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Checks whether a wagon with the specified name exists.
        /// </summary>
        public bool HasWagon(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            return _wagons.ContainsKey(wagonName);
        }

        /// <summary>
        /// Tries to read a wagon value by name without throwing when the wagon is missing.
        /// </summary>
        public bool TryGetWagon(string wagonName, out object cargo)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            return _wagons.TryGetValue(wagonName, out cargo);
        }

        /// <summary>
        /// Reads a typed wagon value by name.
        /// </summary>
        public T PullWagon<T>(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            if (!_wagons.TryGetValue(wagonName, out var value))
            {
                throw new KeyNotFoundException("Wagon '" + wagonName + "' was not found in the manifest.");
            }

            if (!(value is T typed))
            {
                throw new InvalidCastException(
                    "Wagon '" + wagonName + "' contains '" + value.GetType().FullName + "', cannot cast to '" + typeof(T).FullName + "'.");
            }

            return typed;
        }

        /// <summary>
        /// Adds or replaces a wagon value in place and returns this manifest.
        /// </summary>
        public CargoManifest LoadWagon(string wagonName, object cargo)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            _wagons[wagonName] = cargo;
            return this;
        }

        /// <summary>
        /// Removes a wagon by name in place and returns this manifest.
        /// </summary>
        public CargoManifest UnloadWagon(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            _wagons.Remove(wagonName);
            return this;
        }

        /// <summary>
        /// Returns a read-only view of current wagon values (live; not a copy).
        /// </summary>
        public IReadOnlyDictionary<string, object> InspectWagons()
        {
            return _wagons;
        }
    }

    /// <summary>
    /// Describes a red signal reason.
    /// </summary>
    public sealed class SignalIssue
    {
        /// <summary>
        /// Creates a signal issue.
        /// </summary>
        public SignalIssue(string code, string message, string stationName, Exception exception = null)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Issue code cannot be empty.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Issue message cannot be empty.", nameof(message));
            }

            Code = code;
            Message = message;
            StationName = stationName ?? string.Empty;
            Exception = exception;
        }

        /// <summary>
        /// Gets the issue code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the issue message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the station name where the issue occurred.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Gets the optional exception that caused the issue.
        /// </summary>
        public Exception Exception { get; }
    }

    /// <summary>
    /// Base signal type returned by stations.
    /// </summary>
    public abstract class Signal
    {
        /// <summary>
        /// Creates a signal with the provided manifest.
        /// </summary>
        protected Signal(CargoManifest manifest)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        /// <summary>
        /// Gets the manifest carried by this signal.
        /// </summary>
        public CargoManifest Manifest { get; }

        /// <summary>
        /// Gets whether the signal allows route continuation.
        /// </summary>
        public abstract bool IsGreen { get; }
    }

    /// <summary>
    /// Signal indicating route continuation.
    /// </summary>
    public sealed class GreenSignal : Signal
    {
        /// <summary>
        /// Creates a green signal.
        /// </summary>
        public GreenSignal(CargoManifest manifest)
            : base(manifest)
        {
        }

        /// <summary>
        /// Gets whether the signal allows route continuation.
        /// </summary>
        public override bool IsGreen => true;
    }

    /// <summary>
    /// Signal indicating route stop with issue information.
    /// </summary>
    public sealed class RedSignal : Signal
    {
        /// <summary>
        /// Creates a red signal.
        /// </summary>
        public RedSignal(CargoManifest manifest, SignalIssue issue)
            : base(manifest)
        {
            Issue = issue ?? throw new ArgumentNullException(nameof(issue));
        }

        /// <summary>
        /// Gets the issue that stopped the route.
        /// </summary>
        public SignalIssue Issue { get; }

        /// <summary>
        /// Gets whether the signal allows route continuation.
        /// </summary>
        public override bool IsGreen => false;
    }

    /// <summary>
    /// Factory methods for creating route signals.
    /// </summary>
    public static class RailwaySignals
    {
        /// <summary>
        /// Creates a green signal for the provided manifest.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static GreenSignal Green(CargoManifest manifest)
        {
            return new GreenSignal(manifest);
        }

        /// <summary>
        /// Creates a green signal payload for data-oriented handlers.
        /// The payload is merged into the manifest by generated adapters.
        /// </summary>
        public static GreenPayload<T> Green<T>(T payload)
        {
            return new GreenPayload<T>(payload);
        }

        /// <summary>
        /// Leaves the manifest unchanged and continues the route with a green signal.
        /// </summary>
        public static GreenPass Pass => GreenPass.Instance;

        /// <summary>
        /// Creates a red signal for the provided manifest and issue.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static RedSignal Red(CargoManifest manifest, SignalIssue issue)
        {
            return new RedSignal(manifest, issue);
        }

        /// <summary>
        /// Creates a red signal request for data-oriented handlers.
        /// The current manifest and station name are filled in by generated adapters.
        /// </summary>
        public static RedFailure Red(string code, string message)
        {
            return new RedFailure(code, message);
        }
    }

    /// <summary>
    /// Represents one executed station and whether it returned a green signal.
    /// Failure details are available only from <see cref="RouteReport.TerminalSignal"/>.
    /// </summary>
    public readonly struct StationVisit
    {
        /// <summary>
        /// Creates station visit information.
        /// </summary>
        public StationVisit(string stationName, bool isGreen)
        {
            StationName = stationName ?? throw new ArgumentNullException(nameof(stationName));
            IsGreen = isGreen;
        }

        /// <summary>
        /// Gets the executed station name.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Gets whether the station returned a green signal on this hop.
        /// </summary>
        public bool IsGreen { get; }
    }

    /// <summary>
    /// Final execution report for a train route.
    /// </summary>
    public sealed class RouteReport
    {
        /// <summary>
        /// Creates a route report.
        /// </summary>
        public RouteReport(IReadOnlyList<StationVisit> visits, Signal terminalSignal)
        {
            Visits = visits ?? throw new ArgumentNullException(nameof(visits));
            TerminalSignal = terminalSignal ?? throw new ArgumentNullException(nameof(terminalSignal));
        }

        /// <summary>
        /// Gets the list of executed station visits.
        /// </summary>
        public IReadOnlyList<StationVisit> Visits { get; }

        /// <summary>
        /// Gets the final signal that ended route execution.
        /// </summary>
        public Signal TerminalSignal { get; }

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

            var manifest = TerminalSignal.Manifest;
            if (!manifest.TryGetWagon(wagonName, out var value))
            {
                throw new KeyNotFoundException("Wagon '" + wagonName + "' was not found in the terminal report.");
            }

            if (!(value is T typed))
            {
                throw new InvalidCastException(
                    "Wagon '" + wagonName + "' contains '" + value.GetType().FullName + "', cannot cast to '" + typeof(T).FullName + "'.");
            }

            return typed;
        }
    }

    /// <summary>
    /// Builder for a route made of stations.
    /// Use generated <see cref="Station"/> extensions for data-oriented handlers.
    /// </summary>
    public sealed class TrainRoute
    {
        private readonly List<StationPlan> _route = new List<StationPlan>();
        private ServiceStationPlan _serviceStation;
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

            _route.Add(new StationPlan(stationName, throughStation));
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

            _route.Add(new StationPlan(stationName, throughStationWithToken));
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

            _route.Add(new StationPlan(stationName, station));
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

            _route.Add(new StationPlan(stationName, stationWithToken));
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

            _route.Add(new StationPlan(stationName, (manifest, _) => throughAsyncStation(manifest)));
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

            _route.Add(new StationPlan(stationName, throughAsyncStation));
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

            _route.Add(new StationPlan(stationName, (manifest, _) => asyncStation(manifest)));
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

            _route.Add(new StationPlan(stationName, asyncStation));
            return this;
        }

        /// <summary>
        /// Builds a train instance for executing the configured route.
        /// </summary>
        public Train DispatchTrain()
        {
            return new Train(_route, _serviceStation);
        }

        /// <summary>
        /// Attaches a synchronous service station that handles red signals.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, Signal> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, _) => handler(red));
        }

        /// <summary>
        /// Attaches a synchronous service station with cancellation support.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, CancellationToken, Signal> handler)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _serviceStation = new ServiceStationPlan(stationName, handler);
            return this;
        }

        /// <summary>
        /// Attaches an asynchronous service station that handles red signals.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, Task<Signal>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return ServiceStation(stationName, (red, _) => handler(red));
        }

        /// <summary>
        /// Attaches an asynchronous service station with cancellation support.
        /// </summary>
        public TrainRoute ServiceStation(string stationName, Func<RedSignal, CancellationToken, Task<Signal>> handler)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _serviceStation = new ServiceStationPlan(stationName, handler);
            return this;
        }
    }

    /// <summary>
    /// Executes stations configured in a train route.
    /// </summary>
    public sealed class Train
    {
        private readonly IReadOnlyList<StationPlan> _route;
        private readonly ServiceStationPlan _serviceStation;
        private const string StationExceptionCode = "STATION_EXCEPTION";
        private const string ServiceStationExceptionCode = "SERVICE_STATION_EXCEPTION";

        /// <summary>
        /// Creates a train from the configured route and optional service station.
        /// </summary>
        internal Train(IReadOnlyList<StationPlan> route, ServiceStationPlan serviceStation)
        {
            _route = route ?? throw new ArgumentNullException(nameof(route));
            _serviceStation = serviceStation;
        }

        /// <summary>
        /// Pre-sized visit journal capacity: one entry per route station, doubled when a service station may add recovery visits.
        /// </summary>
        private int VisitJournalCapacity =>
            _serviceStation != null ? _route.Count * 2 : _route.Count;

        /// <summary>
        /// Executes the route from an empty manifest.
        /// </summary>
        public RouteReport Travel()
        {
            return TravelCore(new CargoManifest(), CancellationToken.None);
        }

        /// <summary>
        /// Executes the route from an empty manifest with cancellation support.
        /// </summary>
        public RouteReport Travel(CancellationToken cancellationToken)
        {
            return TravelCore(new CargoManifest(), cancellationToken);
        }

        /// <summary>
        /// Asynchronously executes the route from an empty manifest.
        /// </summary>
        public Task<RouteReport> TravelAsync()
        {
            return TravelCoreAsync(new CargoManifest(), CancellationToken.None);
        }

        /// <summary>
        /// Asynchronously executes the route from an empty manifest with cancellation support.
        /// </summary>
        public Task<RouteReport> TravelAsync(CancellationToken cancellationToken)
        {
            return TravelCoreAsync(new CargoManifest(), cancellationToken);
        }

        /// <summary>
        /// Executes all route stations synchronously and returns the final report.
        /// </summary>
        private RouteReport TravelCore(CargoManifest manifest, CancellationToken cancellationToken)
        {
            var current = manifest ?? throw new ArgumentNullException(nameof(manifest));
            var visits = new List<StationVisit>(VisitJournalCapacity);

            for (var i = 0; i < _route.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var plan = _route[i];
                var signal = ExecuteStation(plan, current, cancellationToken);
                var step = ProcessStationStep(signal, plan.StationName, visits, current, cancellationToken);

                if (step.TerminalReport != null)
                {
                    return step.TerminalReport;
                }

                current = step.Current;
            }

            return new RouteReport(visits, RailwaySignals.Green(current));
        }

        /// <summary>
        /// Executes all route stations asynchronously and returns the final report.
        /// </summary>
        private async Task<RouteReport> TravelCoreAsync(CargoManifest manifest, CancellationToken cancellationToken)
        {
            var current = manifest ?? throw new ArgumentNullException(nameof(manifest));
            var visits = new List<StationVisit>(VisitJournalCapacity);

            for (var i = 0; i < _route.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var plan = _route[i];
                var signal = await ExecuteStationAsync(plan, current, cancellationToken).ConfigureAwait(false);
                var step = await ProcessStationStepAsync(signal, plan.StationName, visits, current, cancellationToken)
                    .ConfigureAwait(false);

                if (step.TerminalReport != null)
                {
                    return step.TerminalReport;
                }

                current = step.Current;
            }

            return new RouteReport(visits, RailwaySignals.Green(current));
        }

        /// <summary>
        /// Invokes one synchronous station plan and returns its signal.
        /// </summary>
        private static Signal ExecuteStation(
            StationPlan plan,
            CargoManifest current,
            CancellationToken cancellationToken)
        {
            if (plan.IsAsync)
            {
                throw new InvalidOperationException(
                    "Route contains async station '" + plan.StationName + "'. Use TravelAsync instead of Travel.");
            }

            try
            {
                if (plan.ThroughStationWithToken != null)
                {
                    var nextManifest = plan.ThroughStationWithToken(current, cancellationToken);
                    return RailwaySignals.Green(nextManifest ?? current);
                }

                if (plan.ThroughStation != null)
                {
                    var nextManifest = plan.ThroughStation(current);
                    return RailwaySignals.Green(nextManifest ?? current);
                }

                if (plan.StationWithToken != null)
                {
                    return plan.StationWithToken(current, cancellationToken);
                }

                return plan.Station(current);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapStationException(plan, current, exception);
            }
        }

        /// <summary>
        /// Invokes one station plan and returns its signal.
        /// </summary>
        private static async Task<Signal> ExecuteStationAsync(
            StationPlan plan,
            CargoManifest current,
            CancellationToken cancellationToken)
        {
            try
            {
                if (plan.ThroughAsyncStation != null)
                {
                    var nextManifest = await plan.ThroughAsyncStation(current, cancellationToken).ConfigureAwait(false);
                    return RailwaySignals.Green(nextManifest ?? current);
                }

                if (plan.AsyncStation != null)
                {
                    return await plan.AsyncStation(current, cancellationToken).ConfigureAwait(false);
                }

                if (plan.ThroughStationWithToken != null)
                {
                    var nextManifest = plan.ThroughStationWithToken(current, cancellationToken);
                    return RailwaySignals.Green(nextManifest ?? current);
                }

                if (plan.ThroughStation != null)
                {
                    var nextManifest = plan.ThroughStation(current);
                    return RailwaySignals.Green(nextManifest ?? current);
                }

                if (plan.StationWithToken != null)
                {
                    return plan.StationWithToken(current, cancellationToken);
                }

                return plan.Station(current);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapStationException(plan, current, exception);
            }
        }

        /// <summary>
        /// Records a station visit and handles red signals via the service station when configured.
        /// </summary>
        private StationStepResult ProcessStationStep(
            Signal signal,
            string stationName,
            List<StationVisit> visits,
            CargoManifest current,
            CancellationToken cancellationToken)
        {
            EnsureStationSignal(signal, stationName);
            visits.Add(new StationVisit(stationName, signal.IsGreen));
            current = signal.Manifest;

            if (signal.IsGreen)
            {
                return new StationStepResult(current, null);
            }

            var handled = InvokeServiceStation((RedSignal)signal, cancellationToken);
            return CompleteRedSignalStep(signal, handled, visits, current);
        }

        /// <summary>
        /// Records a station visit and handles red signals via the service station when configured.
        /// </summary>
        private async Task<StationStepResult> ProcessStationStepAsync(
            Signal signal,
            string stationName,
            List<StationVisit> visits,
            CargoManifest current,
            CancellationToken cancellationToken)
        {
            EnsureStationSignal(signal, stationName);
            visits.Add(new StationVisit(stationName, signal.IsGreen));
            current = signal.Manifest;

            if (signal.IsGreen)
            {
                return new StationStepResult(current, null);
            }

            var handled = await InvokeServiceStationAsync((RedSignal)signal, cancellationToken).ConfigureAwait(false);
            return CompleteRedSignalStep(signal, handled, visits, current);
        }

        /// <summary>
        /// Invokes the configured synchronous service station for a red signal, if present.
        /// </summary>
        private Signal InvokeServiceStation(RedSignal redSignal, CancellationToken cancellationToken)
        {
            if (_serviceStation == null)
            {
                return null;
            }

            if (_serviceStation.AsyncHandler != null)
            {
                throw new InvalidOperationException(
                    "Route contains async service station '" + _serviceStation.StationName + "'. Use TravelAsync instead of Travel.");
            }

            try
            {
                var handled = _serviceStation.SyncHandler(redSignal, cancellationToken);
                return handled ?? redSignal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapServiceStationException(_serviceStation, redSignal, exception);
            }
        }

        /// <summary>
        /// Invokes the configured service station for a red signal, if present.
        /// </summary>
        private async Task<Signal> InvokeServiceStationAsync(
            RedSignal redSignal,
            CancellationToken cancellationToken)
        {
            if (_serviceStation == null)
            {
                return null;
            }

            try
            {
                Signal handled;
                if (_serviceStation.AsyncHandler != null)
                {
                    handled = await _serviceStation.AsyncHandler(redSignal, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    handled = _serviceStation.SyncHandler(redSignal, cancellationToken);
                }

                return handled ?? redSignal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapServiceStationException(_serviceStation, redSignal, exception);
            }
        }

        /// <summary>
        /// Validates that a station returned a non-null signal.
        /// </summary>
        private static void EnsureStationSignal(Signal signal, string stationName)
        {
            if (signal == null)
            {
                throw new InvalidOperationException("Station '" + stationName + "' returned null signal.");
            }
        }

        /// <summary>
        /// Converts an unhandled station exception into a red signal.
        /// </summary>
        private static Signal WrapStationException(StationPlan plan, CargoManifest current, Exception exception)
        {
            var issue = new SignalIssue(
                StationExceptionCode,
                "Unhandled station exception: " + exception.Message,
                plan.StationName,
                exception);
            return RailwaySignals.Red(current, issue);
        }

        /// <summary>
        /// Converts an unhandled service-station exception into a red signal.
        /// </summary>
        private static Signal WrapServiceStationException(
            ServiceStationPlan serviceStation,
            RedSignal redSignal,
            Exception exception)
        {
            var issue = new SignalIssue(
                ServiceStationExceptionCode,
                "Unhandled service station exception: " + exception.Message,
                serviceStation.StationName,
                exception);
            return RailwaySignals.Red(redSignal.Manifest, issue);
        }

        /// <summary>
        /// Applies service-station handling after a red signal, or returns a terminal report.
        /// </summary>
        private StationStepResult CompleteRedSignalStep(
            Signal originalSignal,
            Signal handled,
            List<StationVisit> visits,
            CargoManifest currentAfterRed)
        {
            if (handled != null)
            {
                visits.Add(new StationVisit(_serviceStation.StationName, handled.IsGreen));
                currentAfterRed = handled.Manifest;
                if (handled.IsGreen)
                {
                    return new StationStepResult(currentAfterRed, null);
                }

                return new StationStepResult(currentAfterRed, new RouteReport(visits, handled));
            }

            return new StationStepResult(currentAfterRed, new RouteReport(visits, originalSignal));
        }
    }
}
