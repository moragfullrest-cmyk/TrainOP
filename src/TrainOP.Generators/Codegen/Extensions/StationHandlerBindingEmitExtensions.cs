using System.Text;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    internal static class StationHandlerBindingEmitExtensions
    {
        /// <summary>
        /// Emits the public route extension method with null checks and adapter registration body.
        /// </summary>
        internal static void EmitPublicExtensionMethod(
            this StationHandlerBinding schema,
            StringBuilder source,
            NamingScope names,
            CodegenContext context,
            bool incrementChainOrdinal)
        {
            schema.EmitOverloadResolutionPriority(source);
            var handlerTypeName = schema.BuildHandlerTypeName(names.DelegateName);
            source.Append("        public static TrainRoute ")
                .Append(schema.ExtensionMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            if (incrementChainOrdinal)
            {
                source.AppendLine("            route.NextChainRegistrationOrdinal();");
            }

            schema.EmitAdapterBody(source, context);
            source.AppendLine("        }");
        }

        /// <summary>
        /// Emits <c>return route.ServiceStation/RegisterStation(..., =&gt; { ... });</c> for one schema.
        /// </summary>
        internal static void EmitAdapterBody(
            this StationHandlerBinding schema,
            StringBuilder source,
            CodegenContext context)
        {
            EmitRegistrationOpen(source, schema, context.StationLabelExpression);
            EmitPull(source, schema, context);
            EmitHandlerInvocation(source, schema, context);
            EmitRefLocalValuesIfNeeded(source, schema, context.UseNeutralWagonNames);
            EmitReturnMerge(source, schema, context);
            source.AppendLine("            });");
        }

        /// <summary>
        /// Emits the chain-dispatch public extension that forwards to the internal core adapter.
        /// </summary>
        internal static void EmitChainDispatchPublicMethod(
            this StationHandlerBinding schema,
            StringBuilder source,
            NamingScope names,
            string handlerTypeName)
        {
            schema.EmitOverloadResolutionPriority(source);
            source.Append("        public static TrainRoute ")
                .Append(schema.ExtensionMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.Append("            return ")
                .Append(names.CoreMethodName)
                .AppendLine("(route, stationName, handler, route.CallerChainKey, route.NextChainRegistrationOrdinal());");
            source.AppendLine("        }");
        }

        /// <summary>
        /// Emits internal core overloads that resolve chain bindings and run the adapter body.
        /// </summary>
        internal static void EmitChainDispatchCoreMethods(
            this StationHandlerBinding schema,
            StringBuilder source,
            NamingScope names,
            string handlerTypeName,
            CodegenContext context)
        {
            source.Append("        internal static TrainRoute ")
                .Append(names.CoreMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler, string chainKey, int chainStationIndex)");
            source.AppendLine("        {");
            source.Append("            return ")
                .Append(names.CoreMethodName)
                .Append("(route, stationName, handler, ")
                .Append(names.ResolveChainBindingMethod)
                .AppendLine("(chainKey, chainStationIndex));");
            source.AppendLine("        }");
            source.AppendLine();

            source.Append("        internal static TrainRoute ")
                .Append(names.CoreMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .Append(" handler, ")
                .Append(ChainBindingTypes.BindingTypeName)
                .AppendLine(" binding)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.AppendLine("            var inputNames = binding.InputNames;");
            source.AppendLine("            var returnMembers = binding.ReturnMembers;");
            source.AppendLine("            var refFlags = binding.RefFlags;");

            schema.EmitAdapterBody(source, context);
            source.AppendLine("        }");
        }

        private static void EmitOverloadResolutionPriority(this StationHandlerBinding schema, StringBuilder source)
        {
            var priority = schema.IsAsync ? 0 : 1;
            source.Append("        [System.Runtime.CompilerServices.OverloadResolutionPriority(")
                .Append(priority)
                .AppendLine(")]");
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
                    source.Append("            return route.")
                        .Append(TrainRouteMethodNames.ServiceStation)
                        .Append("(")
                        .Append(stationLabelExpression)
                        .AppendLine(", async (red, token) =>");
                }
                else
                {
                    source.Append("            return route.")
                        .Append(TrainRouteMethodNames.ServiceStation)
                        .Append("(")
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

        private static void EmitPull(StringBuilder source, StationHandlerBinding schema, CodegenContext context)
        {
            if (context.Pull == PullStrategy.NameArray)
            {
                for (var i = 0; i < schema.Wagons.Length; i++)
                {
                    schema.Wagons[i].EmitPull(
                        source,
                        PullContext.NameArray(i, context.InputNamesVariable));
                }

                return;
            }

            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                schema.Wagons[i].EmitPull(source, PullContext.Literal(schema.Wagons[i]));
            }
        }

        private static void EmitHandlerInvocation(
            StringBuilder source,
            StationHandlerBinding schema,
            CodegenContext context)
        {
            var tokenVariable = schema.IsServiceStation || schema.IsAsync || schema.HasCancellationToken
                ? "token"
                : null;
            var redVariable = schema.IsServiceStation ? "red" : null;
            var signalIssue = context.UseNeutralWagonNames
                ? "issue"
                : (redVariable ?? "red") + ".Issue";
            var argumentContext = new CallArgumentContext(
                context.UseNeutralWagonNames,
                tokenVariable,
                redVariable,
                signalIssue);

            var stationReturnType = GetStationReturnTypeDisplay(schema);
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("                await handler(");
                    schema.Input.EmitCallArguments(source, argumentContext);
                    source.AppendLine(").ConfigureAwait(false);");
                    source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                    return;
                }

                source.Append("                ").Append(stationReturnType).Append(" stationReturn = await handler(");
                schema.Input.EmitCallArguments(source, argumentContext);
                source.AppendLine(").ConfigureAwait(false);");
                return;
            }

            if (schema.ReturnShape.IsVoid)
            {
                source.Append("                handler(");
                schema.Input.EmitCallArguments(source, argumentContext);
                source.AppendLine(");");
                source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                return;
            }

            source.Append("                ").Append(stationReturnType).Append(" stationReturn = handler(");
            schema.Input.EmitCallArguments(source, argumentContext);
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
            CodegenContext context)
        {
            var refFlagsField = schema.HasRefWagons ? context.RefFlagsExpression : null;
            var refLocalValues = schema.HasRefWagons ? "refLocalValues" : null;

            if (schema.IsServiceStation
                && context.PassRefFlagsToServiceMergeWhenPresent
                && !string.IsNullOrEmpty(context.RefFlagsExpression))
            {
                source.Append("                return StationMerge.ToServiceSignal(manifest, stationReturn, ")
                    .Append(context.StationLabelExpression)
                    .Append(", ")
                    .Append(context.WagonNamesExpression)
                    .Append(", ")
                    .Append(context.RefFlagsExpression)
                    .Append(", ")
                    .Append(refLocalValues ?? "null")
                    .AppendLine(");");
                return;
            }

            EmitStationReturnMerge(
                source,
                schema,
                context.WagonNamesExpression,
                context.ReturnMembersExpression,
                refFlagsField,
                refLocalValues);
        }

        private static void EmitStationReturnMerge(
            StringBuilder source,
            StationHandlerBinding schema,
            string wagonNamesField,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            if (MergePlanBuilder.CanBuildStaticPlan(schema, returnMembersField))
            {
                var plan = MergePlanBuilder.Build(schema);
                plan.EmitTypedMerge(
                    source,
                    schema,
                    new MergeEmitContext(
                        wagonNamesField,
                        "stationReturn",
                        refFlagsField,
                        refLocalValuesExpression,
                        schema.RemoveOmittedRegularInputs));
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

        private static string GetStationReturnTypeDisplay(StationHandlerBinding schema)
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

            return HandlerFuncTypeResolver.ResolveCanonicalFuncReturnType(schema);
        }
    }
}
