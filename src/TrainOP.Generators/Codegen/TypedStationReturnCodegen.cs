using System.Text;
using TrainOP.Generators.Handlers;
namespace TrainOP.Generators
{
    /// <summary>
    /// Emits compile-time typed station return merge code for generated route adapters.
    /// </summary>
    internal static class TypedStationReturnCodegen
    {
        /// <summary>
        /// Builds an inline compile-time <c>string[]</c> literal for return member names when the return shape is known.
        /// </summary>
        public static string BuildCompileTimeReturnMembersExpression(StationHandlerBinding schema)
        {
            return schema.Output.BuildCompileTimeReturnMembersExpression(StringHelpers.Escape);
        }

        /// <summary>
        /// Determines whether typed data merge can be emitted for a merged handler schema.
        /// </summary>
        public static bool CanEmitTypedDataMerge(StationHandlerBinding schema, string returnMembersField)
        {
            return MergePlanBuilder.CanBuildStaticPlan(schema, returnMembersField);
        }

        /// <summary>
        /// Emits typed manifest merge and green signal conversion for a data-oriented handler return.
        /// </summary>
        public static void EmitTypedDataMerge(
            StringBuilder source,
            StationHandlerBinding schema,
            string wagonNamesField,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            var returnShape = schema.ReturnShape;
            var returnTypeDisplay = returnShape.ReturnTypeDisplay;
            var unwrapGreenPayload = IsGreenPayloadReturnType(returnTypeDisplay);
            var dataVariable = unwrapGreenPayload ? "stationReturnData" : "stationReturn";
            var plan = MergePlanBuilder.Build(schema);

            source.AppendLine("                var merged = manifest;");
            if (unwrapGreenPayload)
            {
                source.Append("                var ").Append(dataVariable).Append(" = stationReturn.Value;").AppendLine();
            }

            EmitInputSlots(
                source,
                plan,
                wagonNamesField,
                dataVariable,
                refFlagsField,
                refLocalValuesExpression,
                schema.RemoveOmittedRegularInputs);

            EmitExtraSlots(source, plan, dataVariable);

            source.AppendLine("                return RailwaySignals.Green(merged);");
        }

        private static void EmitInputSlots(
            StringBuilder source,
            MergePlan plan,
            string wagonNamesField,
            string dataVariable,
            string refFlagsField,
            string refLocalValuesExpression,
            bool removeOmittedRegularInputs)
        {
            for (var i = 0; i < plan.InputSlots.Length; i++)
            {
                var slot = plan.InputSlots[i];
                var wagonNameExpression = BuildWagonNameExpression(wagonNamesField, slot.WagonIndex);

                if (slot.IsMapped)
                {
                    source.Append("                merged = merged.LoadWagon(")
                        .Append(wagonNameExpression)
                        .Append(", ")
                        .Append(dataVariable)
                        .Append('.')
                        .Append(slot.ReturnMemberName)
                        .AppendLine(");");
                    continue;
                }

                if (refFlagsField != null)
                {
                    source.Append("                if (").Append(refFlagsField).Append('[').Append(slot.WagonIndex).AppendLine("])");
                    source.Append("                { merged = merged.LoadWagon(")
                        .Append(wagonNameExpression)
                        .Append(", ")
                        .Append(refLocalValuesExpression)
                        .Append('[')
                        .Append(slot.WagonIndex)
                        .AppendLine("]); }");

                    if (removeOmittedRegularInputs)
                    {
                        source.AppendLine("                else");
                        source.AppendLine("                {");
                        source.Append("                    merged = merged.UnloadWagon(")
                            .Append(wagonNameExpression)
                            .AppendLine(");");
                        source.AppendLine("                }");
                    }
                }
                else if (removeOmittedRegularInputs)
                {
                    source.Append("                merged = merged.UnloadWagon(")
                        .Append(wagonNameExpression)
                        .AppendLine(");");
                }
            }
        }

        private static void EmitExtraSlots(StringBuilder source, MergePlan plan, string dataVariable)
        {
            for (var i = 0; i < plan.ExtraSlots.Length; i++)
            {
                var slot = plan.ExtraSlots[i];
                source.Append("                merged = merged.LoadWagon(\"")
                    .Append(StringHelpers.Escape(slot.ReturnMemberName))
                    .Append("\", ")
                    .Append(dataVariable)
                    .Append('.')
                    .Append(slot.ReturnMemberName)
                    .AppendLine(");");
            }
        }

        private static string BuildWagonNameExpression(string wagonNamesField, int wagonIndex)
        {
            return wagonNamesField + "[" + wagonIndex + "]";
        }

        private static bool IsGreenPayloadReturnType(string returnTypeDisplay)
        {
            return !string.IsNullOrWhiteSpace(returnTypeDisplay)
                && returnTypeDisplay.StartsWith("global::TrainOP.GreenPayload<", System.StringComparison.Ordinal);
        }
    }
}
