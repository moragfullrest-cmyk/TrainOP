using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits chain-key dispatch tables for one delegate signature group.
    /// </summary>
    internal sealed class ChainBindingTable
    {
        private readonly NamingScope _names;
        private readonly IReadOnlyList<ChainSiteBinding> _chainBindings;
        private readonly StationHandlerBinding _canonicalSchema;
        private readonly string[] _defaultReturnMembers;

        /// <summary>
        /// Creates a binding table model for one chain-dispatch schema group.
        /// </summary>
        public ChainBindingTable(
            NamingScope names,
            IReadOnlyList<ChainSiteBinding> chainBindings,
            StationHandlerBinding canonicalSchema,
            string[] defaultReturnMembers)
        {
            _names = names;
            _chainBindings = chainBindings;
            _canonicalSchema = canonicalSchema;
            _defaultReturnMembers = defaultReturnMembers;
        }

        /// <summary>
        /// Emits the shared chain binding struct once per generated extensions file.
        /// </summary>
        internal static void EmitStructOnce(StringBuilder source, EmissionState emissionState)
        {
            if (emissionState.EmittedChainBindingStruct)
            {
                return;
            }

            EmitStruct(source);
            emissionState.EmittedChainBindingStruct = true;
        }

        /// <summary>
        /// Emits binding constants and the resolver method for this table.
        /// </summary>
        internal void Emit(StringBuilder source)
        {
            var orderedBindings = _chainBindings
                .OrderBy(binding => binding.ChainId, StringComparer.Ordinal)
                .ThenBy(binding => binding.StationIndex)
                .ToList();

            EmitBindingConstant(
                source,
                "DefaultChainBinding_" + _names.DelegateTypeId,
                _canonicalSchema,
                _defaultReturnMembers);
            source.AppendLine();

            for (var i = 0; i < orderedBindings.Count; i++)
            {
                EmitBindingConstant(
                    source,
                    BuildBindingFieldName(_names.DelegateTypeId, orderedBindings[i]),
                    orderedBindings[i].Schema,
                    orderedBindings[i].ReturnMembers);
            }

            source.AppendLine();
            EmitResolver(source, orderedBindings);
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
            return "ChainBinding_" + delegateTypeId + "_" + StringHelpers.SanitizeIdentifier(chainId) + "_" + stationIndex;
        }

        private static void EmitStruct(StringBuilder source)
        {
            source.Append("        internal readonly struct ")
                .Append(ChainBindingTypes.BindingTypeName)
                .AppendLine();
            source.AppendLine("        {");
            source.Append("            public ")
                .Append(ChainBindingTypes.BindingTypeName)
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
        }

        private void EmitResolver(StringBuilder source, List<ChainSiteBinding> orderedBindings)
        {
            source.Append("        private static ")
                .Append(ChainBindingTypes.BindingTypeName)
                .Append(" ")
                .Append(_names.ResolveChainBindingMethod)
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
                if (!string.Equals(currentChainId, binding.ChainId, StringComparison.Ordinal))
                {
                    if (currentChainId != null)
                    {
                        source.AppendLine("                        }");
                        source.AppendLine("                        break;");
                        source.AppendLine();
                    }

                    source.Append("                    case \"")
                        .Append(StringHelpers.Escape(binding.ChainId))
                        .AppendLine("\":");
                    source.AppendLine("                        switch (chainStationIndex)");
                    source.AppendLine("                        {");
                    currentChainId = binding.ChainId;
                }

                source.Append("                            case ")
                    .Append(binding.StationIndex)
                    .Append(": return ")
                    .Append(BuildBindingFieldName(_names.DelegateTypeId, binding))
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
                .Append(_names.DelegateTypeId)
                .AppendLine(";");
            source.AppendLine("        }");
        }

        private static void EmitBindingConstant(
            StringBuilder source,
            string fieldName,
            StationHandlerBinding schema,
            string[] returnMembers)
        {
            source.Append("        internal static readonly ")
                .Append(ChainBindingTypes.BindingTypeName)
                .Append(" ")
                .Append(fieldName)
                .Append(" = new ")
                .Append(ChainBindingTypes.BindingTypeName)
                .Append("(");
            schema.Input.EmitWagonNamesArrayLiteral(source, StringHelpers.Escape);
            source.Append(", ");
            EmitStringArray(source, returnMembers);
            source.Append(", ");
            schema.Input.EmitRefFlagsArrayLiteral(source);
            source.AppendLine(");");
        }

        private static void EmitStringArray(StringBuilder source, string[] values)
        {
            source.Append("new string[] { ");
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    source.Append("\"").Append(StringHelpers.Escape(values[i])).Append("\"");
                    if (i < values.Length - 1)
                    {
                        source.Append(", ");
                    }
                }
            }

            source.Append(" }");
        }
    }
}
