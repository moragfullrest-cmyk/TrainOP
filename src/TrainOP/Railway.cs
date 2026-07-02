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
        private readonly Dictionary<string, object> _cars;

        /// <summary>
        /// Creates an empty cargo manifest.
        /// </summary>
        public CargoManifest()
            : this(new Dictionary<string, object>(StringComparer.Ordinal))
        {
        }

        private CargoManifest(Dictionary<string, object> cars)
        {
            _cars = cars;
        }

        /// <summary>
        /// Checks whether a wagon with the specified name exists.
        /// </summary>
        public bool HasCar(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName))
            {
                throw new ArgumentException("Car name cannot be empty.", nameof(carName));
            }

            return _cars.ContainsKey(carName);
        }

        /// <summary>
        /// Reads a typed wagon value by name.
        /// </summary>
        public T PullCar<T>(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName))
            {
                throw new ArgumentException("Car name cannot be empty.", nameof(carName));
            }

            if (!_cars.TryGetValue(carName, out var value))
            {
                throw new KeyNotFoundException("Car '" + carName + "' was not found in the manifest.");
            }

            if (!(value is T typed))
            {
                throw new InvalidCastException(
                    "Car '" + carName + "' contains '" + value.GetType().FullName + "', cannot cast to '" + typeof(T).FullName + "'.");
            }

            return typed;
        }

        /// <summary>
        /// Adds or replaces a wagon value and returns a new manifest.
        /// </summary>
        public CargoManifest LoadCar(string carName, object cargo)
        {
            if (string.IsNullOrWhiteSpace(carName))
            {
                throw new ArgumentException("Car name cannot be empty.", nameof(carName));
            }

            var cloned = CloneCars();
            cloned[carName] = cargo;
            return new CargoManifest(cloned);
        }

        /// <summary>
        /// Removes a wagon by name and returns a new manifest.
        /// </summary>
        public CargoManifest UnloadCar(string carName)
        {
            if (string.IsNullOrWhiteSpace(carName))
            {
                throw new ArgumentException("Car name cannot be empty.", nameof(carName));
            }

            var cloned = CloneCars();
            cloned.Remove(carName);
            return new CargoManifest(cloned);
        }

        /// <summary>
        /// Returns a read-only snapshot of current wagon values.
        /// </summary>
        public IReadOnlyDictionary<string, object> InspectCars()
        {
            return new ReadOnlyDictionary<string, object>(_cars);
        }

        private Dictionary<string, object> CloneCars()
        {
            return new Dictionary<string, object>(_cars, StringComparer.Ordinal);
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
        public SignalIssue(string code, string message, string stationName)
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
        }

        public string Code { get; }

        public string Message { get; }

        public string StationName { get; }
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
        /// Creates a red signal for the provided manifest and issue.
        /// </summary>
        public static RedSignal Red(CargoManifest manifest, SignalIssue issue)
        {
            return new RedSignal(manifest, issue);
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

    internal sealed class RedSignalStationPlan
    {
        public RedSignalStationPlan(string stationName, Func<RedSignal, CancellationToken, Signal> syncHandler)
        {
            StationName = stationName;
            SyncHandler = syncHandler;
        }

        public RedSignalStationPlan(string stationName, Func<RedSignal, CancellationToken, Task<Signal>> asyncHandler)
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
        private RedSignalStationPlan _redSignalStation;

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
            return new Train(_route, _redSignalStation);
        }

        /// <summary>
        /// Attaches a synchronous red-signal handler station.
        /// </summary>
        public TrainRoute AttachRedSignalStation(string stationName, Func<RedSignal, Signal> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return AttachRedSignalStation(stationName, (red, _) => handler(red));
        }

        /// <summary>
        /// Attaches a synchronous red-signal handler with cancellation support.
        /// </summary>
        public TrainRoute AttachRedSignalStation(string stationName, Func<RedSignal, CancellationToken, Signal> handler)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _redSignalStation = new RedSignalStationPlan(stationName, handler);
            return this;
        }

        /// <summary>
        /// Attaches an asynchronous red-signal handler station.
        /// </summary>
        public TrainRoute AttachRedSignalStation(string stationName, Func<RedSignal, Task<Signal>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return AttachRedSignalStation(stationName, (red, _) => handler(red));
        }

        /// <summary>
        /// Attaches an asynchronous red-signal handler with cancellation support.
        /// </summary>
        public TrainRoute AttachRedSignalStation(string stationName, Func<RedSignal, CancellationToken, Task<Signal>> handler)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _redSignalStation = new RedSignalStationPlan(stationName, handler);
            return this;
        }
    }

    /// <summary>
    /// Executes stations configured in a train route.
    /// </summary>
    public sealed class Train
    {
        private readonly IReadOnlyList<StationPlan> _route;
        private readonly RedSignalStationPlan _redSignalStation;
        private const string StationExceptionCode = "STATION_EXCEPTION";
        private const string RedSignalStationExceptionCode = "RED_SIGNAL_STATION_EXCEPTION";

        internal Train(IReadOnlyList<StationPlan> route, RedSignalStationPlan redSignalStation)
        {
            _route = route ?? throw new ArgumentNullException(nameof(route));
            _redSignalStation = redSignalStation;
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
            var current = manifest ?? throw new ArgumentNullException(nameof(manifest));
            var visits = new List<StationVisit>();

            for (var i = 0; i < _route.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var plan = _route[i];
                if (plan.IsAsync)
                {
                    throw new InvalidOperationException(
                        "Route contains async station '" + plan.StationName + "'. Use TravelAsync instead of Travel.");
                }

                Signal signal;
                try
                {
                    if (plan.ThroughStationWithToken != null)
                    {
                        var nextManifest = plan.ThroughStationWithToken(current, cancellationToken);
                        signal = RailwaySignals.Green(nextManifest ?? current);
                    }
                    else if (plan.ThroughStation != null)
                    {
                        var nextManifest = plan.ThroughStation(current);
                        signal = RailwaySignals.Green(nextManifest ?? current);
                    }
                    else if (plan.StationWithToken != null)
                    {
                        signal = plan.StationWithToken(current, cancellationToken);
                    }
                    else
                    {
                        signal = plan.Station(current);
                    }
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
                        plan.StationName);
                    signal = RailwaySignals.Red(current, issue);
                }

                if (signal == null)
                {
                    throw new InvalidOperationException("Station '" + plan.StationName + "' returned null signal.");
                }

                visits.Add(new StationVisit(plan.StationName, signal));
                current = signal.Manifest;

                if (!signal.IsGreen)
                {
                    var red = (RedSignal)signal;
                    var handled = HandleRedSignal(red, cancellationToken);
                    if (handled != null)
                    {
                        visits.Add(new StationVisit(_redSignalStation.StationName, handled));
                        current = handled.Manifest;
                        if (handled.IsGreen)
                        {
                            continue;
                        }

                        return new RouteReport(visits, handled);
                    }

                    return new RouteReport(visits, signal);
                }
            }

            return new RouteReport(visits, RailwaySignals.Green(current));
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
        public async Task<RouteReport> TravelAsync(CargoManifest manifest, CancellationToken cancellationToken = default(CancellationToken))
        {
            var current = manifest ?? throw new ArgumentNullException(nameof(manifest));
            var visits = new List<StationVisit>();

            for (var i = 0; i < _route.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var plan = _route[i];
                Signal signal;
                try
                {
                    if (plan.ThroughAsyncStation != null)
                    {
                        var nextManifest = await plan.ThroughAsyncStation(current, cancellationToken).ConfigureAwait(false);
                        signal = RailwaySignals.Green(nextManifest ?? current);
                    }
                    else if (plan.AsyncStation != null)
                    {
                        signal = await plan.AsyncStation(current, cancellationToken).ConfigureAwait(false);
                    }
                    else if (plan.ThroughStation != null)
                    {
                        var nextManifest = plan.ThroughStation(current);
                        signal = RailwaySignals.Green(nextManifest ?? current);
                    }
                    else if (plan.ThroughStationWithToken != null)
                    {
                        var nextManifest = plan.ThroughStationWithToken(current, cancellationToken);
                        signal = RailwaySignals.Green(nextManifest ?? current);
                    }
                    else if (plan.StationWithToken != null)
                    {
                        signal = plan.StationWithToken(current, cancellationToken);
                    }
                    else
                    {
                        signal = plan.Station(current);
                    }
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
                        plan.StationName);
                    signal = RailwaySignals.Red(current, issue);
                }

                if (signal == null)
                {
                    throw new InvalidOperationException("Station '" + plan.StationName + "' returned null signal.");
                }

                visits.Add(new StationVisit(plan.StationName, signal));
                current = signal.Manifest;

                if (!signal.IsGreen)
                {
                    var red = (RedSignal)signal;
                    var handled = await HandleRedSignalAsync(red, cancellationToken).ConfigureAwait(false);
                    if (handled != null)
                    {
                        visits.Add(new StationVisit(_redSignalStation.StationName, handled));
                        current = handled.Manifest;
                        if (handled.IsGreen)
                        {
                            continue;
                        }

                        return new RouteReport(visits, handled);
                    }

                    return new RouteReport(visits, signal);
                }
            }

            return new RouteReport(visits, RailwaySignals.Green(current));
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

        private Signal HandleRedSignal(RedSignal redSignal, CancellationToken cancellationToken)
        {
            if (_redSignalStation == null)
            {
                return null;
            }

            if (_redSignalStation.AsyncHandler != null)
            {
                throw new InvalidOperationException(
                    "Route contains async red-signal station '" + _redSignalStation.StationName + "'. Use TravelAsync instead of Travel.");
            }

            try
            {
                var handled = _redSignalStation.SyncHandler(redSignal, cancellationToken);
                return handled ?? redSignal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var issue = new SignalIssue(
                    RedSignalStationExceptionCode,
                    "Unhandled red-signal station exception: " + exception.Message,
                    _redSignalStation.StationName);
                return RailwaySignals.Red(redSignal.Manifest, issue);
            }
        }

        private async Task<Signal> HandleRedSignalAsync(RedSignal redSignal, CancellationToken cancellationToken)
        {
            if (_redSignalStation == null)
            {
                return null;
            }

            try
            {
                Signal handled;
                if (_redSignalStation.AsyncHandler != null)
                {
                    handled = await _redSignalStation.AsyncHandler(redSignal, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    handled = _redSignalStation.SyncHandler(redSignal, cancellationToken);
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
                    RedSignalStationExceptionCode,
                    "Unhandled red-signal station exception: " + exception.Message,
                    _redSignalStation.StationName);
                return RailwaySignals.Red(redSignal.Manifest, issue);
            }
        }
    }
}
