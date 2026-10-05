using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TrainOP
{
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
}
