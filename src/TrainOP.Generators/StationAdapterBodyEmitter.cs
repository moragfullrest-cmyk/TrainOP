using System.Text;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits the shared RegisterStation / ServiceStation adapter body:
    /// open lambda, pull wagons, invoke handler, optional ref locals, merge to signal.
    /// </summary>
    internal static class StationAdapterBodyEmitter
    {
        /// <summary>
        /// How wagon values are pulled from the manifest inside the adapter body.
        /// </summary>
        internal enum PullMode
        {
            /// <summary>Compile-time wagon names via <see cref="WagonBindingCodegen"/>.</summary>
            LiteralNames,

            /// <summary>Runtime <c>string[]</c> variable (typically <c>inputNames</c>).</summary>
            NameArray
        }

        /// <summary>
        /// Options that differ between canonical, chain-binding, and reflection adapters.
        /// </summary>
        internal sealed class Options
        {
            public PullMode Pull { get; set; } = PullMode.LiteralNames;

            /// <summary>When true, handler args use <c>wagon0</c>… and SignalIssue is <c>issue</c>.</summary>
            public bool UseNeutralWagonNames { get; set; }

            /// <summary>Expression for wagon name array passed to merge (field or local).</summary>
            public string WagonNamesExpression { get; set; }

            /// <summary>Expression for return member names, or null.</summary>
            public string ReturnMembersExpression { get; set; }

            /// <summary>Expression for ref flags array, or null when absent.</summary>
            public string RefFlagsExpression { get; set; }

            /// <summary>
            /// When true and schema has ref wagons, always pass <see cref="RefFlagsExpression"/>
            /// to service merge even if the expression is the hoisted binding field.
            /// </summary>
            public bool PassRefFlagsToServiceMergeWhenPresent { get; set; }

            /// <summary>Name-array variable for <see cref="PullMode.NameArray"/> (default <c>inputNames</c>).</summary>
            public string InputNamesVariable { get; set; } = "inputNames";

            /// <summary>Station label passed to route registration (default <c>stationName</c>).</summary>
            public string StationLabelExpression { get; set; } = "stationName";
        }

        /// <summary>
        /// Emits <c>return route.ServiceStation/RegisterStation(..., =&gt; { ... });</c> for one schema.
        /// </summary>
        public static void EmitRegistration(StringBuilder source, StationHandlerBinding schema, Options options)
        {
            EmitRegistrationOpen(source, schema, options.StationLabelExpression);
            EmitPull(source, schema, options);
            EmitHandlerInvocation(source, schema, options);
            EmitRefLocalValuesIfNeeded(source, schema, options.UseNeutralWagonNames);
            EmitReturnMerge(source, schema, options);
            source.AppendLine("            });");
        }

        /// <summary>
        /// Resolves the generated station return variable type.
        /// </summary>
        public static string GetStationReturnTypeDisplay(StationHandlerBinding schema)
        {
            if (schema.ReturnShape.IsVoid)
            {
                return "global::System.Object";
            }

            if (!string.IsNullOrWhiteSpace(schema.ReturnShape.ReturnTypeDisplay)
                && !schema.ReturnShape.UseGenericReturn
                && !schema.ReturnShape.IsUnknown)
            {
                return schema.ReturnShape.ReturnTypeDisplay;
            }

            return HandlerFuncTypeBuilder.ResolveCanonicalFuncReturnType(schema);
        }

        /// <summary>
        /// Emits call arguments in <see cref="HandlerInputParameters.CallOrder"/>.
        /// </summary>
        public static void EmitHandlerCallArguments(
            StringBuilder source,
            StationHandlerBinding schema,
            string tokenVariable,
            string redVariable,
            bool useNeutralWagonNames,
            string signalIssueExpression)
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
                        if (slot.Wagon.IsByReference)
                        {
                            source.Append("ref ");
                        }

                        if (useNeutralWagonNames)
                        {
                            source.Append("wagon").Append(slot.WagonIndex);
                        }
                        else
                        {
                            source.Append(slot.Wagon.Name);
                        }

                        break;
                    case HandlerInputKind.RedSignal:
                        source.Append(redVariable ?? "red");
                        break;
                    case HandlerInputKind.SignalIssue:
                        source.Append(signalIssueExpression);
                        break;
                    case HandlerInputKind.CargoManifest:
                        source.Append("manifest");
                        break;
                    case HandlerInputKind.CancellationToken:
                        source.Append(tokenVariable ?? "default");
                        break;
                }

                needsComma = true;
            }
        }

        private static void EmitRegistrationOpen(
            StringBuilder source,
            StationHandlerBinding schema,
            string stationLabelExpression)
        {
            if (schema.IsServiceStation)
            {
                if (schema.IsAsync)
                {
                    source.Append("            return route.ServiceStation(")
                        .Append(stationLabelExpression)
                        .AppendLine(", async (red, token) =>");
                }
                else
                {
                    source.Append("            return route.ServiceStation(")
                        .Append(stationLabelExpression)
                        .AppendLine(", (red, token) =>");
                }

                source.AppendLine("            {");
                source.AppendLine("                var manifest = red.Manifest;");
                return;
            }

            if (schema.IsAsync)
            {
                source.Append("            return route.RegisterStation(")
                    .Append(stationLabelExpression)
                    .AppendLine(", async (manifest, token) =>");
            }
            else if (schema.HasCancellationToken)
            {
                source.Append("            return route.RegisterStation(")
                    .Append(stationLabelExpression)
                    .AppendLine(", (manifest, token) =>");
            }
            else
            {
                source.Append("            return route.RegisterStation(")
                    .Append(stationLabelExpression)
                    .AppendLine(", manifest =>");
            }

            source.AppendLine("            {");
        }

        private static void EmitPull(StringBuilder source, StationHandlerBinding schema, Options options)
        {
            if (options.Pull == PullMode.NameArray)
            {
                ChainAwareStationCodegen.EmitPullWagonsFromNameArray(
                    source,
                    schema,
                    options.InputNamesVariable);
                return;
            }

            WagonBindingCodegen.EmitPullWagonStatements(source, schema);
        }

        private static void EmitHandlerInvocation(
            StringBuilder source,
            StationHandlerBinding schema,
            Options options)
        {
            var tokenVariable = schema.IsServiceStation || schema.IsAsync || schema.HasCancellationToken
                ? "token"
                : null;
            var redVariable = schema.IsServiceStation ? "red" : null;
            var signalIssue = options.UseNeutralWagonNames
                ? "issue"
                : (redVariable ?? "red") + ".Issue";

            var stationReturnType = GetStationReturnTypeDisplay(schema);
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("                await handler(");
                    EmitHandlerCallArguments(
                        source,
                        schema,
                        tokenVariable,
                        redVariable,
                        options.UseNeutralWagonNames,
                        signalIssue);
                    source.AppendLine(").ConfigureAwait(false);");
                    source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                    return;
                }

                source.Append("                ").Append(stationReturnType).Append(" stationReturn = await handler(");
                EmitHandlerCallArguments(
                    source,
                    schema,
                    tokenVariable,
                    redVariable,
                    options.UseNeutralWagonNames,
                    signalIssue);
                source.AppendLine(").ConfigureAwait(false);");
                return;
            }

            if (schema.ReturnShape.IsVoid)
            {
                source.Append("                handler(");
                EmitHandlerCallArguments(
                    source,
                    schema,
                    tokenVariable,
                    redVariable,
                    options.UseNeutralWagonNames,
                    signalIssue);
                source.AppendLine(");");
                source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                return;
            }

            source.Append("                ").Append(stationReturnType).Append(" stationReturn = handler(");
            EmitHandlerCallArguments(
                source,
                schema,
                tokenVariable,
                redVariable,
                options.UseNeutralWagonNames,
                signalIssue);
            source.AppendLine(");");
        }

        private static void EmitRefLocalValuesIfNeeded(
            StringBuilder source,
            StationHandlerBinding schema,
            bool useNeutralWagonNames)
        {
            if (!schema.HasRefWagons)
            {
                return;
            }

            source.Append("                var refLocalValues = new object[] { ");
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                if (useNeutralWagonNames)
                {
                    source.Append("wagon").Append(i);
                }
                else
                {
                    source.Append(schema.Wagons[i].Name);
                }

                if (i < schema.Wagons.Length - 1)
                {
                    source.Append(", ");
                }
            }

            source.AppendLine(" };");
        }

        private static void EmitReturnMerge(
            StringBuilder source,
            StationHandlerBinding schema,
            Options options)
        {
            var refFlagsField = ResolveRefFlagsForMerge(schema, options);
            var refLocalValues = schema.HasRefWagons ? "refLocalValues" : null;

            if (schema.IsServiceStation
                && options.PassRefFlagsToServiceMergeWhenPresent
                && !string.IsNullOrEmpty(options.RefFlagsExpression))
            {
                source.Append("                return StationMerge.ToServiceSignal(manifest, stationReturn, ")
                    .Append(options.StationLabelExpression)
                    .Append(", ")
                    .Append(options.WagonNamesExpression)
                    .Append(", ")
                    .Append(options.RefFlagsExpression)
                    .Append(", ")
                    .Append(refLocalValues ?? "null")
                    .AppendLine(");");
                return;
            }

            EmitStationReturnMerge(
                source,
                schema,
                options.WagonNamesExpression,
                options.ReturnMembersExpression,
                refFlagsField,
                refLocalValues);
        }

        private static string ResolveRefFlagsForMerge(StationHandlerBinding schema, Options options)
        {
            if (!schema.HasRefWagons)
            {
                return null;
            }

            return options.RefFlagsExpression;
        }

        /// <summary>
        /// Emits typed data merge or StationMerge conversion for a handler return value.
        /// </summary>
        public static void EmitStationReturnMerge(
            StringBuilder source,
            StationHandlerBinding schema,
            string wagonNamesField,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            if (TypedStationReturnCodegen.CanEmitTypedDataMerge(schema, returnMembersField))
            {
                TypedStationReturnCodegen.EmitTypedDataMerge(
                    source,
                    schema,
                    wagonNamesField,
                    returnMembersField,
                    refFlagsField,
                    refLocalValuesExpression);
                return;
            }

            EmitToSignalCall(
                source,
                wagonNamesField,
                schema,
                returnMembersField,
                refFlagsField,
                refLocalValuesExpression);
        }

        private static void EmitToSignalCall(
            StringBuilder source,
            string wagonNamesField,
            StationHandlerBinding schema,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            const string stationLabelExpression = "stationName";
            if (schema.IsServiceStation)
            {
                source.Append("                return StationMerge.ToServiceSignal(manifest, stationReturn, ")
                    .Append(stationLabelExpression)
                    .Append(", ")
                    .Append(wagonNamesField)
                    .Append(", ")
                    .Append(refFlagsField ?? "null")
                    .Append(", ")
                    .Append(refLocalValuesExpression ?? "null")
                    .AppendLine(");");
                return;
            }

            source.Append("                return StationMerge.ToSignal(manifest, stationReturn, ")
                .Append(stationLabelExpression)
                .Append(", ")
                .Append(wagonNamesField)
                .Append(", ")
                .Append(schema.RemoveOmittedRegularInputs ? "true" : "false")
                .Append(", ")
                .Append(returnMembersField ?? "null");

            if (refFlagsField != null)
            {
                source.Append(", ")
                    .Append(refFlagsField)
                    .Append(", ")
                    .Append(refLocalValuesExpression)
                    .AppendLine(");");
            }
            else
            {
                source.AppendLine(");");
            }
        }
    }
}
