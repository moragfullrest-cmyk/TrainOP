namespace TrainOP.Generators.Route
{
    /// <summary>
    /// Distinguishes anchor roots from station handler call sites in a route graph.
    /// </summary>
    internal enum RouteSiteKind
    {
        /// <summary>
        /// Chain root: <c>new TrainRoute()</c>, local variable, or factory invocation.
        /// </summary>
        Anchor,

        /// <summary>
        /// Data-oriented <c>.Station(...)</c> call site.
        /// </summary>
        Station,

        /// <summary>
        /// Data-oriented <c>.ServiceStation(...)</c> call site.
        /// </summary>
        ServiceStation,
    }
}
