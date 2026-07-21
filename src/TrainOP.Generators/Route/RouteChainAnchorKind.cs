namespace TrainOP.Generators.Route
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

        /// <summary>
        /// Synthetic anchor for a downstream chain after a forking receiver join.
        /// </summary>
        BranchJoin,

        /// <summary>
        /// Anchor at a private/internal factory invocation resolved inline.
        /// </summary>
        MethodInvocation,

        /// <summary>
        /// Anchor at a public/exported factory invocation resolved via exported schema.
        /// </summary>
        FactorySchema,
    }
}

