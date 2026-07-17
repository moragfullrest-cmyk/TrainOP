using System.Collections.Generic;
using System.Text;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds Func and Action type strings for generated station handler extension methods.
    /// Parameter order comes from <see cref="HandlerInputParameters.CallOrder"/>.
    /// </summary>
    internal static class HandlerFuncTypeBuilder
    {
        /// <summary>
        /// Determines whether a handler must use a custom delegate.
        /// Ref parameters are unsupported by Func/Action; async handlers use custom delegates
        /// to avoid overload ambiguity with sync Func counterparts under a single Station name.
        /// </summary>
        public static bool RequiresCustomDelegate(StationHandlerBinding schema)
        {
            return schema.HasRefWagons || schema.IsAsync;
        }

        /// <summary>
        /// Resolves the handler type used by a generated route extension method.
        /// </summary>
        public static string BuildHandlerTypeName(StationHandlerBinding schema, string customDelegateName)
        {
            if (RequiresCustomDelegate(schema))
            {
                return customDelegateName;
            }

            if (UsesActionCancellationHandler(schema))
            {
                return "Action<CancellationToken>";
            }

            return BuildFuncOrActionTypeName(schema);
        }

        /// <summary>
        /// Builds a Func or Action type with concrete return types for tuple, object, and signal handler groups.
        /// </summary>
        public static string BuildFuncOrActionTypeName(StationHandlerBinding schema)
        {
            var parameters = new List<string>();
            AppendHandlerParameterTypes(schema, parameters);

            if (schema.ReturnShape.IsVoid)
            {
                if (parameters.Count == 0)
                {
                    return "Action";
                }

                return "Action<" + string.Join(", ", parameters) + ">";
            }

            var returnType = ResolveCanonicalFuncReturnType(schema);
            if (parameters.Count == 0)
            {
                return "Func<" + returnType + ">";
            }

            parameters.Add(returnType);
            return "Func<" + string.Join(", ", parameters) + ">";
        }

        /// <summary>
        /// Builds a custom delegate declaration for handlers that require ref parameters.
        /// </summary>
        public static void EmitCustomDelegateDeclaration(StringBuilder source, StationHandlerBinding schema, string delegateName)
        {
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("        public delegate System.Threading.Tasks.Task ").Append(delegateName).Append("(");
                }
                else
                {
                    source.Append("        public delegate System.Threading.Tasks.Task<")
                        .Append(ResolveCanonicalFuncReturnType(schema))
                        .Append("> ")
                        .Append(delegateName)
                        .Append("(");
                }
            }
            else if (schema.ReturnShape.IsVoid)
            {
                source.Append("        public delegate void ").Append(delegateName).Append("(");
            }
            else
            {
                source.Append("        public delegate ")
                    .Append(ResolveCanonicalFuncReturnType(schema))
                    .Append(" ")
                    .Append(delegateName)
                    .Append("(");
            }

            EmitDelegateParameters(source, schema, useNeutralParameterNames: true);
            source.AppendLine(");");
        }

        /// <summary>
        /// Resolves the return type segment of a generated Func or Action type.
        /// </summary>
        public static string ResolveFuncReturnType(StationHandlerBinding schema)
        {
            if (schema.ReturnShape.IsVoid)
            {
                return "void";
            }

            if (schema.ReturnShape.IsExplicitSignalReturn)
            {
                return ReturnTypeDisplayBuilder.SignalReturnTypeDisplay;
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
        public static string ResolveCanonicalFuncReturnType(StationHandlerBinding schema)
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
        public static string ResolveCanonicalTupleReturnTypeDisplay(ReturnShape returnShape)
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

        /// <summary>
        /// Builds a stable grouping key for handler schemas that share the same generated extension signature.
        /// </summary>
        public static string BuildGroupingKey(StationHandlerBinding schema, string delegateTypeId)
        {
            var routeMethod = schema.ExtensionMethodName;
            if (RequiresCustomDelegate(schema))
            {
                return routeMethod + "|delegate|" + delegateTypeId;
            }

            return routeMethod + "|" + BuildHandlerTypeName(schema, "unused");
        }

        private static bool UsesActionCancellationHandler(StationHandlerBinding schema)
        {
            return !schema.IsServiceStation
                && !schema.IsAsync
                && schema.ReturnShape.IsVoid
                && schema.HasCancellationToken
                && schema.Wagons.Length == 0;
        }

        private static void AppendHandlerParameterTypes(StationHandlerBinding schema, List<string> parameters)
        {
            var callOrder = schema.Input.CallOrder;
            for (var i = 0; i < callOrder.Length; i++)
            {
                var slot = callOrder[i];
                switch (slot.Kind)
                {
                    case HandlerInputKind.Wagon:
                        var wagon = slot.Wagon;
                        var typeDisplay = wagon.TypeDisplay;
                        if (wagon.IsByReference)
                        {
                            typeDisplay = "ref " + typeDisplay;
                        }

                        parameters.Add(typeDisplay);
                        break;
                    case HandlerInputKind.RedSignal:
                        parameters.Add("global::TrainOP.RedSignal");
                        break;
                    case HandlerInputKind.SignalIssue:
                        parameters.Add("global::TrainOP.SignalIssue");
                        break;
                    case HandlerInputKind.CargoManifest:
                        parameters.Add("global::TrainOP.CargoManifest");
                        break;
                    case HandlerInputKind.CancellationToken:
                        parameters.Add("CancellationToken");
                        break;
                }
            }
        }

        private static void EmitDelegateParameters(StringBuilder source, StationHandlerBinding schema, bool useNeutralParameterNames)
        {
            var needsComma = false;
            var callOrder = schema.Input.CallOrder;
            for (var i = 0; i < callOrder.Length; i++)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                var slot = callOrder[i];
                switch (slot.Kind)
                {
                    case HandlerInputKind.Wagon:
                        var wagon = slot.Wagon;
                        if (wagon.IsByReference)
                        {
                            source.Append("ref ");
                        }

                        var parameterName = useNeutralParameterNames ? "p" + slot.WagonIndex : wagon.Name;
                        source.Append(wagon.TypeDisplay).Append(" ").Append(parameterName);
                        break;
                    case HandlerInputKind.RedSignal:
                        source.Append("RedSignal ").Append(useNeutralParameterNames ? "pRed" : "red");
                        break;
                    case HandlerInputKind.SignalIssue:
                        source.Append("SignalIssue ").Append(useNeutralParameterNames ? "pIssue" : "issue");
                        break;
                    case HandlerInputKind.CargoManifest:
                        source.Append("CargoManifest ").Append(useNeutralParameterNames ? "pManifest" : "manifest");
                        break;
                    case HandlerInputKind.CancellationToken:
                        source.Append("CancellationToken ").Append(useNeutralParameterNames ? "pToken" : "cancellationToken");
                        break;
                }

                needsComma = true;
            }
        }
    }
}
