namespace TrainOP.Generators.Handlers
{
    /// <summary>
    /// Classifies the source form of a data-oriented station handler argument.
    /// </summary>
    internal enum HandlerKind
    {
        Lambda,
        AnonymousMethod,
        MethodGroup,
    }
}
