namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Identifies the syntactic form of a route chain anchor.
    /// </summary>
    internal enum RouteChainAnchorKind
    {
        /// <summary>
        /// Anchor at <c>new TrainRoute()</c>.
        /// </summary>
        ObjectCreation,

        /// <summary>
        /// Anchor at a local variable assigned once from <c>new TrainRoute()</c>.
        /// </summary>
        LocalVariable,
    }
}
