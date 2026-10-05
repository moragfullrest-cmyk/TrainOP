namespace TrainOP
{
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
}
