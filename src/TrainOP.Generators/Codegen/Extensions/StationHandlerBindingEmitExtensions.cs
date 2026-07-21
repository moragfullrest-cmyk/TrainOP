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
            CodegenWriter writer,
            NamingScope names,
            CodegenContext context,
            bool incrementChainOrdinal)
        {
            schema.EmitOverloadResolutionPriority(writer);
            var handlerTypeName = schema.BuildHandlerTypeName(names.DelegateName);
            writer.AppendIndented("public static TrainRoute ")
                .Append(schema.ExtensionMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .Append(" handler)");
            writer.EndLine();
            using (writer.Block())
            {
                writer.AppendLine("if (route == null) throw new ArgumentNullException(nameof(route));");
                writer.AppendLine("if (handler == null) throw new ArgumentNullException(nameof(handler));");
                if (incrementChainOrdinal)
                {
                    writer.AppendLine("route.NextChainRegistrationOrdinal();");
                }

                schema.EmitAdapterBody(writer, context);
            }
        }

        /// <summary>
        /// Emits <c>return route.ServiceStation/RegisterStation(..., =&gt; { ... });</c> for one schema.
        /// </summary>
        internal static void EmitAdapterBody(
            this StationHandlerBinding schema,
            CodegenWriter writer,
            CodegenContext context)
        {
            EmitRegistrationOpen(writer, schema, context.StationLabelExpression);
            using (writer.Block(closeSuffix: ");"))
            {
                if (schema.IsServiceStation)
                {
                    writer.AppendLine("var manifest = red.Manifest;");
                }

                EmitPull(writer, schema, context);
                EmitHandlerInvocation(writer, schema, context);
                EmitRefLocalValuesIfNeeded(writer, schema, context.UseNeutralWagonNames);
                EmitReturnMerge(writer, schema, context);
            }
        }

        /// <summary>
        /// Emits the chain-dispatch public extension that forwards to the internal core adapter.
        /// </summary>
        internal static void EmitChainDispatchPublicMethod(
            this StationHandlerBinding schema,
            CodegenWriter writer,
            NamingScope names,
            string handlerTypeName)
        {
            schema.EmitOverloadResolutionPriority(writer);
            writer.AppendIndented("public static TrainRoute ")
                .Append(schema.ExtensionMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .Append(" handler)");
            writer.EndLine();
            using (writer.Block())
            {
                writer.AppendLine("if (route == null) throw new ArgumentNullException(nameof(route));");
                writer.AppendLine("if (handler == null) throw new ArgumentNullException(nameof(handler));");
                writer.AppendIndented("return ")
                    .Append(names.CoreMethodName)
                    .Append("(route, stationName, handler, route.CallerChainKey, route.NextChainRegistrationOrdinal());");
                writer.EndLine();
            }
        }

        /// <summary>
        /// Emits internal core overloads that resolve chain bindings and run the adapter body.
        /// </summary>
        internal static void EmitChainDispatchCoreMethods(
            this StationHandlerBinding schema,
            CodegenWriter writer,
            NamingScope names,
            string handlerTypeName,
            CodegenContext context)
        {
            writer.AppendIndented("internal static TrainRoute ")
                .Append(names.CoreMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .Append(" handler, string chainKey, int chainStationIndex)");
            writer.EndLine();
            using (writer.Block())
            {
                writer.AppendIndented("return ")
                    .Append(names.CoreMethodName)
                    .Append("(route, stationName, handler, ")
                    .Append(names.ResolveChainBindingMethod)
                    .Append("(chainKey, chainStationIndex));");
                writer.EndLine();
            }

            writer.AppendLine();

            writer.AppendIndented("internal static TrainRoute ")
                .Append(names.CoreMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .Append(" handler, ")
                .Append(ChainBindingTypes.BindingTypeName)
                .Append(" binding)");
            writer.EndLine();
            using (writer.Block())
            {
                writer.AppendLine("if (route == null) throw new ArgumentNullException(nameof(route));");
                writer.AppendLine("if (handler == null) throw new ArgumentNullException(nameof(handler));");
                writer.AppendLine("var inputNames = binding.InputNames;");
                writer.AppendLine("var returnMembers = binding.ReturnMembers;");
                writer.AppendLine("var refFlags = binding.RefFlags;");

                schema.EmitAdapterBody(writer, context);
            }
        }

        private static void EmitOverloadResolutionPriority(this StationHandlerBinding schema, CodegenWriter writer)
        {
            var priority = schema.IsAsync ? 0 : 1;
            writer.AppendIndented("[System.Runtime.CompilerServices.OverloadResolutionPriority(")
                .Append(priority)
                .Append(")]");
            writer.EndLine();
        }

        private static void EmitRegistrationOpen(
            CodegenWriter writer,
            StationHandlerBinding schema,
            string stationLabelExpression)
        {
            if (schema.IsServiceStation)
            {
                if (schema.IsAsync)
                {
                    writer.AppendIndented("return route.")
                        .Append(TrainRouteMethodNames.ServiceStation)
                        .Append("(")
                        .Append(stationLabelExpression)
                        .Append(", async (red, token) =>");
                    writer.EndLine();
                }
                else
                {
                    writer.AppendIndented("return route.")
                        .Append(TrainRouteMethodNames.ServiceStation)
                        .Append("(")
                        .Append(stationLabelExpression)
                        .Append(", (red, token) =>");
                    writer.EndLine();
                }

                return;
            }

            if (schema.IsAsync)
            {
                writer.AppendIndented("return route.RegisterStation(")
                    .Append(stationLabelExpression)
                    .Append(", async (manifest, token) =>");
                writer.EndLine();
            }
            else if (schema.HasCancellationToken)
            {
                writer.AppendIndented("return route.RegisterStation(")
                    .Append(stationLabelExpression)
                    .Append(", (manifest, token) =>");
                writer.EndLine();
            }
            else
            {
                writer.AppendIndented("return route.RegisterStation(")
                    .Append(stationLabelExpression)
                    .Append(", manifest =>");
                writer.EndLine();
            }
        }

        private static void EmitPull(CodegenWriter writer, StationHandlerBinding schema, CodegenContext context)
        {
            if (context.Pull == PullStrategy.NameArray)
            {
                for (var i = 0; i < schema.Wagons.Length; i++)
                {
                    schema.Wagons[i].EmitPull(
                        writer,
                        PullContext.NameArray(i, context.InputNamesVariable));
                }

                return;
            }

            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                schema.Wagons[i].EmitPull(writer, PullContext.Literal(schema.Wagons[i]));
            }
        }

        private static void EmitHandlerInvocation(
            CodegenWriter writer,
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
                    writer.AppendIndented("await handler(");
                    schema.Input.EmitCallArguments(writer, argumentContext);
                    writer.Append(").ConfigureAwait(false);");
                    writer.EndLine();
                    writer.AppendIndented(stationReturnType).Append(" stationReturn = default;");
                    writer.EndLine();
                    return;
                }

                writer.AppendIndented(stationReturnType).Append(" stationReturn = await handler(");
                schema.Input.EmitCallArguments(writer, argumentContext);
                writer.Append(").ConfigureAwait(false);");
                writer.EndLine();
                return;
            }

            if (schema.ReturnShape.IsVoid)
            {
                writer.AppendIndented("handler(");
                schema.Input.EmitCallArguments(writer, argumentContext);
                writer.Append(");");
                writer.EndLine();
                writer.AppendIndented(stationReturnType).Append(" stationReturn = default;");
                writer.EndLine();
                return;
            }

            writer.AppendIndented(stationReturnType).Append(" stationReturn = handler(");
            schema.Input.EmitCallArguments(writer, argumentContext);
            writer.Append(");");
            writer.EndLine();
        }

        private static void EmitRefLocalValuesIfNeeded(
            CodegenWriter writer,
            StationHandlerBinding schema,
            bool useNeutralWagonNames)
        {
            if (!schema.HasRefWagons)
            {
                return;
            }

            writer.AppendIndented("var refLocalValues = new object[] { ");
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                if (useNeutralWagonNames)
                {
                    writer.Append("wagon").Append(i);
                }
                else
                {
                    writer.Append(schema.Wagons[i].Name);
                }

                if (i < schema.Wagons.Length - 1)
                {
                    writer.Append(", ");
                }
            }

            writer.Append(" };");
            writer.EndLine();
        }

        private static void EmitReturnMerge(
            CodegenWriter writer,
            StationHandlerBinding schema,
            CodegenContext context)
        {
            var refFlagsField = schema.HasRefWagons ? context.RefFlagsExpression : null;
            var refLocalValues = schema.HasRefWagons ? "refLocalValues" : null;

            if (schema.IsServiceStation
                && context.PassRefFlagsToServiceMergeWhenPresent
                && !string.IsNullOrEmpty(context.RefFlagsExpression))
            {
                writer.AppendIndented("return StationMerge.ToServiceSignal(manifest, stationReturn, ")
                    .Append(context.StationLabelExpression)
                    .Append(", ")
                    .Append(context.WagonNamesExpression)
                    .Append(", ")
                    .Append(context.RefFlagsExpression)
                    .Append(", ")
                    .Append(refLocalValues ?? "null")
                    .Append(");");
                writer.EndLine();
                return;
            }

            EmitStationReturnMerge(
                writer,
                schema,
                context.WagonNamesExpression,
                context.ReturnMembersExpression,
                refFlagsField,
                refLocalValues);
        }

        private static void EmitStationReturnMerge(
            CodegenWriter writer,
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
                    writer,
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
                writer,
                wagonNamesField,
                schema,
                returnMembersField,
                refFlagsField,
                refLocalValuesExpression);
        }

        private static void EmitToSignalCall(
            CodegenWriter writer,
            string wagonNamesField,
            StationHandlerBinding schema,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            const string stationLabelExpression = "stationName";
            if (schema.IsServiceStation)
            {
                writer.AppendIndented("return StationMerge.ToServiceSignal(manifest, stationReturn, ")
                    .Append(stationLabelExpression)
                    .Append(", ")
                    .Append(wagonNamesField)
                    .Append(", ")
                    .Append(refFlagsField ?? "null")
                    .Append(", ")
                    .Append(refLocalValuesExpression ?? "null")
                    .Append(");");
                writer.EndLine();
                return;
            }

            writer.AppendIndented("return StationMerge.ToSignal(manifest, stationReturn, ")
                .Append(stationLabelExpression)
                .Append(", ")
                .Append(wagonNamesField)
                .Append(", ")
                .Append(schema.RemoveOmittedRegularInputs ? "true" : "false")
                .Append(", ")
                .Append(returnMembersField ?? "null");

            if (refFlagsField != null)
            {
                writer.Append(", ")
                    .Append(refFlagsField)
                    .Append(", ")
                    .Append(refLocalValuesExpression)
                    .Append(");");
                writer.EndLine();
            }
            else
            {
                writer.Append(");");
                writer.EndLine();
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
