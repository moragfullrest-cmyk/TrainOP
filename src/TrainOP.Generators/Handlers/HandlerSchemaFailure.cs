namespace TrainOP.Generators.Handlers
{
    /// <summary>
    /// Reason why a handler schema could not be resolved from syntax.
    /// </summary>
    internal enum HandlerSchemaFailure
    {
        None = 0,
        InvalidShape,
        NotTrainRouteReceiver,
        BuiltinHandler,
        BuiltinServiceHandler,
        UnresolvedHandler,
        InvalidSchema
    }
}
