using System;
using System.Collections.Generic;
using System.Linq;
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
        internal static void EmitStructOnce(CodegenWriter writer, EmissionState emissionState)
        {
            if (emissionState.EmittedChainBindingStruct)
            {
                return;
            }

            EmitStruct(writer);
            emissionState.EmittedChainBindingStruct = true;
        }

        /// <summary>
        /// Emits binding constants and the resolver method for this table.
        /// </summary>
        internal void Emit(CodegenWriter writer)
        {
            var orderedBindings = _chainBindings
                .OrderBy(binding => binding.ChainId, StringComparer.Ordinal)
                .ThenBy(binding => binding.StationIndex)
                .ToList();

            EmitBindingConstant(
                writer,
                "DefaultChainBinding_" + _names.DelegateTypeId,
                _canonicalSchema,
                _defaultReturnMembers);
            writer.AppendLine();

            for (var i = 0; i < orderedBindings.Count; i++)
            {
                EmitBindingConstant(
                    writer,
                    BuildBindingFieldName(_names.DelegateTypeId, orderedBindings[i]),
                    orderedBindings[i].Schema,
                    orderedBindings[i].ReturnMembers);
            }

            writer.AppendLine();
            EmitResolver(writer, orderedBindings);
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

        private static void EmitStruct(CodegenWriter writer)
        {
            writer.AppendIndented("internal readonly struct ")
                .Append(ChainBindingTypes.BindingTypeName);
            writer.EndLine();
            using (writer.Block())
            {
                writer.AppendIndented("public ")
                    .Append(ChainBindingTypes.BindingTypeName)
                    .Append("(string[] inputNames, string[] returnMembers, bool[] refFlags)");
                writer.EndLine();
                using (writer.Block())
                {
                    writer.AppendLine("InputNames = inputNames;");
                    writer.AppendLine("ReturnMembers = returnMembers;");
                    writer.AppendLine("RefFlags = refFlags;");
                }

                writer.AppendLine();
                writer.AppendLine("public string[] InputNames { get; }");
                writer.AppendLine("public string[] ReturnMembers { get; }");
                writer.AppendLine("public bool[] RefFlags { get; }");
            }

            writer.AppendLine();
        }

        private void EmitResolver(CodegenWriter writer, List<ChainSiteBinding> orderedBindings)
        {
            writer.AppendIndented("private static ")
                .Append(ChainBindingTypes.BindingTypeName)
                .Append(" ")
                .Append(_names.ResolveChainBindingMethod)
                .Append("(string chainKey, int chainStationIndex)");
            writer.EndLine();
            using (writer.Block())
            {
                writer.AppendLine("if (!string.IsNullOrEmpty(chainKey))");
                using (writer.Block())
                {
                    writer.AppendLine("switch (chainKey)");
                    using (writer.Block())
                    {
                        for (var i = 0; i < orderedBindings.Count;)
                        {
                            var chainId = orderedBindings[i].ChainId;
                            writer.AppendIndented("case \"")
                                .Append(StringHelpers.Escape(chainId))
                                .Append("\":");
                            writer.EndLine();
                            using (writer.PushIndent())
                            {
                                writer.AppendLine("switch (chainStationIndex)");
                                using (writer.Block())
                                {
                                    while (i < orderedBindings.Count
                                        && string.Equals(orderedBindings[i].ChainId, chainId, StringComparison.Ordinal))
                                    {
                                        var binding = orderedBindings[i];
                                        writer.AppendIndented("case ")
                                            .Append(binding.StationIndex)
                                            .Append(": return ")
                                            .Append(BuildBindingFieldName(_names.DelegateTypeId, binding))
                                            .Append(";");
                                        writer.EndLine();
                                        i++;
                                    }
                                }

                                writer.AppendLine("break;");
                            }

                            writer.AppendLine();
                        }
                    }
                }

                writer.AppendLine();
                writer.AppendIndented("return DefaultChainBinding_")
                    .Append(_names.DelegateTypeId)
                    .Append(";");
                writer.EndLine();
            }
        }

        private static void EmitBindingConstant(
            CodegenWriter writer,
            string fieldName,
            StationHandlerBinding schema,
            string[] returnMembers)
        {
            writer.AppendIndented("internal static readonly ")
                .Append(ChainBindingTypes.BindingTypeName)
                .Append(" ")
                .Append(fieldName)
                .Append(" = new ")
                .Append(ChainBindingTypes.BindingTypeName)
                .Append("(");
            schema.Input.EmitWagonNamesArrayLiteral(writer, StringHelpers.Escape);
            writer.Append(", ");
            EmitStringArray(writer, returnMembers);
            writer.Append(", ");
            schema.Input.EmitRefFlagsArrayLiteral(writer);
            writer.Append(");");
            writer.EndLine();
        }

        private static void EmitStringArray(CodegenWriter writer, string[] values)
        {
            writer.Append("new string[] { ");
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    writer.Append("\"").Append(StringHelpers.Escape(values[i])).Append("\"");
                    if (i < values.Length - 1)
                    {
                        writer.Append(", ");
                    }
                }
            }

            writer.Append(" }");
        }
    }
}
