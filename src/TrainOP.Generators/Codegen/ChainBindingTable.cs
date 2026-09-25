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
                _defaultReturnMembers,
                _canonicalSchema.ReturnShape.HasDefaultItemNTupleElements);
            writer.AppendLine();

            for (var i = 0; i < orderedBindings.Count; i++)
            {
                EmitBindingConstant(
                    writer,
                    BuildBindingFieldName(_names.DelegateTypeId, orderedBindings[i]),
                    orderedBindings[i].Schema,
                    orderedBindings[i].ReturnMembers,
                    orderedBindings[i].Schema.ReturnShape.HasDefaultItemNTupleElements);
            }

            writer.AppendLine();
            EmitResolver(writer, orderedBindings);
            writer.AppendLine();
            EmitStraightAttach(writer, orderedBindings);
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
                    .Append("(string[] inputNames, string[] returnMembers, bool[] refFlags, bool allocateDefaultItemN)");
                writer.EndLine();
                using (writer.Block())
                {
                    writer.AppendLine("InputNames = inputNames;");
                    writer.AppendLine("ReturnMembers = returnMembers;");
                    writer.AppendLine("RefFlags = refFlags;");
                    writer.AppendLine("AllocateDefaultItemN = allocateDefaultItemN;");
                }

                writer.AppendLine();
                writer.AppendLine("public string[] InputNames { get; }");
                writer.AppendLine("public string[] ReturnMembers { get; }");
                writer.AppendLine("public bool[] RefFlags { get; }");
                writer.AppendLine("public bool AllocateDefaultItemN { get; }");
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
                        EmitChainKeyCases(writer, orderedBindings);
                    }
                }

                writer.AppendLine();
                writer.AppendIndented("return DefaultChainBinding_")
                    .Append(_names.DelegateTypeId)
                    .Append(";");
                writer.EndLine();
            }
        }

        private void EmitChainKeyCases(CodegenWriter writer, List<ChainSiteBinding> orderedBindings)
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
                        i = EmitStationIndexCases(writer, orderedBindings, i, chainId);
                    }

                    writer.AppendLine("break;");
                }

                writer.AppendLine();
            }
        }

        private int EmitStationIndexCases(
            CodegenWriter writer,
            List<ChainSiteBinding> orderedBindings,
            int startIndex,
            string chainId)
        {
            var i = startIndex;
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

            return i;
        }

        private static void EmitBindingConstant(
            CodegenWriter writer,
            string fieldName,
            StationHandlerBinding schema,
            string[] returnMembers,
            bool allocateDefaultItemN)
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
            writer.Append(", ");
            writer.Append(allocateDefaultItemN ? "true" : "false");
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

        private void EmitStraightAttach(CodegenWriter writer, List<ChainSiteBinding> orderedBindings)
        {
            var straightChains = new List<(int Start, int Count, string MethodName, string ChainId, int LastIndex)>();
            for (var i = 0; i < orderedBindings.Count;)
            {
                var chainId = orderedBindings[i].ChainId;
                var start = i;
                while (i < orderedBindings.Count
                    && string.Equals(orderedBindings[i].ChainId, chainId, StringComparison.Ordinal))
                {
                    i++;
                }

                var count = i - start;
                if (!CanEmitStraightChain(orderedBindings, start, count))
                {
                    continue;
                }

                var methodName = "StraightTravel_" + _names.DelegateTypeId + "_" + StringHelpers.SanitizeIdentifier(chainId);
                straightChains.Add((start, count, methodName, chainId, orderedBindings[start + count - 1].StationIndex));
                EmitStraightMethod(writer, methodName, orderedBindings, start, count);
                writer.AppendLine();
            }

            writer.AppendIndented("private static TrainRoute AttachStraight_")
                .Append(_names.DelegateTypeId)
                .Append("(TrainRoute route, string chainKey, int chainStationIndex)");
            writer.EndLine();
            using (writer.Block())
            {
                for (var i = 0; i < straightChains.Count; i++)
                {
                    var chain = straightChains[i];
                    writer.AppendIndented("if (string.Equals(chainKey, \"")
                        .Append(StringHelpers.Escape(chain.ChainId))
                        .Append("\", System.StringComparison.Ordinal) && chainStationIndex == ")
                        .Append(chain.LastIndex.ToString())
                        .Append(") route.AttachStraightTravel(recordVisits => ")
                        .Append(chain.MethodName)
                        .Append("(recordVisits));");
                    writer.EndLine();
                }

                writer.AppendLine("return route;");
            }
        }

        private static bool CanEmitStraightChain(List<ChainSiteBinding> bindings, int start, int count)
        {
            if (count < 2)
            {
                return false;
            }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var schema = bindings[start + i].Schema;
                if (schema == null
                    || schema.StraightExpression == null
                    || schema.IsAsync
                    || schema.IsServiceStation
                    || schema.HasCancellationToken
                    || schema.HasRefWagons
                    || schema.IncludeManifest
                    || schema.IncludeRedSignal
                    || schema.ReturnShape == null
                    || bindings[start + i].ReturnMembers == null
                    || bindings[start + i].ReturnMembers.Length == 0)
                {
                    return false;
                }

                if (i == 0 && schema.Wagons.Length != 0)
                {
                    return false;
                }

                var wagons = schema.Wagons;
                for (var w = 0; w < wagons.Length; w++)
                {
                    if (!declared.Contains(wagons[w].Name))
                    {
                        return false;
                    }
                }

                var members = bindings[start + i].ReturnMembers;
                for (var m = 0; m < members.Length; m++)
                {
                    declared.Add(members[m]);
                }
            }

            return true;
        }

        private static void EmitStraightMethod(
            CodegenWriter writer,
            string methodName,
            List<ChainSiteBinding> bindings,
            int start,
            int count)
        {
            writer.AppendIndented("private static RouteReport ")
                .Append(methodName)
                .Append("(bool recordVisits)");
            writer.EndLine();
            using (writer.Block())
            {
                var declared = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < count; i++)
                {
                    var binding = bindings[start + i];
                    writer.AppendIndented("var step").Append(i.ToString()).Append(" = ")
                        .Append(binding.Schema.StraightExpression)
                        .Append(";");
                    writer.EndLine();
                    var members = binding.ReturnMembers;
                    for (var m = 0; m < members.Length; m++)
                    {
                        var name = members[m];
                        var first = declared.Add(name);
                        writer.AppendIndented(first ? "var " : string.Empty)
                            .Append(name)
                            .Append(" = step")
                            .Append(i.ToString())
                            .Append(".")
                            .Append(name)
                            .Append(";");
                        writer.EndLine();
                    }
                }

                writer.AppendLine("var manifest = new CargoManifest();");
                var terminal = bindings[start + count - 1].ReturnMembers;
                for (var m = 0; m < terminal.Length; m++)
                {
                    var name = terminal[m];
                    writer.AppendIndented("manifest.LoadWagonUnchecked(\"")
                        .Append(StringHelpers.Escape(name))
                        .Append("\", ")
                        .Append(name)
                        .Append(");");
                    writer.EndLine();
                }

                writer.AppendLine("var visits = recordVisits ? new System.Collections.Generic.List<StationVisit>() : null;");
                for (var i = 0; i < count; i++)
                {
                    writer.AppendIndented("if (visits != null) visits.Add(new StationVisit(\"")
                        .Append(StringHelpers.Escape(bindings[start + i].StationName))
                        .Append("\", true));");
                    writer.EndLine();
                }

                writer.AppendLine("return new RouteReport(visits ?? (System.Collections.Generic.IReadOnlyList<StationVisit>)System.Array.Empty<StationVisit>(), RailwaySignals.Green(), manifest);");
            }
        }
    }
}
