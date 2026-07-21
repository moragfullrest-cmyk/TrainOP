using System.Text;
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
            StringBuilder source,
            StationHandlerBinding schema,
            MergeEmitContext context)
        {
            var returnTypeDisplay = schema.ReturnShape.ReturnTypeDisplay;
            var unwrapGreenPayload = IsGreenPayloadReturnType(returnTypeDisplay);
            var dataVariable = unwrapGreenPayload ? "stationReturnData" : context.DataVariable;

            source.AppendLine(context.StatementIndent + "var merged = manifest;");
            if (unwrapGreenPayload)
            {
                source.Append(context.StatementIndent)
                    .Append("var ")
                    .Append(dataVariable)
                    .Append(" = stationReturn.Value;")
                    .AppendLine();
            }

            plan.EmitInputSlots(source, context.WithDataVariable(dataVariable));
            plan.EmitExtraSlots(source, dataVariable, context.StatementIndent);
            source.AppendLine(context.StatementIndent + "return RailwaySignals.Green(merged);");
        }

        private static void EmitInputSlots(this MergePlan plan, StringBuilder source, MergeEmitContext context)
        {
            for (var i = 0; i < plan.InputSlots.Length; i++)
            {
                plan.InputSlots[i].EmitMergeStatement(source, context);
            }
        }

        private static void EmitExtraSlots(this MergePlan plan, StringBuilder source, string dataVariable, string indent)
        {
            for (var i = 0; i < plan.ExtraSlots.Length; i++)
            {
                plan.ExtraSlots[i].EmitMergeStatement(source, dataVariable, indent);
            }
        }

        internal static void EmitMergeStatement(this MergeInputSlot slot, StringBuilder source, MergeEmitContext context)
        {
            var indent = context.StatementIndent;
            var wagonNameExpression = MergeEmitContext.BuildWagonNameExpression(context.WagonNamesExpression, slot.WagonIndex);

            if (slot.IsMapped)
            {
                source.Append(indent).Append("merged = merged.LoadWagon(")
                    .Append(wagonNameExpression)
                    .Append(", ")
                    .Append(context.DataVariable)
                    .Append('.')
                    .Append(slot.ReturnMemberName)
                    .AppendLine(");");
                return;
            }

            if (context.RefFlagsExpression != null)
            {
                source.Append(indent).Append("if (").Append(context.RefFlagsExpression).Append('[').Append(slot.WagonIndex).AppendLine("])");
                source.Append(indent).Append("{ merged = merged.LoadWagon(")
                    .Append(wagonNameExpression)
                    .Append(", ")
                    .Append(context.RefLocalValuesExpression)
                    .Append('[')
                    .Append(slot.WagonIndex)
                    .AppendLine("]); }");

                if (context.RemoveOmittedRegularInputs)
                {
                    source.AppendLine(indent + "else");
                    source.AppendLine(indent + "{");
                    source.Append(indent).Append("    merged = merged.UnloadWagon(")
                        .Append(wagonNameExpression)
                        .AppendLine(");");
                    source.AppendLine(indent + "}");
                }
            }
            else if (context.RemoveOmittedRegularInputs)
            {
                source.Append(indent).Append("merged = merged.UnloadWagon(")
                    .Append(wagonNameExpression)
                    .AppendLine(");");
            }
        }

        internal static void EmitMergeStatement(
            this MergeExtraSlot slot,
            StringBuilder source,
            string dataVariable,
            string indent)
        {
            source.Append(indent).Append("merged = merged.LoadWagon(\"")
                .Append(StringHelpers.Escape(slot.ReturnMemberName))
                .Append("\", ")
                .Append(dataVariable)
                .Append('.')
                .Append(slot.ReturnMemberName)
                .AppendLine(");");
        }

        private static MergeEmitContext WithDataVariable(this MergeEmitContext context, string dataVariable)
        {
            return new MergeEmitContext(
                context.WagonNamesExpression,
                dataVariable,
                context.RefFlagsExpression,
                context.RefLocalValuesExpression,
                context.RemoveOmittedRegularInputs,
                context.StatementIndent);
        }

        private static bool IsGreenPayloadReturnType(string returnTypeDisplay)
        {
            return !string.IsNullOrWhiteSpace(returnTypeDisplay)
                && returnTypeDisplay.StartsWith("global::TrainOP.GreenPayload<", System.StringComparison.Ordinal);
        }
    }
}
