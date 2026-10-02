using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    /// Read-only snapshot of wagon keys. Values are the same references as the trip manifest.
    /// <see cref="InspectWagons"/> is a wrapper, not the live <see cref="Dictionary{TKey,TValue}"/>.
    /// </summary>
    public sealed class ReadOnlyManifest
    {
        private readonly Dictionary<string, object> _wagons;
        private readonly ReadOnlyDictionary<string, object> _view;

        /// <summary>
        /// Copies keys from <paramref name="source"/>. Reference values stay shared.
        /// </summary>
        public ReadOnlyManifest(CargoManifest source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _wagons = new Dictionary<string, object>();
            foreach (var pair in source.InspectWagons())
            {
                _wagons[pair.Key] = pair.Value;
            }

            _view = new ReadOnlyDictionary<string, object>(_wagons);
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
                throw new KeyNotFoundException($"Wagon '{wagonName}' was not found in the manifest.");
            }

            return CargoManifest.CastWagonValue<T>(wagonName, value);
        }

        /// <summary>
        /// Returns a read-only view that cannot be cast to <see cref="Dictionary{TKey,TValue}"/>.
        /// </summary>
        public IReadOnlyDictionary<string, object> InspectWagons()
        {
            return _view;
        }
    }

    /// <summary>
    /// Describes a red signal reason.
    /// </summary>
    public sealed class SignalIssue
    {
        /// <summary>
        /// Creates a signal issue. A null <paramref name="details"/> dictionary is stored as empty.
        /// </summary>
        public SignalIssue(
            string code,
            string message,
            string stationName,
            Exception exception = null,
            IReadOnlyDictionary<string, object> details = null)
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
            Details = CopyDetails(details);
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

        /// <summary>
        /// Gets application data attached to this issue. Empty when the station supplied none.
        /// </summary>
        public IReadOnlyDictionary<string, object> Details { get; }

        /// <summary>
        /// Returns a copy whose station name is <paramref name="stationName"/> when this issue has none.
        /// An existing name and <see cref="Exception"/> stay as they are.
        /// </summary>
        internal SignalIssue WithStationNameIfEmpty(string stationName)
        {
            if (!string.IsNullOrEmpty(StationName))
            {
                return this;
            }

            return new SignalIssue(Code, Message, stationName, Exception, Details);
        }

        internal static IReadOnlyDictionary<string, object> CopyDetails(IReadOnlyDictionary<string, object> details)
        {
            if (details == null || details.Count == 0)
            {
                return EmptyDetails;
            }

            var copy = new Dictionary<string, object>(details.Count);
            foreach (var pair in details)
            {
                copy.Add(pair.Key, pair.Value);
            }

            return copy;
        }

        private static readonly IReadOnlyDictionary<string, object> EmptyDetails =
            new Dictionary<string, object>();
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
        /// Creates a red signal with the issues of one stop, in the given order.
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

            Issue = _issues[0];
        }

        /// <summary>
        /// Gets the first issue of this stop. <see cref="RouteReport.FailureCode"/> reads the same entry.
        /// </summary>
        public SignalIssue Issue { get; }

        /// <summary>
        /// Gets the issues of this stop, in the order they were supplied.
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
        /// Creates a red signal request for data-oriented handlers.
        /// The station name is filled in by generated adapters.
        /// </summary>
        public static RedFailure Red(string code, string message)
        {
            return new RedFailure(code, message);
        }

        /// <summary>
        /// Creates a red signal request with one issue and the supplied details.
        /// A null <paramref name="details"/> dictionary is stored as empty.
        /// The station name is filled in by generated adapters.
        /// </summary>
        public static RedFailure Red(string code, string message, IReadOnlyDictionary<string, object> details)
        {
            return new RedFailure(code, message, details);
        }

        /// <summary>
        /// Creates a red signal request for one or more issues of the same stop, in array order.
        /// Not <c>params</c>: a single <see cref="SignalIssue"/> selects <see cref="Red(SignalIssue)"/>.
        /// An empty array throws <see cref="ArgumentException"/>.
        /// </summary>
        public static RedFailure Red(SignalIssue[] issues)
        {
            return new RedFailure(issues);
        }
    }

    /// <summary>
    /// Outcome of one plan step in the visit journal.
    /// </summary>
    public enum HopOutcome
    {
        /// <summary>
        /// The station ran and returned green.
        /// </summary>
        Green = 0,

        /// <summary>
        /// The station ran and returned lunar white. The route still continues.
        /// </summary>
        White = 1,

        /// <summary>
        /// The station ran and returned red.
        /// </summary>
        Red = 2,

        /// <summary>
        /// The plan bypassed this step. Written only when the journal is enabled.
        /// </summary>
        Skipped = 3,
    }

    /// <summary>
    /// One plan step in the visit journal: name, outcome, plan index, and elapsed time.
    /// Failure details stay on <see cref="RouteReport.TerminalSignal"/>.
    /// </summary>
    public readonly struct StationVisit
    {
        /// <summary>
        /// Creates a green or red visit at index 0 with zero elapsed time.
        /// </summary>
        public StationVisit(string stationName, bool isGreen)
            : this(stationName, isGreen ? HopOutcome.Green : HopOutcome.Red, 0, TimeSpan.Zero)
        {
        }

        /// <summary>
        /// Creates a visit for one plan step.
        /// </summary>
        public StationVisit(string stationName, HopOutcome outcome, int index, TimeSpan elapsed)
        {
            StationName = stationName ?? throw new ArgumentNullException(nameof(stationName));
            if (!Enum.IsDefined(typeof(HopOutcome), outcome))
            {
                throw new ArgumentOutOfRangeException(nameof(outcome));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }

            Outcome = outcome;
            Index = index;
            Elapsed = elapsed;
        }

        /// <summary>
        /// Elapsed time from <paramref name="startedTimestamp"/>, taken with <see cref="Stopwatch.GetTimestamp"/>.
        /// A non-positive delta is <see cref="TimeSpan.Zero"/>.
        /// </summary>
        public static TimeSpan ElapsedSince(long startedTimestamp)
        {
            var delta = Stopwatch.GetTimestamp() - startedTimestamp;
            if (delta <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromTicks((long)(delta * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));
        }

        /// <summary>
        /// Gets the station name.
        /// </summary>
        public string StationName { get; }

        /// <summary>
        /// Gets how this plan step ended.
        /// </summary>
        public HopOutcome Outcome { get; }

        /// <summary>
        /// Gets the step index in the plan. Duplicate names stay distinguishable by this index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets how long the step ran. A skipped step is <see cref="TimeSpan.Zero"/>.
        /// </summary>
        public TimeSpan Elapsed { get; }

        /// <summary>
        /// Gets whether the route continued after this step.
        /// True for <see cref="HopOutcome.Green"/> and <see cref="HopOutcome.White"/>.
        /// </summary>
        public bool IsGreen => Outcome == HopOutcome.Green || Outcome == HopOutcome.White;
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
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            Manifest = new ReadOnlyManifest(manifest);
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
        /// Gets the terminal wagon snapshot for this run. Keys cannot be loaded or unloaded here.
        /// </summary>
        public ReadOnlyManifest Manifest { get; }

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
        /// Gets the issues of the terminal red signal, in stop order; otherwise empty.
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

        /// <summary>
        /// Tries to read a typed terminal wagon. Returns false when the name is missing.
        /// An empty name throws. A present value of the wrong type throws <see cref="InvalidCastException"/>.
        /// </summary>
        public bool TryGet<T>(string wagonName, out T value)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            if (!Manifest.TryGetWagon(wagonName, out var cargo))
            {
                value = default;
                return false;
            }

            value = CargoManifest.CastWagonValue<T>(wagonName, cargo);
            return true;
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

