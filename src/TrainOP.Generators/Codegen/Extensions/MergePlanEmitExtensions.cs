using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    internal static class MergePlanEmitExtensions
    {
        /// <summary>
        /// Emits typed manifest merge and green signal conversion for a handler return.
        /// </summary>
        internal static void EmitTypedMerge(
            this MergePlan plan,
            CodegenWriter writer,
            StationHandlerBinding schema,
            MergeEmitContext context)
        {
            var returnTypeDisplay = schema.ReturnShape.ReturnTypeDisplay;
            var unwrapGreenPayload = IsGreenPayloadReturnType(returnTypeDisplay);
            var useRuntimeMemberAccess = schema.ReturnShape.UseGenericReturn;
            var dataVariable = context.DataVariable;

            writer.AppendLine("var merged = manifest;");
            if (unwrapGreenPayload)
            {
                writer.AppendIndented("var stationReturnData = stationReturn.Value;")
                    .EndLine();
                dataVariable = "stationReturnData";
            }
            else if (useRuntimeMemberAccess)
            {
                writer.AppendIndented("var mergeSource = WagonStationReturn.UnwrapGreenPayloadReturn(stationReturn);")
                    .EndLine();
                dataVariable = "mergeSource";
            }

            plan.EmitInputSlots(writer, context.WithDataVariable(dataVariable));
            plan.EmitExtraSlots(writer, context.WithDataVariable(dataVariable));
            writer.AppendLine("return RailwaySignals.Green();");
        }

        private static void EmitInputSlots(this MergePlan plan, CodegenWriter writer, MergeEmitContext context)
        {
            for (var i = 0; i < plan.InputSlots.Length; i++)
            {
                plan.InputSlots[i].EmitMergeStatement(writer, context);
            }
        }

        private static void EmitExtraSlots(this MergePlan plan, CodegenWriter writer, MergeEmitContext context)
        {
            for (var i = 0; i < plan.ExtraSlots.Length; i++)
            {
                plan.ExtraSlots[i].EmitMergeStatement(writer, context);
            }
        }

        internal static void EmitMergeStatement(this MergeInputSlot slot, CodegenWriter writer, MergeEmitContext context)
        {
            var wagonNameExpression = MergeEmitContext.BuildWagonNameExpression(context.WagonNamesExpression, slot.WagonIndex);

            if (slot.IsMapped)
            {
                EmitMappedMemberLoad(
                    writer,
                    context,
                    wagonNameExpression,
                    slot.ReturnMemberName,
                    "overlay" + slot.WagonIndex);
                return;
            }

            if (context.RefFlagsExpression != null)
            {
                writer.AppendIndented("if (").Append(context.RefFlagsExpression).Append('[').Append(slot.WagonIndex).AppendLine("])");
                using (writer.Block())
                {
                    EmitLoadWagon(
                        writer,
                        wagonNameExpression,
                        context.RefLocalValuesExpression + "[" + slot.WagonIndex + "]",
                        context.PreserveManifestComposition);
                }

                if (context.RemoveOmittedRegularInputs)
                {
                    writer.AppendLine("else");
                    using (writer.Block())
                    {
                        writer.AppendIndented("merged = merged.UnloadWagon(")
                            .Append(wagonNameExpression)
                            .Append(");");
                        writer.EndLine();
                    }
                }
            }
            else if (context.RemoveOmittedRegularInputs)
            {
                writer.AppendIndented("merged = merged.UnloadWagon(")
                    .Append(wagonNameExpression)
                    .Append(");");
                writer.EndLine();
            }
        }

        internal static void EmitMergeStatement(
            this MergeExtraSlot slot,
            CodegenWriter writer,
            MergeEmitContext context)
        {
            if (slot.AllocateItemWagon)
            {
                EmitAllocatedItemLoad(writer, context, slot.ReturnMemberName);
                return;
            }

            EmitMappedMemberLoad(
                writer,
                context,
                "\"" + StringHelpers.Escape(slot.ReturnMemberName) + "\"",
                slot.ReturnMemberName,
                "overlayExtra_" + slot.ReturnMemberName);
        }

        private static void EmitAllocatedItemLoad(
            CodegenWriter writer,
            MergeEmitContext context,
            string memberName)
        {
            if (context.PreserveManifestComposition)
            {
                // ServiceStation cannot add wagons; analyzer reports TOP015.
                return;
            }

            if (context.UseRuntimeMemberAccess)
            {
                var memberLiteral = "\"" + StringHelpers.Escape(memberName) + "\"";
                var localName = "allocItem_" + StringHelpers.SanitizeIdentifier(memberName);
                writer.AppendIndented("if (WagonStationReturn.TryGetMemberValue(")
                    .Append(context.DataVariable)
                    .Append(", ")
                    .Append(memberLiteral)
                    .Append(", out var ")
                    .Append(localName)
                    .Append(")) merged = ItemWagonNames.LoadNextItemWagon(merged, ")
                    .Append(localName)
                    .Append(");");
                writer.EndLine();
                return;
            }

            writer.AppendIndented("merged = ItemWagonNames.LoadNextItemWagon(merged, ")
                .Append(context.DataVariable)
                .Append(".")
                .Append(memberName)
                .Append(");");
            writer.EndLine();
        }

        private static void EmitMappedMemberLoad(
            CodegenWriter writer,
            MergeEmitContext context,
            string wagonNameExpression,
            string memberName,
            string overlayLocalName)
        {
            if (context.UseRuntimeMemberAccess)
            {
                var memberLiteral = "\"" + StringHelpers.Escape(memberName) + "\"";
                if (context.PreserveManifestComposition)
                {
                    writer.AppendIndented("if (merged.HasWagon(")
                        .Append(wagonNameExpression)
                        .Append(") && WagonStationReturn.TryGetMemberValue(")
                        .Append(context.DataVariable)
                        .Append(", ")
                        .Append(memberLiteral)
                        .Append(", out var ")
                        .Append(overlayLocalName)
                        .Append(")) merged = merged.LoadWagon(")
                        .Append(wagonNameExpression)
                        .Append(", ")
                        .Append(overlayLocalName)
                        .Append(");");
                    writer.EndLine();
                    return;
                }

                writer.AppendIndented("if (WagonStationReturn.TryGetMemberValue(")
                    .Append(context.DataVariable)
                    .Append(", ")
                    .Append(memberLiteral)
                    .Append(", out var ")
                    .Append(overlayLocalName)
                    .Append(")) merged = merged.LoadWagon(")
                    .Append(wagonNameExpression)
                    .Append(", ")
                    .Append(overlayLocalName)
                    .Append(");");
                writer.EndLine();
                return;
            }

            EmitLoadWagon(
                writer,
                wagonNameExpression,
                context.DataVariable + "." + memberName,
                context.PreserveManifestComposition);
        }

        private static void EmitLoadWagon(
            CodegenWriter writer,
            string wagonNameExpression,
            string valueExpression,
            bool preserveManifestComposition)
        {
            if (preserveManifestComposition)
            {
                writer.AppendIndented("if (merged.HasWagon(")
                    .Append(wagonNameExpression)
                    .Append(")) merged = merged.LoadWagon(")
                    .Append(wagonNameExpression)
                    .Append(", ")
                    .Append(valueExpression)
                    .Append(");");
                writer.EndLine();
                return;
            }

            writer.AppendIndented("merged = merged.LoadWagon(")
                .Append(wagonNameExpression)
                .Append(", ")
                .Append(valueExpression)
                .Append(");");
            writer.EndLine();
        }

        private static MergeEmitContext WithDataVariable(this MergeEmitContext context, string dataVariable)
        {
            return new MergeEmitContext(
                context.WagonNamesExpression,
                dataVariable,
                context.RefFlagsExpression,
                context.RefLocalValuesExpression,
                context.RemoveOmittedRegularInputs,
                context.PreserveManifestComposition,
                context.UseRuntimeMemberAccess);
        }

        private static bool IsGreenPayloadReturnType(string returnTypeDisplay)
        {
            return !string.IsNullOrWhiteSpace(returnTypeDisplay)
                && returnTypeDisplay.StartsWith("global::TrainOP.GreenPayload<", System.StringComparison.Ordinal);
        }
    }
}
