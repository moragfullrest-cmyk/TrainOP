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
        /// Determines whether typed data merge can be emitted for a merged handler schema.
        /// </summary>
        public static bool CanEmitTypedDataMerge(StationHandlerBinding schema, string returnMembersField)
        {
            var returnShape = schema.ReturnShape;
            if (schema.IsServiceStation
                || returnShape.IsVoid
                || returnShape.UseGenericReturn
                || returnShape.IsCargoManifest
                || returnShape.IsExplicitSignalReturn
                || returnShape.IsUnknown
                || returnShape.Members.IsDefaultOrEmpty
                || returnMembersField == null)
            {
                return false;
            }

            return !IsSignalOnlyReturnType(returnShape.ReturnTypeDisplay);
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
            EmitTypedMemberMatch(source, returnShape, dataVariable, "wagonName", "wagonValue");
            source.AppendLine("                    if (mapped)");
            source.AppendLine("                    {");
            source.AppendLine("                        merged = merged.LoadWagon(wagonName, wagonValue);");
            source.AppendLine("                    }");
            if (refFlagsField != null)
            {
                source.Append("                    else if (").Append(refFlagsField).AppendLine("[i])");
                source.Append("                    { merged = merged.LoadWagon(wagonName, ").Append(refLocalValuesExpression).AppendLine("[i]); }");
            }

            if (schema.RemoveOmittedRegularInputs)
            {
                source.AppendLine("                    else");
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
            source.AppendLine("                        var mapped = false;");
            EmitTypedMemberMatch(source, returnShape, dataVariable, "memberName", "extraValue", indent: "                        ");
            source.AppendLine("                        if (mapped)");
            source.AppendLine("                        {");
            source.AppendLine("                            merged = merged.LoadWagon(memberName, extraValue);");
            source.AppendLine("                        }");
            source.AppendLine("                    }");
            source.AppendLine("                }");
            source.AppendLine("                return RailwaySignals.Green(merged);");
        }

        private static void EmitTypedMemberMatch(
            StringBuilder source,
            ReturnShape returnShape,
            string dataVariable,
            string nameVariable,
            string valueVariable,
            string indent = "                    ")
        {
            source.Append(indent).Append("object ").Append(valueVariable).AppendLine(" = null;");
            source.Append(indent).AppendLine("switch (" + nameVariable + ")");
            source.Append(indent).AppendLine("{");
            for (var i = 0; i < returnShape.Members.Length; i++)
            {
                var member = returnShape.Members[i];
                source.Append(indent).Append("    case \"").Append(Escape(member.Name)).AppendLine("\":");
                source.Append(indent).Append("        ").Append(valueVariable).Append(" = ").Append(dataVariable).Append('.').Append(member.Name).AppendLine(";");
                source.Append(indent).AppendLine("        mapped = true;");
                source.Append(indent).AppendLine("        break;");
            }

            source.Append(indent).AppendLine("}");
        }

        private static bool IsSignalOnlyReturnType(string returnTypeDisplay)
        {
            if (string.IsNullOrWhiteSpace(returnTypeDisplay))
            {
                return false;
            }

            return returnTypeDisplay == ReturnTypeDisplayBuilder.SignalReturnTypeDisplay
                || returnTypeDisplay == "global::TrainOP.RedFailure"
                || returnTypeDisplay == "global::TrainOP.GreenPass"
                || returnTypeDisplay == "global::TrainOP.CargoManifest"
                || returnTypeDisplay == "global::TrainOP.Signal"
                || returnTypeDisplay == "global::TrainOP.GreenSignal"
                || returnTypeDisplay == "global::TrainOP.RedSignal";
        }

        private static bool IsGreenPayloadReturnType(string returnTypeDisplay)
        {
            return !string.IsNullOrWhiteSpace(returnTypeDisplay)
                && returnTypeDisplay.StartsWith("global::TrainOP.GreenPayload<", System.StringComparison.Ordinal);
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
