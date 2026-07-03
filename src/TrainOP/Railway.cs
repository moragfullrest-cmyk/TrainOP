using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace TrainOP
{
    /// <summary>
    /// Immutable shared storage for wagon values between route stations.
    /// </summary>
    public sealed class CargoManifest
    {
        private readonly Dictionary<string, object> _wagons;

        /// <summary>
        /// Creates an empty cargo manifest.
        /// </summary>
        public CargoManifest()
            : this(new Dictionary<string, object>(StringComparer.Ordinal))
        {
        }

        private CargoManifest(Dictionary<string, object> wagons)
        {
            _wagons = wagons;
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
        /// Adds or replaces a wagon value and returns a new manifest.
        /// </summary>
        public CargoManifest LoadWagon(string wagonName, object cargo)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            var cloned = CloneWagons();
            cloned[wagonName] = cargo;
            return new CargoManifest(cloned);
        }

        /// <summary>
        /// Removes a wagon by name and returns a new manifest.
        /// </summary>
        public CargoManifest UnloadWagon(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            var cloned = CloneWagons();
            cloned.Remove(wagonName);
            return new CargoManifest(cloned);
        }

        /// <summary>
        /// Returns a read-only snapshot of current wagon values.
        /// </summary>
        public IReadOnlyDictionary<string, object> InspectWagons()
        {
            return new ReadOnlyDictionary<string, object>(_wagons);
        }

        private Dictionary<string, object> CloneWagons()
        {
            return new Dictionary<string, object>(_wagons, StringComparer.Ordinal);
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

        public string Code { get; }

        public string Message { get; }

        public string StationName { get; }

        public Exception Exception { get; }
    }

    /// <summary>
    /// Base signal type returned by stations.
    /// </summary>
    public abstract class Signal
    {
        protected Signal(CargoManifest manifest)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        public CargoManifest Manifest { get; }

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

        public SignalIssue Issue { get; }

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
    /// Represents one executed station and the resulting signal.
    /// </summary>
    public sealed class StationVisit
    {
        /// <summary>
        /// Creates station visit information.
        /// </summary>
        public StationVisit(string stationName, Signal signal)
        {
            StationName = stationName ?? throw new ArgumentNullException(nameof(stationName));
            Signal = signal ?? throw new ArgumentNullException(nameof(signal));
        }

        public string StationName { get; }

        public Signal Signal { get; }
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

        public IReadOnlyList<StationVisit> Visits { get; }

        public Signal TerminalSignal { get; }

        public bool ReachedDestination => TerminalSignal.IsGreen;
    }

    internal sealed class StationPlan
    {
        public StationPlan(string stationName, Func<CargoManifest, Signal> station)
        {
            StationName = stationName;
            Station = station;
        }

        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<Signal>> asyncStation)
        {
            StationName = stationName;
            AsyncStation = asyncStation;
        }

        public StationPlan(string stationName, Func<CargoManifest, CargoManifest> throughStation)
        {
            StationName = stationName;
            ThroughStation = throughStation;
        }

        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Task<CargoManifest>> throughAsyncStation)
        {
            StationName = stationName;
            ThroughAsyncStation = throughAsyncStation;
        }

        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, Signal> stationWithToken)
        {
            StationName = stationName;
            StationWithToken = stationWithToken;
        }

        public StationPlan(string stationName, Func<CargoManifest, CancellationToken, CargoManifest> throughStationWithToken)
        {
            StationName = stationName;
            ThroughStationWithToken = throughStationWithToken;
        }

        public string StationName { get; }

        public Func<CargoManifest, Signal> Station { get; }

        public Func<CargoManifest, CancellationToken, Signal> StationWithToken { get; }

        public Func<CargoManifest, CargoManifest> ThroughStation { get; }

        public Func<CargoManifest, CancellationToken, CargoManifest> ThroughStationWithToken { get; }

        public Func<CargoManifest, CancellationToken, Task<Signal>> AsyncStation { get; }

        public Func<CargoManifest, CancellationToken, Task<CargoManifest>> ThroughAsyncStation { get; }

        public bool IsAsync => AsyncStation != null || ThroughAsyncStation != null;
    }

    internal sealed class ServiceStationPlan
    {
        public ServiceStationPlan(string stationName, Func<RedSignal, CancellationToken, Signal> syncHandler)
        {
            StationName = stationName;
            SyncHandler = syncHandler;
        }

        public ServiceStationPlan(string stationName, Func<RedSignal, CancellationToken, Task<Signal>> asyncHandler)
        {
            StationName = stationName;
            AsyncHandler = asyncHandler;
        }

        public string StationName { get; }

        public Func<RedSignal, CancellationToken, Signal> SyncHandler { get; }

        public Func<RedSignal, CancellationToken, Task<Signal>> AsyncHandler { get; }
    }

    /// <summary>
    /// Builder for a route made of stations.
    /// Use generated <see cref="Station"/> extensions for data-oriented handlers;
    /// use <see cref="AttachStation"/> for manifest-level control.
    /// </summary>
    public sealed class TrainRoute
    {
        private readonly List<StationPlan> _route = new List<StationPlan>();
        private ServiceStationPlan _serviceStation;

        /// <summary>
        /// Attaches a synchronous station that returns an updated manifest.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, CargoManifest> throughStation)
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
        /// Attaches a synchronous station with cancellation support that returns an updated manifest.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, CancellationToken, CargoManifest> throughStationWithToken)
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
        /// Attaches a synchronous station that returns a signal.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, Signal> station)
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
        /// Attaches a synchronous station with cancellation support that returns a signal.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, CancellationToken, Signal> stationWithToken)
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
        /// Attaches an asynchronous station that returns an updated manifest.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, Task<CargoManifest>> throughAsyncStation)
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
        /// Attaches an asynchronous station with cancellation support that returns an updated manifest.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, CancellationToken, Task<CargoManifest>> throughAsyncStation)
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
        /// Attaches an asynchronous station that returns a signal.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, Task<Signal>> asyncStation)
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
        /// Attaches an asynchronous station with cancellation support that returns a signal.
        /// </summary>
        public TrainRoute AttachStation(string stationName, Func<CargoManifest, CancellationToken, Task<Signal>> asyncStation)
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

        internal Train(IReadOnlyList<StationPlan> route, ServiceStationPlan serviceStation)
        {
            _route = route ?? throw new ArgumentNullException(nameof(route));
            _serviceStation = serviceStation;
        }

        /// <summary>
        /// Executes the route from the specified manifest.
        /// </summary>
        public RouteReport Travel(CargoManifest manifest)
        {
            return Travel(manifest, CancellationToken.None);
        }

        /// <summary>
        /// Executes the route from an empty manifest.
        /// </summary>
        public RouteReport Travel()
        {
            return Travel(new CargoManifest(), CancellationToken.None);
        }

        /// <summary>
        /// Executes the route from the specified manifest with cancellation support.
        /// </summary>
        public RouteReport Travel(CargoManifest manifest, CancellationToken cancellationToken)
        {
            return TravelCoreAsync(manifest, cancellationToken, synchronousOnly: true)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Executes the route from an empty manifest with cancellation support.
        /// </summary>
        public RouteReport Travel(CancellationToken cancellationToken)
        {
            return Travel(new CargoManifest(), cancellationToken);
        }

        /// <summary>
        /// Asynchronously executes the route from the specified manifest.
        /// </summary>
        public Task<RouteReport> TravelAsync(CargoManifest manifest, CancellationToken cancellationToken = default(CancellationToken))
        {
            return TravelCoreAsync(manifest, cancellationToken, synchronousOnly: false);
        }

        /// <summary>
        /// Asynchronously executes the route from an empty manifest.
        /// </summary>
        public Task<RouteReport> TravelAsync()
        {
            return TravelAsync(new CargoManifest(), CancellationToken.None);
        }

        /// <summary>
        /// Asynchronously executes the route from an empty manifest with cancellation support.
        /// </summary>
        public Task<RouteReport> TravelAsync(CancellationToken cancellationToken)
        {
            return TravelAsync(new CargoManifest(), cancellationToken);
        }

        private async Task<RouteReport> TravelCoreAsync(
            CargoManifest manifest,
            CancellationToken cancellationToken,
            bool synchronousOnly)
        {
            var current = manifest ?? throw new ArgumentNullException(nameof(manifest));
            var visits = new List<StationVisit>();

            for (var i = 0; i < _route.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var plan = _route[i];
                var signal = await ExecuteStationAsync(plan, current, cancellationToken, synchronousOnly)
                    .ConfigureAwait(false);
                var step = await ProcessStationStepAsync(signal, plan.StationName, visits, current, cancellationToken, synchronousOnly)
                    .ConfigureAwait(false);

                if (step.TerminalReport != null)
                {
                    return step.TerminalReport;
                }

                current = step.Current;
            }

            return new RouteReport(visits, RailwaySignals.Green(current));
        }

        private static async Task<Signal> ExecuteStationAsync(
            StationPlan plan,
            CargoManifest current,
            CancellationToken cancellationToken,
            bool synchronousOnly)
        {
            if (synchronousOnly && plan.IsAsync)
            {
                throw new InvalidOperationException(
                    "Route contains async station '" + plan.StationName + "'. Use TravelAsync instead of Travel.");
            }

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
                var issue = new SignalIssue(
                    StationExceptionCode,
                    "Unhandled station exception: " + exception.Message,
                    plan.StationName,
                    exception);
                return RailwaySignals.Red(current, issue);
            }
        }

        private async Task<StationStepResult> ProcessStationStepAsync(
            Signal signal,
            string stationName,
            List<StationVisit> visits,
            CargoManifest current,
            CancellationToken cancellationToken,
            bool synchronousOnly)
        {
            if (signal == null)
            {
                throw new InvalidOperationException("Station '" + stationName + "' returned null signal.");
            }

            visits.Add(new StationVisit(stationName, signal));
            current = signal.Manifest;

            if (signal.IsGreen)
            {
                return new StationStepResult(current, null);
            }

            var red = (RedSignal)signal;
            var handled = await InvokeServiceStationAsync(red, cancellationToken, synchronousOnly).ConfigureAwait(false);
            if (handled != null)
            {
                visits.Add(new StationVisit(_serviceStation.StationName, handled));
                current = handled.Manifest;
                if (handled.IsGreen)
                {
                    return new StationStepResult(current, null);
                }

                return new StationStepResult(current, new RouteReport(visits, handled));
            }

            return new StationStepResult(current, new RouteReport(visits, signal));
        }

        private async Task<Signal> InvokeServiceStationAsync(
            RedSignal redSignal,
            CancellationToken cancellationToken,
            bool synchronousOnly)
        {
            if (_serviceStation == null)
            {
                return null;
            }

            if (synchronousOnly && _serviceStation.AsyncHandler != null)
            {
                throw new InvalidOperationException(
                    "Route contains async service station '" + _serviceStation.StationName + "'. Use TravelAsync instead of Travel.");
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
                var issue = new SignalIssue(
                    ServiceStationExceptionCode,
                    "Unhandled service station exception: " + exception.Message,
                    _serviceStation.StationName,
                    exception);
                return RailwaySignals.Red(redSignal.Manifest, issue);
            }
        }

        private readonly struct StationStepResult
        {
            public StationStepResult(CargoManifest current, RouteReport terminalReport)
            {
                Current = current;
                TerminalReport = terminalReport;
            }

            public CargoManifest Current { get; }

            public RouteReport TerminalReport { get; }
        }
    }
}
