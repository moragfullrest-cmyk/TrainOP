namespace TrainOP.Generators.Route
{
    /// <summary>
    /// Legacy syntactic discriminator for <see cref="RouteChainAnchor"/> (adapter stamp only).
    /// </summary>
    /// <remarks>
    /// Prefer first-class Parts (<c>CreationSeed</c>, <c>FactoryCall</c>, <c>LocalBinding</c>,
    /// <c>JoinSeed</c>) and their ports for new logic. Hot paths must not switch on this enum;
    /// it remains the downward stamp on <see cref="RouteChainAnchor"/> / <see cref="RouteSite"/>.
    /// </remarks>
    internal enum RouteChainAnchorKind
    {
        /// <summary>
        /// Anchor at <c>new TrainRoute()</c> (→ <c>CreationSeed</c>).
        /// </summary>
        ObjectCreation,

        /// <summary>
        /// Anchor at a local variable assigned from a TrainRoute origin (→ <c>LocalBinding</c>).
        /// </summary>
        LocalVariable,

        /// <summary>
        /// Synthetic anchor after a forking receiver join (→ <c>JoinSeed</c>).
        /// </summary>
        BranchJoin,

        /// <summary>
        /// Private/internal factory invocation resolved inline (→ <c>FactoryCall</c> Inline).
        /// </summary>
        MethodInvocation,

        /// <summary>
        /// Public/exported factory invocation resolved via schema (→ <c>FactoryCall</c> Schema).
        /// </summary>
        FactorySchema,
    }
}
