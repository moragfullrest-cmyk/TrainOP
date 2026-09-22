namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Kind of connection between two <see cref="IRoutePart"/> instances in a chain constructor.
    /// </summary>
    internal enum PartEdgeKind
    {
        /// <summary>
        /// Origin seed bound to a local variable window.
        /// </summary>
        Bind,

        /// <summary>
        /// Linear append of a station (or service station) link.
        /// </summary>
        Append,

        /// <summary>
        /// Fork arms merged into a join seed.
        /// </summary>
        Join,

        /// <summary>
        /// Factory call extended by a continuation tail.
        /// </summary>
        Extend,
    }
}
