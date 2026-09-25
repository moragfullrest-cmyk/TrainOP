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
            _wagons = new Dictionary<string, object>();
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

            return HasWagonUnchecked(wagonName);
        }

        /// <summary>
        /// Checks whether a wagon exists. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasWagonUnchecked(string wagonName)
        {
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

            return TryGetWagonUnchecked(wagonName, out cargo);
        }

        /// <summary>
        /// Tries to read a wagon value. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool TryGetWagonUnchecked(string wagonName, out object cargo)
        {
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

            return PullWagonUnchecked<T>(wagonName);
        }

        /// <summary>
        /// Reads a typed wagon value. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T PullWagonUnchecked<T>(string wagonName)
        {
            if (!TryGetWagonUnchecked(wagonName, out var value))
            {
                throw new KeyNotFoundException($"Wagon '{wagonName}' was not found in the manifest.");
            }

            return CastWagonValue<T>(wagonName, value);
        }

        /// <summary>
        /// Casts a stored wagon value to <typeparamref name="T"/>, allowing null when <typeparamref name="T"/> is null-compatible.
        /// </summary>
        internal static T CastWagonValue<T>(string wagonName, object value)
        {
            if (value == null)
            {
                if (default(T) == null)
                {
                    return default;
                }

                throw new InvalidCastException(
                    $"Wagon '{wagonName}' contains null, cannot cast to '{typeof(T).FullName}'.");
            }

            if (!(value is T typed))
            {
                throw new InvalidCastException(
                    $"Wagon '{wagonName}' contains '{value.GetType().FullName}', cannot cast to '{typeof(T).FullName}'.");
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

            return LoadWagonUnchecked(wagonName, cargo);
        }

        /// <summary>
        /// Adds or replaces a wagon value. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public CargoManifest LoadWagonUnchecked(string wagonName, object cargo)
        {
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

            return UnloadWagonUnchecked(wagonName);
        }

        /// <summary>
        /// Removes a wagon by name. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public CargoManifest UnloadWagonUnchecked(string wagonName)
        {
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

        /// <summary>
        /// Replaces all wagon entries with those from <paramref name="source"/> (same instance is a no-op).
        /// </summary>
        internal void ReplaceWith(CargoManifest source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(source, this))
            {
                return;
            }

            _wagons.Clear();
            foreach (var pair in source._wagons)
            {
                _wagons[pair.Key] = pair.Value;
            }
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
    /// Base signal type returned by stations (control only; cargo lives on the run / <see cref="RouteReport"/>).
    /// </summary>
    public abstract class Signal
    {
        /// <summary>
        /// Creates a signal.
        /// </summary>
        protected Signal()
        {
        }

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
        /// Gets the shared green signal instance.
        /// </summary>
        public static GreenSignal Instance { get; } = new GreenSignal();

        private GreenSignal()
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
        private readonly SignalIssue[] _issues;

        /// <summary>
        /// Creates a red signal with a single issue.
        /// </summary>
        public RedSignal(SignalIssue issue)
            : this(issue == null ? null : new[] { issue })
        {
        }

        /// <summary>
        /// Creates a red signal with an ordered issue chain (earliest/root first, immediate stop last).
        /// </summary>
        public RedSignal(IReadOnlyList<SignalIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                throw new ArgumentException("At least one issue is required.", nameof(issues));
            }

            _issues = new SignalIssue[issues.Count];
            for (var i = 0; i < issues.Count; i++)
            {
                _issues[i] = issues[i] ?? throw new ArgumentException("Issue entries cannot be null.", nameof(issues));
            }

            Issue = _issues[_issues.Length - 1];
        }

        /// <summary>
        /// Gets the issue that stopped the route at this hop (last entry in <see cref="Issues"/>).
        /// </summary>
        public SignalIssue Issue { get; }

        /// <summary>
        /// Gets the ordered issue chain from nested/root causes through the immediate stop.
        /// </summary>
        public IReadOnlyList<SignalIssue> Issues => _issues;

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
        /// Creates a green continuation signal (no cargo; manifest is owned by the run).
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static GreenSignal Green()
        {
            return GreenSignal.Instance;
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
        /// Leaves the manifest unchanged and continues the route (lunar-white / pass-through).
        /// </summary>
        public static WhitePass White => WhitePass.Instance;

        /// <summary>
        /// Creates a red signal for the provided issue.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static RedSignal Red(SignalIssue issue)
        {
            return new RedSignal(issue);
        }

        /// <summary>
        /// Creates a red signal with prior nested issues followed by the immediate stop issue.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static RedSignal Red(
            SignalIssue issue,
            IReadOnlyList<SignalIssue> priorIssues)
        {
            if (issue == null)
            {
                throw new ArgumentNullException(nameof(issue));
            }

            if (priorIssues == null || priorIssues.Count == 0)
            {
                return new RedSignal(issue);
            }

            var combined = new SignalIssue[priorIssues.Count + 1];
            for (var i = 0; i < priorIssues.Count; i++)
            {
                combined[i] = priorIssues[i] ?? throw new ArgumentException("Prior issue entries cannot be null.", nameof(priorIssues));
            }

            combined[priorIssues.Count] = issue;
            return new RedSignal(combined);
        }

        /// <summary>
        /// Creates a red signal request for data-oriented handlers.
        /// The station name is filled in by generated adapters.
        /// </summary>
        public static RedFailure Red(string code, string message)
        {
            return new RedFailure(code, message);
        }

        /// <summary>
        /// Creates a red signal request that preserves issues from a nested route failure.
        /// Prior issues are ordered root-first; the adapter appends this station's issue last.
        /// </summary>
        public static RedFailure Red(string code, string message, IReadOnlyList<SignalIssue> priorIssues)
        {
            return new RedFailure(code, message, priorIssues);
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
        public RouteReport(IReadOnlyList<StationVisit> visits, Signal terminalSignal, CargoManifest manifest)
        {
            Visits = visits ?? throw new ArgumentNullException(nameof(visits));
            TerminalSignal = terminalSignal ?? throw new ArgumentNullException(nameof(terminalSignal));
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
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
        /// Gets the terminal cargo manifest for this run.
        /// </summary>
        public CargoManifest Manifest { get; }

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
        /// Gets the ordered issue chain when the route stopped with a red signal; otherwise empty.
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
    }

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
        private Func<bool, RouteReport> _straightTravel;
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
        /// Attaches a generated straight-chain runner. Later stations replace an earlier runner.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void AttachStraightTravel(Func<bool, RouteReport> runner)
        {
            _straightTravel = runner;
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
        /// Executes the route from an empty manifest.
        /// Snapshots the station list so later builder mutations do not affect this run.
        /// </summary>
        public RouteReport Travel()
        {
            return TravelCore(CancellationToken.None, recordVisits: true);
        }

        /// <summary>
        /// Executes the route from an empty manifest with cancellation support.
        /// Snapshots the station list so later builder mutations do not affect this run.
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
        /// Snapshots the station list so later builder mutations do not affect this run.
        /// </summary>
        public Task<RouteReport> TravelAsync()
        {
            return TravelCoreAsync(CancellationToken.None, recordVisits: true);
        }

        /// <summary>
        /// Asynchronously executes the route from an empty manifest with cancellation support.
        /// Snapshots the station list so later builder mutations do not affect this run.
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
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _route.Add(new StationPlan(new ServiceStationPlan(stationName, handler)));
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
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", nameof(stationName));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _route.Add(new StationPlan(new ServiceStationPlan(stationName, handler)));
            return this;
        }

        private const string StationExceptionCode = "STATION_EXCEPTION";
        private const string ServiceStationExceptionCode = "SERVICE_STATION_EXCEPTION";

        /// <summary>
        /// Executes all route hops synchronously and returns the final report.
        /// Regular stations run after green; service stations run after red; otherwise the hop is skipped.
        /// </summary>
        private RouteReport TravelCore(CancellationToken cancellationToken, bool recordVisits)
        {
            if (_straightTravel != null && !cancellationToken.CanBeCanceled)
            {
                return _straightTravel(recordVisits);
            }

            var route = new List<StationPlan>(_route);
            var current = new CargoManifest();
            var visits = recordVisits ? new List<StationVisit>(route.Count) : null;
            Signal previous = RailwaySignals.Green();
            var canCancel = cancellationToken.CanBeCanceled;

            for (var i = 0; i < route.Count; i++)
            {
                if (canCancel)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var plan = route[i];
                if (!ShouldEnterHop(plan, previous))
                {
                    continue;
                }

                var signal = plan.IsServiceStation
                    ? ExecuteServiceStation(plan, (RedSignal)previous, ref current, cancellationToken)
                    : ExecuteStation(plan, ref current, cancellationToken);
                previous = RecordHop(signal, plan.StationName, visits);
            }

            return new RouteReport(
                visits ?? (IReadOnlyList<StationVisit>)Array.Empty<StationVisit>(),
                previous.IsGreen ? RailwaySignals.Green() : previous,
                current);
        }

        /// <summary>
        /// Executes all route hops asynchronously and returns the final report.
        /// Regular stations run after green; service stations run after red; otherwise the hop is skipped.
        /// </summary>
        private async Task<RouteReport> TravelCoreAsync(CancellationToken cancellationToken, bool recordVisits)
        {
            var route = new List<StationPlan>(_route);
            var manifest = new ManifestHolder(new CargoManifest());
            var visits = recordVisits ? new List<StationVisit>(route.Count) : null;
            Signal previous = RailwaySignals.Green();
            var canCancel = cancellationToken.CanBeCanceled;

            for (var i = 0; i < route.Count; i++)
            {
                if (canCancel)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var plan = route[i];
                if (!ShouldEnterHop(plan, previous))
                {
                    continue;
                }

                var signal = plan.IsServiceStation
                    ? await ExecuteServiceStationAsync(
                        plan,
                        (RedSignal)previous,
                        manifest,
                        cancellationToken).ConfigureAwait(false)
                    : await ExecuteStationAsync(plan, manifest, cancellationToken).ConfigureAwait(false);
                previous = RecordHop(signal, plan.StationName, visits);
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
        /// Invokes one synchronous service-station hop for the current red signal.
        /// </summary>
        private static Signal ExecuteServiceStation(
            StationPlan plan,
            RedSignal redSignal,
            ref CargoManifest current,
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
                return servicePlan.SyncHandler(redSignal, current, cancellationToken) ?? redSignal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapServiceStationException(servicePlan, exception);
            }
        }

        /// <summary>
        /// Invokes one service-station hop for the current red signal.
        /// </summary>
        private static async Task<Signal> ExecuteServiceStationAsync(
            StationPlan plan,
            RedSignal redSignal,
            ManifestHolder manifest,
            CancellationToken cancellationToken)
        {
            var servicePlan = plan.ServicePlan;
            try
            {
                Signal handled;
                if (servicePlan.AsyncHandler != null)
                {
                    handled = await servicePlan.AsyncHandler(redSignal, manifest.Current, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    handled = servicePlan.SyncHandler(redSignal, manifest.Current, cancellationToken);
                }

                return handled ?? redSignal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return WrapServiceStationException(servicePlan, exception);
            }
        }

        /// <summary>
        /// Normalizes the hop signal, optionally records a visit, and returns the signal that becomes "previous" for the next hop.
        /// </summary>
        private static Signal RecordHop(
            Signal signal,
            string stationName,
            List<StationVisit> visits)
        {
            EnsureStationSignal(signal, stationName);
            signal = NormalizeRequestSignal(signal, stationName);
            if (visits != null)
            {
                visits.Add(new StationVisit(stationName, signal.IsGreen));
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
                var issue = new SignalIssue(failure.Code, failure.Message, stationName);
                return RailwaySignals.Red(issue, failure.PriorIssues);
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

        /// <summary>
        /// Converts an unhandled service-station exception into a red signal.
        /// </summary>
        private static Signal WrapServiceStationException(
            ServiceStationPlan serviceStation,
            Exception exception)
        {
            var issue = new SignalIssue(
                ServiceStationExceptionCode,
                $"Unhandled service station exception: {exception.Message}",
                serviceStation.StationName,
                exception);
            return RailwaySignals.Red(issue);
        }
    }
}

