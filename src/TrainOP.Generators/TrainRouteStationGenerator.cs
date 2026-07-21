using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
namespace TrainOP.Generators
{
    /// <summary>
    /// Source generator that emits typed Station and ServiceStation extension methods for data-oriented handlers.
    /// </summary>
    [Generator]
    public sealed class TrainRouteStationGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Creates a generator instance (required for test hosts and MEF discovery).
        /// </summary>
        public TrainRouteStationGenerator()
        {
        }

        /// <summary>
        /// Registers syntax-driven discovery of station handlers and emits grouped extension source code.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var stationSites = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => RouteSiteDiscoverer.IsCandidateStationSite(node),
                static (generatorContext, _) => RouteSiteDiscoverer.TryDiscoverStation(generatorContext)).Collect();

            var anchorSites = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => RouteSiteDiscoverer.IsCandidateAnchorSite(node),
                static (generatorContext, _) => RouteSiteDiscoverer.TryDiscoverAnchor(generatorContext)).Collect();

            var allSites = stationSites
                .Combine(anchorSites)
                .Select(static (pair, _) => RouteSiteDiscoverer.MergeSites(pair.Left, pair.Right));

            var combined = context.CompilationProvider.Combine(allSites);

            context.RegisterSourceOutput(combined, (productionContext, source) =>
            {
                var compilation = source.Left;
                var sites = source.Right;
                RouteSchemaExporter.Emit(productionContext, compilation);
                var graph = RouteGraphAssembler.Build(sites, compilation);
                var groups = new Dictionary<string, TypeSignatureGroup>(StringComparer.Ordinal);
                var processedInvocationKeys = new HashSet<string>(StringComparer.Ordinal);

                foreach (var site in graph.StationSites
                    .OrderBy(site => site.IdentityLocation.SourceSpan.Start))
                {
                    AddDiscoveredCall(
                        groups,
                        processedInvocationKeys,
                        graph.ChainIndex,
                        productionContext,
                        site.HandlerBinding,
                        site.HandlerLocation,
                        site.Invocation);
                }

                foreach (var chainBinding in graph.ChainIndex.Values
                    .SelectMany(x => x)
                    .OrderBy(binding => binding.InvocationLocation.SourceSpan.Start))
                {
                    if (chainBinding.Schema == null || chainBinding.Invocation == null)
                    {
                        continue;
                    }

                    AddDiscoveredCall(
                        groups,
                        processedInvocationKeys,
                        graph.ChainIndex,
                        productionContext,
                        chainBinding.Schema,
                        chainBinding.InvocationLocation,
                        chainBinding.Invocation);
                }

                if (groups.Count == 0)
                {
                    return;
                }

                var mergedSchemas = groups.Values
                    .Select(group => group.ToMerged(productionContext))
                    .OrderBy(x => x.DelegateTypeId, StringComparer.Ordinal)
                    .ToImmutableArray();

                EmitExtensions(productionContext, mergedSchemas);
            });
        }

        private static void AddDiscoveredCall(
            Dictionary<string, TypeSignatureGroup> groups,
            HashSet<string> processedInvocationKeys,
            IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> chainIndex,
            SourceProductionContext productionContext,
            StationHandlerBinding handlerBinding,
            Location location,
            InvocationExpressionSyntax invocation)
        {
            if (handlerBinding == null || invocation == null)
            {
                return;
            }

            var invocationLocation = invocation.GetLocation();
            var invocationKey = ChainStationCallIndex.BuildLocationKey(invocationLocation);
            if (invocationKey.Length == 0 || !processedInvocationKeys.Add(invocationKey))
            {
                return;
            }

            var typeSignature = DelegateTypeSignature.From(handlerBinding);
            var groupingKey = HandlerFuncTypeCodegen.BuildGroupingKey(handlerBinding, typeSignature.TypeId);
            if (!groups.TryGetValue(groupingKey, out var group))
            {
                group = new TypeSignatureGroup(typeSignature);
                groups[groupingKey] = group;
            }

            if (ChainStationCallIndex.TryResolveAll(chainIndex, invocationLocation, out var chainBindings)
                && chainBindings.Length > 0)
            {
                for (var i = 0; i < chainBindings.Length; i++)
                {
                    group.Add(handlerBinding, location, chainBindings[i], productionContext);
                }
            }
            else
            {
                group.Add(handlerBinding, location, null, productionContext);
            }
        }

        /// <summary>
        /// Emits the TrainRouteStationExtensions source file for all merged handler schemas.
        /// </summary>
        private static void EmitExtensions(
            SourceProductionContext context,
            ImmutableArray<MergedStationSchema> schemas)
        {
            var source = new StringBuilder();
            var emittedSignatures = new HashSet<string>(StringComparer.Ordinal);
            var emittedMetadataKeys = new HashSet<string>(StringComparer.Ordinal);
            var emittedChainBindingStruct = false;
            var metadataConsolidation = BuildMetadataConsolidation(schemas);
            source.AppendLine("using System;");
            source.AppendLine("using System.Threading;");
            source.AppendLine("using System.Threading.Tasks;");
            source.AppendLine();
            source.AppendLine("namespace TrainOP");
            source.AppendLine("{");
            source.AppendLine("    public static class TrainRouteStationExtensions");
            source.AppendLine("    {");

            var emittedCount = 0;
            for (var i = 0; i < schemas.Length; i++)
            {
                var merged = schemas[i];
                var emissionKey = HandlerFuncTypeCodegen.BuildGroupingKey(merged.CanonicalBinding, merged.DelegateTypeId);
                if (!emittedSignatures.Add(emissionKey))
                {
                    continue;
                }

                if (emittedCount > 0)
                {
                    source.AppendLine();
                }

                EmitSchemaMembers(source, merged, metadataConsolidation, emittedMetadataKeys, ref emittedChainBindingStruct);
                emittedCount++;
            }

            source.AppendLine("    }");
            source.AppendLine("}");

            context.AddSource("TrainRouteStation.Extensions.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Emits overload resolution priority so sync handlers win over async counterparts
        /// when a throw-only lambda is compatible with both delegate shapes.
        /// </summary>
        private static void EmitOverloadResolutionPriority(StringBuilder source, StationHandlerBinding handlerBinding)
        {
            var priority = handlerBinding.IsAsync ? 0 : 1;
            source.Append("        [System.Runtime.CompilerServices.OverloadResolutionPriority(")
                .Append(priority)
                .AppendLine(")]");
        }

        private static void EmitSchemaMembers(
            StringBuilder source,
            MergedStationSchema merged,
            IReadOnlyDictionary<string, MergedStationSchema> metadataConsolidation,
            HashSet<string> emittedMetadataKeys,
            ref bool emittedChainBindingStruct)
        {
            if (merged.UsesChainDispatch)
            {
                EmitChainAwareSchemaMembers(source, merged, ref emittedChainBindingStruct);
                return;
            }

            var metadataKey = BuildMetadataKey(merged);
            var emitMetadata = emittedMetadataKeys.Add(metadataKey);
            var metadata = metadataConsolidation[metadataKey];
            EmitCanonicalSchemaMembers(source, merged, metadata, emitMetadata);
        }

        /// <summary>
        /// Builds a stable key for shared metadata fields emitted once per delegate signature and wagon-name set.
        /// </summary>
        private static string BuildMetadataKey(MergedStationSchema merged)
        {
            return merged.DelegateTypeId + "|" + HandlerInputParameters.FormatWagonNames(merged.CanonicalBinding.Wagons);
        }

        /// <summary>
        /// Merges return-shape metadata for schemas that share the same delegate type id and wagon names.
        /// </summary>
        private static Dictionary<string, MergedStationSchema> BuildMetadataConsolidation(ImmutableArray<MergedStationSchema> schemas)
        {
            var result = new Dictionary<string, MergedStationSchema>(StringComparer.Ordinal);
            for (var i = 0; i < schemas.Length; i++)
            {
                var merged = schemas[i];
                if (merged.UsesChainDispatch)
                {
                    continue;
                }

                var metadataKey = BuildMetadataKey(merged);
                if (!result.TryGetValue(metadataKey, out var consolidated))
                {
                    consolidated = new MergedStationSchema(merged.CanonicalBinding, merged.DelegateTypeId);
                    consolidated.MergeFrom(merged);
                    result[metadataKey] = consolidated;
                    continue;
                }

                consolidated.MergeFrom(merged);
            }

            return result;
        }

        /// <summary>
        /// Emits chain-dispatched station adapters with compile-time binding lookup.
        /// </summary>
        private static void EmitChainAwareSchemaMembers(
            StringBuilder source,
            MergedStationSchema merged,
            ref bool emittedChainBindingStruct)
        {
            if (!emittedChainBindingStruct)
            {
                ChainAwareStationCodegen.EmitBindingStruct(source);
                emittedChainBindingStruct = true;
            }

            var handlerBinding = merged.CanonicalBinding;
            var delegateTypeId = merged.DelegateTypeId;
            var coreMethodName = "StationCore_" + delegateTypeId;
            ChainAwareStationCodegen.EmitBindingTables(
                source,
                delegateTypeId,
                merged.ChainBindings,
                handlerBinding,
                merged.ReturnMembers);
            source.AppendLine();

            var delegateName = (handlerBinding.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + delegateTypeId;
            if (HandlerFuncTypeCodegen.RequiresCustomDelegate(handlerBinding))
            {
                HandlerFuncTypeCodegen.EmitCustomDelegateDeclaration(source, handlerBinding, delegateName);
                source.AppendLine();
            }

            var handlerTypeName = HandlerFuncTypeCodegen.BuildHandlerTypeName(handlerBinding, delegateName);
            var routeMethodName = handlerBinding.ExtensionMethodName;
            EmitOverloadResolutionPriority(source, handlerBinding);
            source.Append("        public static TrainRoute ")
                .Append(routeMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.Append("            return ")
                .Append(coreMethodName)
                .AppendLine("(route, stationName, handler, route.CallerChainKey, route.NextChainRegistrationOrdinal());");
            source.AppendLine("        }");
            source.AppendLine();

            source.Append("        internal static TrainRoute ")
                .Append(coreMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler, string chainKey, int chainStationIndex)");
            source.AppendLine("        {");
            source.Append("            return ")
                .Append(coreMethodName)
                .Append("(route, stationName, handler, ResolveChainBinding_")
                .Append(delegateTypeId)
                .AppendLine("(chainKey, chainStationIndex));");
            source.AppendLine("        }");
            source.AppendLine();

            source.Append("        internal static TrainRoute ")
                .Append(coreMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .Append(" handler, ")
                .Append(ChainAwareStationCodegen.BindingTypeName)
                .AppendLine(" binding)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.AppendLine("            var inputNames = binding.InputNames;");
            source.AppendLine("            var returnMembers = binding.ReturnMembers;");
            source.AppendLine("            var refFlags = binding.RefFlags;");

            StationAdapterBodyCodegen.EmitRegistration(
                source,
                handlerBinding,
                new StationAdapterBodyCodegen.Options
                {
                    Pull = StationAdapterBodyCodegen.PullMode.NameArray,
                    UseNeutralWagonNames = true,
                    WagonNamesExpression = "inputNames",
                    ReturnMembersExpression = "returnMembers",
                    RefFlagsExpression = "refFlags",
                    PassRefFlagsToServiceMergeWhenPresent = true
                });
            source.AppendLine("        }");
        }

        /// <summary>
        /// Emits delegate, metadata fields, and extension method members for one merged schema.
        /// </summary>
        private static void EmitCanonicalSchemaMembers(
            StringBuilder source,
            MergedStationSchema merged,
            MergedStationSchema metadata,
            bool emitMetadata)
        {
            var handlerBinding = merged.CanonicalBinding;
            var delegateTypeId = merged.DelegateTypeId;
            var wagonNamesField = "WagonNames_" + delegateTypeId;
            var delegateName = (handlerBinding.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + delegateTypeId;
            var hasRefWagons = handlerBinding.HasRefWagons;
            string refFlagsField = hasRefWagons ? "RefFlags_" + delegateTypeId : null;
            string returnMembersField = metadata.ReturnMembers != null ? "ReturnMembers_" + delegateTypeId : null;
            if (emitMetadata)
            {
                if (hasRefWagons)
                {
                    source.Append("        private static readonly bool[] ").Append(refFlagsField).Append(" = ");
                    handlerBinding.Input.AppendRefFlagsArrayLiteral(source);
                    source.AppendLine(";");
                }

                source.Append("        private static readonly string[] ").Append(wagonNamesField).Append(" = ");
                handlerBinding.Input.AppendWagonNamesArrayLiteral(source, StringHelpers.Escape);
                source.AppendLine(";");

                if (returnMembersField != null)
                {
                    var returnMembers = metadata.ReturnMembers;
                    source.Append("        private static readonly string[] ").Append(returnMembersField).Append(" = new string[] { ");
                    for (var i = 0; i < returnMembers.Length; i++)
                    {
                        source.Append("\"").Append(StringHelpers.Escape(returnMembers[i])).Append("\"");
                        if (i < returnMembers.Length - 1)
                        {
                            source.Append(", ");
                        }
                    }

                    source.AppendLine(" };");
                }

                source.AppendLine();

                if (HandlerFuncTypeCodegen.RequiresCustomDelegate(handlerBinding))
                {
                    HandlerFuncTypeCodegen.EmitCustomDelegateDeclaration(source, handlerBinding, delegateName);
                    source.AppendLine();
                }
            }

            var handlerTypeName = HandlerFuncTypeCodegen.BuildHandlerTypeName(handlerBinding, delegateName);
            var routeMethodName = handlerBinding.ExtensionMethodName;
            EmitOverloadResolutionPriority(source, handlerBinding);
            source.Append("        public static TrainRoute ")
                .Append(routeMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.AppendLine("            route.NextChainRegistrationOrdinal();");

            StationAdapterBodyCodegen.EmitRegistration(
                source,
                handlerBinding,
                new StationAdapterBodyCodegen.Options
                {
                    Pull = StationAdapterBodyCodegen.PullMode.LiteralNames,
                    UseNeutralWagonNames = false,
                    WagonNamesExpression = wagonNamesField,
                    ReturnMembersExpression = returnMembersField,
                    RefFlagsExpression = refFlagsField
                });
            source.AppendLine("        }");
        }
    }
}
