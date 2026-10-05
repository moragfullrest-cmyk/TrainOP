using System;
using System.Collections.Generic;

namespace TrainOP
{
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
}
