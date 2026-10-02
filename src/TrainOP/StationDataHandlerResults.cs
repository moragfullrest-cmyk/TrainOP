using System;
using System.Collections.Generic;

namespace TrainOP
{
    /// <summary>
    /// Green signal payload for data-oriented handlers: merged into the manifest by generated adapters.
    /// </summary>
    public sealed class GreenPayload<T> : IGreenPayload
    {
        /// <summary>
        /// Creates a green payload wrapper for the provided value.
        /// </summary>
        public GreenPayload(T value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the payload value to merge into the manifest.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Gets the payload value as an object for merge logic.
        /// </summary>
        object IGreenPayload.GetValue()
        {
            return Value;
        }
    }

    /// <summary>
    /// Internal contract for unwrapping green payload values during manifest merge.
    /// </summary>
    internal interface IGreenPayload
    {
        /// <summary>
        /// Gets the inner payload value.
        /// </summary>
        object GetValue();
    }

    /// <summary>
    /// Red signal request for data-oriented handlers: mapped to <see cref="RedSignal"/> by generated adapters.
    /// </summary>
    public sealed class RedFailure : Signal
    {
        /// <summary>
        /// Creates a red failure request with the provided code and message.
        /// </summary>
        public RedFailure(string code, string message)
            : this(code, message, null)
        {
        }

        /// <summary>
        /// Creates a red failure request with one issue and the supplied details.
        /// A null <paramref name="details"/> dictionary is stored as empty.
        /// </summary>
        public RedFailure(string code, string message, IReadOnlyDictionary<string, object> details)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Details = SignalIssue.CopyDetails(details);
        }

        /// <summary>
        /// Creates a red failure request for the issues of one stop, in array order.
        /// </summary>
        internal RedFailure(SignalIssue[] issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            if (issues.Length == 0)
            {
                throw new ArgumentException("At least one issue is required.", nameof(issues));
            }

            _issues = new SignalIssue[issues.Length];
            for (var i = 0; i < issues.Length; i++)
            {
                _issues[i] = issues[i] ?? throw new ArgumentException("Issue entries cannot be null.", nameof(issues));
            }

            Code = _issues[0].Code;
            Message = _issues[0].Message;
            Details = _issues[0].Details;
        }

        /// <summary>
        /// Gets whether the signal allows route continuation.
        /// </summary>
        public override bool IsGreen => false;

        /// <summary>
        /// Gets the failure code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the failure message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the details of the single-issue form, or the first issue when several were supplied.
        /// </summary>
        public IReadOnlyDictionary<string, object> Details { get; }

        private readonly SignalIssue[] _issues;

        /// <summary>
        /// Maps this request to a route red signal.
        /// Fills <paramref name="stationName"/> only on issues whose station name is empty.
        /// </summary>
        internal RedSignal ToRedSignal(string stationName)
        {
            if (_issues == null)
            {
                return new RedSignal(new SignalIssue(Code, Message, stationName, details: Details));
            }

            var stamped = new SignalIssue[_issues.Length];
            for (var i = 0; i < _issues.Length; i++)
            {
                stamped[i] = _issues[i].WithStationNameIfEmpty(stationName);
            }

            return new RedSignal(stamped);
        }
    }

    /// <summary>
    /// Lunar-white (pass-through) result: manifest is left unchanged and the route continues.
    /// </summary>
    public sealed class WhitePass : Signal
    {
        /// <summary>
        /// Gets the singleton lunar-white pass-through instance.
        /// </summary>
        public static WhitePass Instance { get; } = new WhitePass();

        /// <summary>
        /// Creates the singleton lunar-white pass-through instance.
        /// </summary>
        private WhitePass()
        {
        }

        /// <summary>
        /// Gets whether the signal allows route continuation.
        /// </summary>
        public override bool IsGreen => true;
    }
}
