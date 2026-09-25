using System.Collections.Generic;
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
            return schema.HasRefWagons || schema.HasRefReadonlyWagons || schema.Input.HasTrailingParams || schema.IsAsync;
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
        /// Emits a custom delegate declaration when Func/Action is insufficient.
        /// </summary>
        internal static void EmitCustomDelegateDeclaration(
            this StationHandlerBinding schema,
            CodegenWriter writer,
            string delegateName)
        {
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    writer.AppendIndented("public delegate System.Threading.Tasks.Task ").Append(delegateName).Append("(");
                }
                else
                {
                    writer.AppendIndented("public delegate System.Threading.Tasks.Task<")
                        .Append(HandlerFuncTypeResolver.ResolveCanonicalFuncReturnType(schema))
                        .Append("> ")
                        .Append(delegateName)
                        .Append("(");
                }
            }
            else if (schema.ReturnShape.IsVoid)
            {
                writer.AppendIndented("public delegate void ").Append(delegateName).Append("(");
            }
            else
            {
                writer.AppendIndented("public delegate ")
                    .Append(HandlerFuncTypeResolver.ResolveCanonicalFuncReturnType(schema))
                    .Append(" ")
                    .Append(delegateName)
                    .Append("(");
            }

            schema.Input.EmitDelegateParameters(writer, useNeutralParameterNames: true);
            writer.Append(");");
            writer.EndLine();
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
                var typeDisplay = slot.Kind switch
                {
                    HandlerInputKind.Wagon => slot.Wagon.GetDeclarationModifier(emitParams: i == callOrder.Length - 1)
                        + slot.Wagon.TypeDisplay,
                    HandlerInputKind.RedSignal => ReturnTypeDisplayHelper.RedSignalReturnTypeDisplay,
                    HandlerInputKind.SignalIssue => ReturnTypeDisplayHelper.SignalIssueReturnTypeDisplay,
                    HandlerInputKind.SignalIssues => ReturnTypeDisplayHelper.SignalIssuesListReturnTypeDisplay,
                    HandlerInputKind.CargoManifest => ReturnTypeDisplayHelper.CargoManifestReturnTypeDisplay,
                    HandlerInputKind.CancellationToken => "CancellationToken",
                    _ => null
                };

                if (typeDisplay != null)
                {
                    parameters.Add(typeDisplay);
                }
            }
        }

        private static void EmitDelegateParameters(
            this HandlerInputParameters input,
            CodegenWriter writer,
            bool useNeutralParameterNames)
        {
            var needsComma = false;
            var callOrder = input.CallOrder;
            for (var i = 0; i < callOrder.Length; i++)
            {
                if (needsComma)
                {
                    writer.Append(", ");
                }

                var slot = callOrder[i];
                writer.Append(slot.Kind switch
                {
                    HandlerInputKind.Wagon => slot.Wagon.GetDeclarationModifier(emitParams: i == callOrder.Length - 1)
                        + slot.Wagon.TypeDisplay
                        + " "
                        + (useNeutralParameterNames ? "p" + slot.WagonIndex : slot.Wagon.Name),
                    HandlerInputKind.RedSignal => "RedSignal " + (useNeutralParameterNames ? "pRed" : "red"),
                    HandlerInputKind.SignalIssue => "SignalIssue " + (useNeutralParameterNames ? "pIssue" : "issue"),
                    HandlerInputKind.SignalIssues =>
                        "global::System.Collections.Generic.IReadOnlyList<SignalIssue> "
                        + (useNeutralParameterNames ? "pIssues" : "issues"),
                    HandlerInputKind.CargoManifest =>
                        "CargoManifest " + (useNeutralParameterNames ? "pManifest" : "manifest"),
                    HandlerInputKind.CancellationToken =>
                        "CancellationToken " + (useNeutralParameterNames ? "pToken" : "cancellationToken"),
                    _ => string.Empty
                });

                needsComma = true;
            }
        }
    }
}
