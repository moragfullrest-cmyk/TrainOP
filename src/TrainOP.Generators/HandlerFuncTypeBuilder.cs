using System.Collections.Generic;
using System.Text;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds Func and Action type strings for generated station handler extension methods.
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

            if (UsesAsyncFuncCancellationHandler(schema))
            {
                return "Func<CancellationToken, System.Threading.Tasks.Task>";
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
                if (schema.IsAsync)
                {
                    if (parameters.Count == 0)
                    {
                        return "Func<System.Threading.Tasks.Task>";
                    }

                    parameters.Add("System.Threading.Tasks.Task");
                    return "Func<" + string.Join(", ", parameters) + ">";
                }

                if (parameters.Count == 0)
                {
                    return "Action";
                }

                return "Action<" + string.Join(", ", parameters) + ">";
            }

            var returnType = ResolveCanonicalFuncReturnType(schema);
            if (schema.IsAsync)
            {
                if (parameters.Count == 0)
                {
                    return "Func<System.Threading.Tasks.Task<" + returnType + ">>";
                }

                parameters.Add("System.Threading.Tasks.Task<" + returnType + ">");
            }
            else
            {
                if (parameters.Count == 0)
                {
                    return "Func<" + returnType + ">";
                }

                parameters.Add(returnType);
            }

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

        /// <summary>
        /// Builds a stable emission key for grouping schemas that share the same generated extension signature.
        /// </summary>
        public static string BuildEmissionKey(StationHandlerBinding schema, string delegateTypeId)
        {
            return BuildGroupingKey(schema, delegateTypeId);
        }

        private static bool UsesActionCancellationHandler(StationHandlerBinding schema)
        {
            return !schema.IsServiceStation
                && !schema.IsAsync
                && schema.ReturnShape.IsVoid
                && schema.HasCancellationToken
                && schema.Wagons.Length == 0;
        }

        private static bool UsesAsyncFuncCancellationHandler(StationHandlerBinding schema)
        {
            return !schema.IsServiceStation
                && schema.IsAsync
                && schema.ReturnShape.IsVoid
                && schema.HasCancellationToken
                && schema.Wagons.Length == 0;
        }

        private static void AppendHandlerParameterTypes(StationHandlerBinding schema, List<string> parameters)
        {
            if (schema.IsServiceStation)
            {
                AppendWagonParameterTypes(schema, parameters);

                if (schema.IncludeRedSignal)
                {
                    parameters.Add("global::TrainOP.RedSignal");
                }
            }
            else
            {
                if (schema.IncludeRedSignal)
                {
                    parameters.Add("global::TrainOP.RedSignal");
                }

                if (schema.IncludeSignalIssue)
                {
                    parameters.Add("global::TrainOP.SignalIssue");
                }

                if (schema.IncludeManifest)
                {
                    parameters.Add("global::TrainOP.CargoManifest");
                }

                AppendWagonParameterTypes(schema, parameters);
            }

            if (schema.HasCancellationToken)
            {
                parameters.Add("CancellationToken");
            }
        }

        private static void AppendWagonParameterTypes(StationHandlerBinding schema, List<string> parameters)
        {
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                var wagon = schema.Wagons[i];
                var typeDisplay = wagon.TypeDisplay;
                if (wagon.IsByReference)
                {
                    typeDisplay = "ref " + typeDisplay;
                }

                parameters.Add(typeDisplay);
            }
        }

        private static void EmitDelegateParameters(StringBuilder source, StationHandlerBinding schema, bool useNeutralParameterNames)
        {
            var needsComma = false;
            if (schema.IsServiceStation)
            {
                EmitWagonDelegateParameters(source, schema, useNeutralParameterNames, ref needsComma);

                if (schema.IncludeRedSignal)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append("RedSignal ").Append(useNeutralParameterNames ? "pRed" : "red");
                    needsComma = true;
                }
            }
            else
            {
                if (schema.IncludeRedSignal)
                {
                    source.Append("RedSignal ").Append(useNeutralParameterNames ? "pRed" : "red");
                    needsComma = true;
                }

                if (schema.IncludeSignalIssue)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append("SignalIssue ").Append(useNeutralParameterNames ? "pIssue" : "issue");
                    needsComma = true;
                }

                if (schema.IncludeManifest)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append("CargoManifest ").Append(useNeutralParameterNames ? "pManifest" : "manifest");
                    needsComma = true;
                }

                EmitWagonDelegateParameters(source, schema, useNeutralParameterNames, ref needsComma);
            }

            if (schema.HasCancellationToken)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                source.Append("CancellationToken ").Append(useNeutralParameterNames ? "pToken" : "cancellationToken");
            }
        }

        private static void EmitWagonDelegateParameters(
            StringBuilder source,
            StationHandlerBinding schema,
            bool useNeutralParameterNames,
            ref bool needsComma)
        {
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                var wagon = schema.Wagons[i];
                if (wagon.IsByReference)
                {
                    source.Append("ref ");
                }

                var parameterName = useNeutralParameterNames ? "p" + i : wagon.Name;
                source.Append(wagon.TypeDisplay).Append(" ").Append(parameterName);
                needsComma = true;
            }
        }
    }
}
