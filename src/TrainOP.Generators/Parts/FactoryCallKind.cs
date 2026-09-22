namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// How a <see cref="FactoryCall"/> resolves its upstream wagons.
    /// </summary>
    internal enum FactoryCallKind
    {
        /// <summary>
        /// Private/internal factory resolved inline (legacy MethodInvocation).
        /// </summary>
        Inline,

        /// <summary>
        /// Public/exported factory resolved via schema (legacy FactorySchema).
        /// </summary>
        Schema,
    }
}
