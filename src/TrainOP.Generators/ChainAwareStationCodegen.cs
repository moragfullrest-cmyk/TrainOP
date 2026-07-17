using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits chain-key dispatch tables and binding-aware wagon pull code for station adapters.
    /// </summary>
    internal static class ChainAwareStationCodegen
    {
        /// <summary>
        /// Emits a chain binding struct and lookup tables for one delegate signature group.
        /// </summary>
        public static void EmitBindingTables(
            StringBuilder source,
            string delegateTypeId,
            IReadOnlyList<ChainSiteBinding> chainBindings,
            StationHandlerBinding canonicalSchema,
            string[] defaultReturnMembers)
        {
            var orderedBindings = chainBindings
                .OrderBy(binding => binding.ChainId, System.StringComparer.Ordinal)
                .ThenBy(binding => binding.StationIndex)
                .ToList();

            source.Append("        internal readonly struct ChainStationBinding_")
                .Append(delegateTypeId)
                .AppendLine();
            source.AppendLine("        {");
            source.Append("            public ChainStationBinding_")
                .Append(delegateTypeId)
                .AppendLine("(string[] inputNames, string[] returnMembers, bool[] refFlags)");
            source.AppendLine("            {");
            source.AppendLine("                InputNames = inputNames;");
            source.AppendLine("                ReturnMembers = returnMembers;");
            source.AppendLine("                RefFlags = refFlags;");
            source.AppendLine("            }");
            source.AppendLine();
            source.AppendLine("            public string[] InputNames { get; }");
            source.AppendLine("            public string[] ReturnMembers { get; }");
            source.AppendLine("            public bool[] RefFlags { get; }");
            source.AppendLine("        }");
            source.AppendLine();

            EmitBindingConstant(
                source,
                delegateTypeId,
                "DefaultChainBinding_" + delegateTypeId,
                canonicalSchema,
                defaultReturnMembers);
            source.AppendLine();

            for (var i = 0; i < orderedBindings.Count; i++)
            {
                EmitBindingConstant(
                    source,
                    delegateTypeId,
                    BuildBindingFieldName(delegateTypeId, orderedBindings[i]),
                    orderedBindings[i].Schema,
                    orderedBindings[i].ReturnMembers);
            }

            source.AppendLine();
            source.Append("        private static ChainStationBinding_")
                .Append(delegateTypeId)
                .Append(" ResolveChainBinding_")
                .Append(delegateTypeId)
                .AppendLine("(string chainKey, int chainStationIndex)");
            source.AppendLine("        {");
            source.AppendLine("            if (!string.IsNullOrEmpty(chainKey))");
            source.AppendLine("            {");
            source.AppendLine("                switch (chainKey)");
            source.AppendLine("                {");

            string currentChainId = null;
            for (var i = 0; i < orderedBindings.Count; i++)
            {
                var binding = orderedBindings[i];
                if (!string.Equals(currentChainId, binding.ChainId, System.StringComparison.Ordinal))
                {
                    if (currentChainId != null)
                    {
                        source.AppendLine("                        }");
                        source.AppendLine("                        break;");
                        source.AppendLine();
                    }

                    source.Append("                    case \"")
                        .Append(Escape(binding.ChainId))
                        .AppendLine("\":");
                    source.AppendLine("                        switch (chainStationIndex)");
                    source.AppendLine("                        {");
                    currentChainId = binding.ChainId;
                }

                source.Append("                            case ")
                    .Append(binding.StationIndex)
                    .Append(": return ")
                    .Append(BuildBindingFieldName(delegateTypeId, binding))
                    .AppendLine(";");
            }

            if (currentChainId != null)
            {
                source.AppendLine("                        }");
                source.AppendLine("                        break;");
            }

            source.AppendLine("                }");
            source.AppendLine("            }");
            source.AppendLine();
            source.Append("            return DefaultChainBinding_")
                .Append(delegateTypeId)
                .AppendLine(";");
            source.AppendLine("        }");
        }

        /// <summary>
        /// Emits manifest pull statements using binding input names and neutral local names.
        /// </summary>
        public static void EmitPullWagonsFromBinding(
            StringBuilder source,
            StationHandlerBinding schema,
            string bindingVariable,
            string manifestVariable = "manifest")
        {
            EmitPullWagonsFromNameArray(
                source,
                schema,
                bindingVariable + ".InputNames",
                manifestVariable);
        }

        /// <summary>
        /// Emits manifest pull statements using a string[] variable of wagon names.
        /// </summary>
        public static void EmitPullWagonsFromNameArray(
            StringBuilder source,
            StationHandlerBinding schema,
            string namesVariable,
            string manifestVariable = "manifest")
        {
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                var wagon = schema.Wagons[i];
                var localName = "wagon" + i;
                var nameExpression = namesVariable + "[" + i + "]";
                if (wagon.IsOptional)
                {
                    source.Append("                var ").Append(localName).Append(" = ").Append(manifestVariable).Append(".HasWagon(")
                        .Append(nameExpression)
                        .Append(") ? ").Append(manifestVariable).Append(".PullWagon<")
                        .Append(WagonBindingCodegen.GetManifestPullTypeDisplay(wagon))
                        .Append(">(")
                        .Append(nameExpression)
                        .Append(") : default(")
                        .Append(wagon.TypeDisplay)
                        .AppendLine(");");
                    continue;
                }

                source.Append("                var ").Append(localName).Append(" = ").Append(manifestVariable).Append(".PullWagon<")
                    .Append(WagonBindingCodegen.GetManifestPullTypeDisplay(wagon))
                    .Append(">(")
                    .Append(nameExpression)
                    .AppendLine(");");
            }
        }

        /// <summary>
        /// Emits handler call arguments using neutral wagon local names.
        /// </summary>
        public static void EmitNeutralWagonHandlerCallArguments(
            StringBuilder source,
            StationHandlerBinding schema,
            ref bool needsComma)
        {
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                var wagon = schema.Wagons[i];
                if (wagon.IsByReference)
                {
                    source.Append("ref ");
                }

                source.Append("wagon").Append(i);
                needsComma = true;
            }
        }

        private static void EmitBindingConstant(
            StringBuilder source,
            string delegateTypeId,
            string fieldName,
            StationHandlerBinding schema,
            string[] returnMembers)
        {
            source.Append("        internal static readonly ChainStationBinding_")
                .Append(delegateTypeId)
                .Append(" ")
                .Append(fieldName)
                .Append(" = new ChainStationBinding_")
                .Append(delegateTypeId)
                .Append("(");
            EmitStringArray(source, schema.Wagons, wagon => wagon.Name);
            source.Append(", ");
            EmitStringArray(source, returnMembers);
            source.Append(", ");
            EmitBoolArray(source, schema.Wagons, wagon => wagon.IsByReference);
            source.AppendLine(");");
        }

        /// <summary>
        /// Builds the static binding field name for one chain call site.
        /// </summary>
        internal static string BuildBindingFieldName(string delegateTypeId, ChainSiteBinding binding)
        {
            return BuildBindingFieldName(delegateTypeId, binding.ChainId, binding.StationIndex);
        }

        /// <summary>
        /// Builds the static binding field name from chain id and station index.
        /// </summary>
        internal static string BuildBindingFieldName(string delegateTypeId, string chainId, int stationIndex)
        {
            return "ChainBinding_" + delegateTypeId + "_" + Sanitize(chainId) + "_" + stationIndex;
        }

        private static string Sanitize(string value)
        {
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        private static void EmitStringArray(StringBuilder source, string[] values)
        {
            source.Append("new string[] { ");
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    source.Append("\"").Append(Escape(values[i])).Append("\"");
                    if (i < values.Length - 1)
                    {
                        source.Append(", ");
                    }
                }
            }

            source.Append(" }");
        }

        private static void EmitStringArray(
            StringBuilder source,
            System.Collections.Immutable.ImmutableArray<WagonBinding> wagons,
            System.Func<WagonBinding, string> selector)
        {
            source.Append("new string[] { ");
            for (var i = 0; i < wagons.Length; i++)
            {
                source.Append("\"").Append(Escape(selector(wagons[i]))).Append("\"");
                if (i < wagons.Length - 1)
                {
                    source.Append(", ");
                }
            }

            source.Append(" }");
        }

        private static void EmitBoolArray(
            StringBuilder source,
            System.Collections.Immutable.ImmutableArray<WagonBinding> wagons,
            System.Func<WagonBinding, bool> selector)
        {
            source.Append("new bool[] { ");
            for (var i = 0; i < wagons.Length; i++)
            {
                source.Append(selector(wagons[i]) ? "true" : "false");
                if (i < wagons.Length - 1)
                {
                    source.Append(", ");
                }
            }

            source.Append(" }");
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
