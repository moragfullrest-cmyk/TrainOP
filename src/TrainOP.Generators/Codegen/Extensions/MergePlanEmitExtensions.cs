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
            var dataVariable = unwrapGreenPayload ? "stationReturnData" : context.DataVariable;

            writer.AppendLine("var merged = manifest;");
            if (unwrapGreenPayload)
            {
                writer.AppendIndented("var ")
                    .Append(dataVariable)
                    .Append(" = stationReturn.Value;");
                writer.EndLine();
            }

            plan.EmitInputSlots(writer, context.WithDataVariable(dataVariable));
            plan.EmitExtraSlots(writer, dataVariable);
            writer.AppendLine("return RailwaySignals.Green(merged);");
        }

        private static void EmitInputSlots(this MergePlan plan, CodegenWriter writer, MergeEmitContext context)
        {
            for (var i = 0; i < plan.InputSlots.Length; i++)
            {
                plan.InputSlots[i].EmitMergeStatement(writer, context);
            }
        }

        private static void EmitExtraSlots(this MergePlan plan, CodegenWriter writer, string dataVariable)
        {
            for (var i = 0; i < plan.ExtraSlots.Length; i++)
            {
                plan.ExtraSlots[i].EmitMergeStatement(writer, dataVariable);
            }
        }

        internal static void EmitMergeStatement(this MergeInputSlot slot, CodegenWriter writer, MergeEmitContext context)
        {
            var wagonNameExpression = MergeEmitContext.BuildWagonNameExpression(context.WagonNamesExpression, slot.WagonIndex);

            if (slot.IsMapped)
            {
                writer.AppendIndented("merged = merged.LoadWagon(")
                    .Append(wagonNameExpression)
                    .Append(", ")
                    .Append(context.DataVariable)
                    .Append('.')
                    .Append(slot.ReturnMemberName)
                    .Append(");");
                writer.EndLine();
                return;
            }

            if (context.RefFlagsExpression != null)
            {
                writer.AppendIndented("if (").Append(context.RefFlagsExpression).Append('[').Append(slot.WagonIndex).AppendLine("])");
                writer.AppendIndented("{ merged = merged.LoadWagon(")
                    .Append(wagonNameExpression)
                    .Append(", ")
                    .Append(context.RefLocalValuesExpression)
                    .Append('[')
                    .Append(slot.WagonIndex)
                    .Append("]); }");
                writer.EndLine();

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
            string dataVariable)
        {
            writer.AppendIndented("merged = merged.LoadWagon(\"")
                .Append(StringHelpers.Escape(slot.ReturnMemberName))
                .Append("\", ")
                .Append(dataVariable)
                .Append('.')
                .Append(slot.ReturnMemberName)
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
                context.RemoveOmittedRegularInputs);
        }

        private static bool IsGreenPayloadReturnType(string returnTypeDisplay)
        {
            return !string.IsNullOrWhiteSpace(returnTypeDisplay)
                && returnTypeDisplay.StartsWith("global::TrainOP.GreenPayload<", System.StringComparison.Ordinal);
        }
    }
}
