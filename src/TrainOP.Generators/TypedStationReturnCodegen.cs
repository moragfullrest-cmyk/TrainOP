using System.Text;
using TrainOP.Generators.Models;

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
            return schema.Output.BuildCompileTimeReturnMembersExpression(Escape);
        }

        /// <summary>
        /// Determines whether typed data merge can be emitted for a merged handler schema.
        /// </summary>
        public static bool CanEmitTypedDataMerge(StationHandlerBinding schema, string returnMembersField)
        {
            return schema.Output.CanEmitTypedDataMerge(schema.IsServiceStation, returnMembersField);
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

            source.AppendLine("                var merged = manifest;");
            if (unwrapGreenPayload)
            {
                source.Append("                var ").Append(dataVariable).Append(" = stationReturn.Value;").AppendLine();
            }

            source.AppendLine("                for (var i = 0; i < ").Append(wagonNamesField).AppendLine(".Length; i++)");
            source.AppendLine("                {");
            source.Append("                    var wagonName = ").Append(wagonNamesField).AppendLine("[i];");
            source.AppendLine("                    var mapped = false;");
            EmitTypedMemberLoad(source, returnShape, dataVariable, "wagonName", "merged", trackMapped: true);
            if (refFlagsField != null)
            {
                source.Append("                    if (!mapped && ").Append(refFlagsField).AppendLine("[i])");
                source.Append("                    { merged = merged.LoadWagon(wagonName, ").Append(refLocalValuesExpression).AppendLine("[i]); }");
                if (schema.RemoveOmittedRegularInputs)
                {
                    source.AppendLine("                    else if (!mapped)");
                    source.AppendLine("                    {");
                    source.AppendLine("                        merged = merged.UnloadWagon(wagonName);");
                    source.AppendLine("                    }");
                }
            }
            else if (schema.RemoveOmittedRegularInputs)
            {
                source.AppendLine("                    if (!mapped)");
                source.AppendLine("                    {");
                source.AppendLine("                        merged = merged.UnloadWagon(wagonName);");
                source.AppendLine("                    }");
            }

            source.AppendLine("                }");

            source.AppendLine("                for (var i = 0; i < ").Append(returnMembersField).AppendLine(".Length; i++)");
            source.AppendLine("                {");
            source.Append("                    var memberName = ").Append(returnMembersField).AppendLine("[i];");
            source.AppendLine("                    var isInputWagon = false;");
            source.Append("                    for (var j = 0; j < ").Append(wagonNamesField).AppendLine(".Length; j++)");
            source.AppendLine("                    {");
            source.Append("                        if (string.Equals(").Append(wagonNamesField).AppendLine("[j], memberName, System.StringComparison.Ordinal))");
            source.AppendLine("                        {");
            source.AppendLine("                            isInputWagon = true;");
            source.AppendLine("                            break;");
            source.AppendLine("                        }");
            source.AppendLine("                    }");
            source.AppendLine();
            source.AppendLine("                    if (!isInputWagon)");
            source.AppendLine("                    {");
            EmitTypedMemberLoad(source, returnShape, dataVariable, "memberName", "merged", indent: "                        ", trackMapped: false);
            source.AppendLine("                    }");
            source.AppendLine("                }");
            source.AppendLine("                return RailwaySignals.Green(merged);");
        }

        private static void EmitTypedMemberLoad(
            StringBuilder source,
            ReturnShape returnShape,
            string dataVariable,
            string nameVariable,
            string manifestVariable,
            string indent = "                    ",
            bool trackMapped = true)
        {
            source.Append(indent).AppendLine("switch (" + nameVariable + ")");
            source.Append(indent).AppendLine("{");
            for (var i = 0; i < returnShape.Members.Length; i++)
            {
                var member = returnShape.Members[i];
                source.Append(indent).Append("    case \"").Append(Escape(member.Name)).AppendLine("\":");
                source.Append(indent).Append("        ").Append(manifestVariable).Append(" = ").Append(manifestVariable).Append(".LoadWagon(")
                    .Append(nameVariable).Append(", ").Append(dataVariable).Append('.')
                    .Append(member.Name).AppendLine(");");
                if (trackMapped)
                {
                    source.Append(indent).AppendLine("        mapped = true;");
                }

                source.Append(indent).AppendLine("        break;");
            }

            source.Append(indent).AppendLine("}");
        }

        private static bool IsGreenPayloadReturnType(string returnTypeDisplay)
        {
            return !string.IsNullOrWhiteSpace(returnTypeDisplay)
                && returnTypeDisplay.StartsWith("global::TrainOP.GreenPayload<", System.StringComparison.Ordinal);
        }

        private static string Escape(string value)
        {
            return GeneratedSourceEscape.Escape(value);
        }
    }
}
