namespace TrainOP
{
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
}
