using System;
using System.Diagnostics;

namespace TrainOP
{
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
}
