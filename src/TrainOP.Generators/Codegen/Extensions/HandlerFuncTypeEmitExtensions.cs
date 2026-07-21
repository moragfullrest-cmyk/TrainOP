using System.Collections.Generic;
using System.Text;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    internal static class HandlerFuncTypeEmitExtensions
    {
        /// <summary>
        /// Determines whether this handler must use a custom delegate type.
        /// </summary>
        internal static bool RequiresCustomDelegate(this StationHandlerBinding schema)
        {
            return schema.HasRefWagons || schema.IsAsync;
        }

        /// <summary>
        /// Resolves the handler type used by a generated route extension method.
        /// </summary>
        internal static string BuildHandlerTypeName(this StationHandlerBinding schema, string customDelegateName)
        {
            if (schema.RequiresCustomDelegate())
            {
                return customDelegateName;
            }

            if (schema.UsesActionCancellationHandler())
            {
                return "Action<CancellationToken>";
            }

            return schema.BuildFuncOrActionTypeName();
        }

        /// <summary>
        /// Builds a Func or Action type with concrete return types for this handler.
        /// </summary>
        internal static string BuildFuncOrActionTypeName(this StationHandlerBinding schema)
        {
            var parameters = new List<string>();
            schema.Input.AppendHandlerParameterTypes(parameters);

            if (schema.ReturnShape.IsVoid)
            {
                if (parameters.Count == 0)
                {
                    return "Action";
                }

                return "Action<" + string.Join(", ", parameters) + ">";
            }

            var returnType = HandlerFuncTypeResolver.ResolveCanonicalFuncReturnType(schema);
            if (parameters.Count == 0)
            {
                return "Func<" + returnType + ">";
            }

            parameters.Add(returnType);
            return "Func<" + string.Join(", ", parameters) + ">";
        }

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

        /// <summary>
        /// Emits a custom delegate declaration when Func/Action is insufficient.
        /// </summary>
        internal static void EmitCustomDelegateDeclaration(
            this StationHandlerBinding schema,
            StringBuilder source,
            string delegateName)
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
                        .Append(HandlerFuncTypeResolver.ResolveCanonicalFuncReturnType(schema))
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
                    .Append(HandlerFuncTypeResolver.ResolveCanonicalFuncReturnType(schema))
                    .Append(" ")
                    .Append(delegateName)
                    .Append("(");
            }

            schema.Input.EmitDelegateParameters(source, useNeutralParameterNames: true);
            source.AppendLine(");");
        }

        private static bool UsesActionCancellationHandler(this StationHandlerBinding schema)
        {
            return !schema.IsServiceStation
                && !schema.IsAsync
                && schema.ReturnShape.IsVoid
                && schema.HasCancellationToken
                && schema.Wagons.Length == 0;
        }

        private static void AppendHandlerParameterTypes(this HandlerInputParameters input, List<string> parameters)
        {
            var callOrder = input.CallOrder;
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

        private static void EmitDelegateParameters(
            this HandlerInputParameters input,
            StringBuilder source,
            bool useNeutralParameterNames)
        {
            var needsComma = false;
            var callOrder = input.CallOrder;
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
