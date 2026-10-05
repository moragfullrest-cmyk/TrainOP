using System;
using System.Collections.Generic;

namespace TrainOP
{
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
}
