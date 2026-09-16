using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds stable keys that group handler bindings sharing one generated extension signature.
    /// </summary>
    internal static class HandlerSignatureGrouping
    {
        /// <summary>
        /// Builds a stable grouping key for handler schemas that share the same generated extension signature.
        /// </summary>
        internal static string BuildGroupingKey(this StationHandlerBinding schema, string delegateTypeId)
        {
            var routeMethod = schema.ExtensionMethodName;
            if (schema.RequiresCustomDelegate())
            {
                return routeMethod + "|delegate|" + delegateTypeId;
            }

            return routeMethod + "|" + schema.BuildHandlerTypeName("unused");
        }
    }
}
