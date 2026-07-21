namespace TrainOP.Generators
{
    /// <summary>
    /// How wagon values are pulled from the manifest inside a generated adapter body.
    /// </summary>
    internal enum PullStrategy
    {
        /// <summary>Compile-time wagon names via literal pull statements.</summary>
        LiteralNames,

        /// <summary>Runtime <c>string[]</c> variable (typically <c>inputNames</c>).</summary>
        NameArray
    }
}
