using System.Text;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves Func/Action return type displays for generated handler signatures.
    /// </summary>
    internal static class HandlerFuncTypeResolver
    {
        /// <summary>
        /// Resolves the return type segment of a generated Func or Action type.
        /// </summary>
        internal static string ResolveFuncReturnType(StationHandlerBinding schema)
        {
            if (schema.ReturnShape.IsVoid)
            {
                return "void";
            }

            if (schema.ReturnShape.IsExplicitSignalReturn)
            {
                return ReturnTypeDisplayHelper.SignalReturnTypeDisplay;
            }

            if (schema.ReturnShape.UseGenericReturn || schema.ReturnShape.IsUnknown)
            {
                return "global::System.Object";
            }

            if (!string.IsNullOrWhiteSpace(schema.ReturnShape.ReturnTypeDisplay))
            {
                return schema.ReturnShape.ReturnTypeDisplay;
            }

            return "global::System.Object";
        }

        /// <summary>
        /// Resolves a canonical return type display for generated handler signatures.
        /// </summary>
        internal static string ResolveCanonicalFuncReturnType(StationHandlerBinding schema)
        {
            if (schema.ReturnShape.IsValueTuple)
            {
                var tupleDisplay = ResolveCanonicalTupleReturnTypeDisplay(schema.ReturnShape);
                if (!string.IsNullOrWhiteSpace(tupleDisplay))
                {
                    return tupleDisplay;
                }
            }

            return ResolveFuncReturnType(schema);
        }

        /// <summary>
        /// Resolves a canonical tuple return type display from return shape members.
        /// </summary>
        internal static string ResolveCanonicalTupleReturnTypeDisplay(ReturnShape returnShape)
        {
            if (!returnShape.IsValueTuple || returnShape.Members.IsDefaultOrEmpty)
            {
                return returnShape.ReturnTypeDisplay;
            }

            var builder = new StringBuilder();
            builder.Append('(');
            for (var i = 0; i < returnShape.Members.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(returnShape.Members[i].TypeDisplay);
            }

            builder.Append(')');
            return builder.ToString();
        }
    }
}
