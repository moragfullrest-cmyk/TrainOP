namespace TrainOP
{
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
}
