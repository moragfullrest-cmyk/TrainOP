using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using TrainOP.Generators.Models;

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
            var stationCalls = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    StationSyntaxHelper.IsCandidateStationInvocation(node)
                    || StationSyntaxHelper.IsCandidateServiceStationInvocation(node),
                static (generatorContext, _) => GetRouteHandlerCall(generatorContext));

            var combined = context.CompilationProvider
                .Combine(stationCalls.Collect())
                .Combine(context.AnalyzerConfigOptionsProvider);

            context.RegisterSourceOutput(combined, static (productionContext, source) =>
            {
                var compilation = source.Left.Left;
                var calls = source.Left.Right;
                var chainDispatchMode = ChainDispatchModeReader.Read(source.Right);
                RouteSchemaExporter.Emit(productionContext, compilation);
                var chainIndex = ChainStationCallIndex.Build(compilation);
                var groups = new Dictionary<string, TypeSignatureGroup>(StringComparer.Ordinal);
                var interceptorSites = new List<TrainRouteStationInterceptorsEmitter.InterceptorSite>();
                var processedInvocationKeys = new HashSet<string>(StringComparer.Ordinal);
                var processedInterceptorKeys = new HashSet<string>(StringComparer.Ordinal);

                void AddCall(StationHandlerBinding handlerBinding, Location location, InvocationExpressionSyntax invocation)
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

                    ChainStationCallIndex.TryResolve(chainIndex, invocationLocation, out var chainBinding);
                    var typeSignature = DelegateTypeSignature.From(handlerBinding);
                    var groupingKey = HandlerFuncTypeBuilder.BuildGroupingKey(handlerBinding, typeSignature.TypeId);
                    if (!groups.TryGetValue(groupingKey, out var group))
                    {
                        group = new TypeSignatureGroup(typeSignature);
                        groups[groupingKey] = group;
                    }

                    group.Add(handlerBinding, location, chainBinding, productionContext);
                }

                foreach (var call in calls
                    .Where(static call => call != null)
                    .OrderBy(static call => call.Location.SourceSpan.Start))
                {
                    AddCall(call.HandlerBinding, call.Location, call.Invocation);
                }

                foreach (var chainBinding in chainIndex.Values.OrderBy(binding => binding.InvocationLocation.SourceSpan.Start))
                {
                    AddCall(
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

                EmitExtensions(productionContext, mergedSchemas, chainDispatchMode);

                if (ChainDispatchModeReader.UsesInterceptors(chainDispatchMode))
                {
                    foreach (var merged in mergedSchemas)
                    {
                        if (!merged.UsesChainDispatch)
                        {
                            continue;
                        }

                        var handlerBinding = merged.CanonicalBinding;
                        var delegateName = (handlerBinding.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + merged.DelegateTypeId;
                        var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(handlerBinding, delegateName);
                        if (HandlerFuncTypeBuilder.RequiresCustomDelegate(handlerBinding))
                        {
                            handlerTypeName = "global::TrainOP.TrainRouteStationExtensions." + delegateName;
                        }

                        var coreMethodName = "StationCore_" + merged.DelegateTypeId;
                        foreach (var binding in merged.ChainBindings)
                        {
                            var interceptorKey = ChainStationCallIndex.BuildLocationKey(binding.InvocationLocation);
                            if (interceptorKey.Length == 0 || !processedInterceptorKeys.Add(interceptorKey))
                            {
                                continue;
                            }

                            interceptorSites.Add(new TrainRouteStationInterceptorsEmitter.InterceptorSite(
                                binding.Invocation,
                                handlerBinding.ExtensionMethodName,
                                handlerTypeName,
                                coreMethodName,
                                ChainAwareStationCodegen.BuildBindingFieldName(merged.DelegateTypeId, binding)));
                        }
                    }

                    TrainRouteStationInterceptorsEmitter.Emit(productionContext, compilation, interceptorSites);
                }
            });
        }

        /// <summary>
        /// Extracts handler schema metadata from a candidate station or service-station invocation.
        /// </summary>
        private static StationCallInfo GetRouteHandlerCall(GeneratorSyntaxContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.ArgumentList.Arguments.Count != 2)
            {
                return null;
            }

            var semanticModel = context.SemanticModel;
            var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
            var methodName = memberAccess.Name.Identifier.ValueText;
            var forServiceStation = StationSyntaxHelper.IsServiceStationMethodName(methodName);
            if (!forServiceStation
                && !StationSyntaxHelper.IsStationMethodName(methodName))
            {
                return null;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (!StationSyntaxHelper.IsTrainRouteReceiver(memberAccess.Expression, receiverType, semanticModel))
            {
                return null;
            }

            if (StationSyntaxHelper.IsBuiltinTrainRouteHandler(invocation, semanticModel, methodName))
            {
                return null;
            }

            var handlerArgument = invocation.ArgumentList.Arguments[1].Expression;
            if (!StationSyntaxHelper.TryResolveHandler(handlerArgument, semanticModel, out var resolved)
                || resolved == null)
            {
                return null;
            }

            if (forServiceStation && StationSyntaxHelper.IsLikelyBuiltinServiceStationHandler(resolved))
            {
                return null;
            }

            var handlerBinding = HandlerInputSchemaBuilder.TryBuild(resolved, semanticModel, forServiceStation);
            if (handlerBinding == null)
            {
                return null;
            }

            return new StationCallInfo(handlerBinding, resolved.Location, invocation);
        }

        /// <summary>
        /// Emits the TrainRouteStationExtensions source file for all merged handler schemas.
        /// </summary>
        private static void EmitExtensions(
            SourceProductionContext context,
            ImmutableArray<MergedStationSchema> schemas,
            ChainDispatchMode chainDispatchMode)
        {
            var source = new StringBuilder();
            var emittedSignatures = new HashSet<string>(StringComparer.Ordinal);
            var emittedMetadataKeys = new HashSet<string>(StringComparer.Ordinal);
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
                var emissionKey = HandlerFuncTypeBuilder.BuildGroupingKey(merged.CanonicalBinding, merged.DelegateTypeId);
                if (!emittedSignatures.Add(emissionKey))
                {
                    continue;
                }

                if (emittedCount > 0)
                {
                    source.AppendLine();
                }

                EmitSchemaMembers(source, merged, metadataConsolidation, emittedMetadataKeys, chainDispatchMode);
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
            ChainDispatchMode chainDispatchMode)
        {
            if (merged.UsesChainDispatch)
            {
                if (chainDispatchMode == ChainDispatchMode.Reflection)
                {
                    EmitReflectionChainAwareSchemaMembers(source, merged);
                }
                else
                {
                    EmitChainAwareSchemaMembers(source, merged);
                }

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
        private static void EmitChainAwareSchemaMembers(StringBuilder source, MergedStationSchema merged)
        {
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
            if (HandlerFuncTypeBuilder.RequiresCustomDelegate(handlerBinding))
            {
                HandlerFuncTypeBuilder.EmitCustomDelegateDeclaration(source, handlerBinding, delegateName);
                source.AppendLine();
            }

            var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(handlerBinding, delegateName);
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
                .AppendLine("(route, stationName, handler, null, -1);");
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
                .Append(" handler, ChainStationBinding_")
                .Append(delegateTypeId)
                .AppendLine(" binding)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.AppendLine("            var inputNames = binding.InputNames;");
            source.AppendLine("            var returnMembers = binding.ReturnMembers;");
            source.AppendLine("            var refFlags = binding.RefFlags;");

            StationAdapterBodyEmitter.EmitRegistration(
                source,
                handlerBinding,
                new StationAdapterBodyEmitter.Options
                {
                    Pull = StationAdapterBodyEmitter.PullMode.NameArray,
                    UseNeutralWagonNames = true,
                    WagonNamesExpression = "inputNames",
                    ReturnMembersExpression = "returnMembers",
                    RefFlagsExpression = "refFlags",
                    PassRefFlagsToServiceMergeWhenPresent = true
                });
            source.AppendLine("        }");
        }

        /// <summary>
        /// Emits chain-dispatch adapters that resolve wagon names via ParameterInfo (no interceptors).
        /// </summary>
        private static void EmitReflectionChainAwareSchemaMembers(StringBuilder source, MergedStationSchema merged)
        {
            var handlerBinding = merged.CanonicalBinding;
            var delegateTypeId = merged.DelegateTypeId;
            var delegateName = (handlerBinding.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + delegateTypeId;
            if (HandlerFuncTypeBuilder.RequiresCustomDelegate(handlerBinding))
            {
                HandlerFuncTypeBuilder.EmitCustomDelegateDeclaration(source, handlerBinding, delegateName);
                source.AppendLine();
            }

            var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(handlerBinding, delegateName);
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
            source.AppendLine("            var inputNames = StationHandlerParameterNames.GetWagonInputNames(handler);");
            if (handlerBinding.HasRefWagons)
            {
                source.AppendLine("            var refFlags = StationHandlerParameterNames.GetWagonRefFlags(handler);");
            }

            StationAdapterBodyEmitter.EmitRegistration(
                source,
                handlerBinding,
                new StationAdapterBodyEmitter.Options
                {
                    Pull = StationAdapterBodyEmitter.PullMode.NameArray,
                    UseNeutralWagonNames = true,
                    WagonNamesExpression = "inputNames",
                    ReturnMembersExpression = TypedStationReturnCodegen.BuildCompileTimeReturnMembersExpression(handlerBinding),
                    RefFlagsExpression = handlerBinding.HasRefWagons ? "refFlags" : null
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
                handlerBinding.Input.AppendWagonNamesArrayLiteral(source, Escape);
                source.AppendLine(";");

                if (returnMembersField != null)
                {
                    var returnMembers = metadata.ReturnMembers;
                    source.Append("        private static readonly string[] ").Append(returnMembersField).Append(" = new string[] { ");
                    for (var i = 0; i < returnMembers.Length; i++)
                    {
                        source.Append("\"").Append(Escape(returnMembers[i])).Append("\"");
                        if (i < returnMembers.Length - 1)
                        {
                            source.Append(", ");
                        }
                    }

                    source.AppendLine(" };");
                }

                source.AppendLine();

                if (HandlerFuncTypeBuilder.RequiresCustomDelegate(handlerBinding))
                {
                    HandlerFuncTypeBuilder.EmitCustomDelegateDeclaration(source, handlerBinding, delegateName);
                    source.AppendLine();
                }
            }

            var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(handlerBinding, delegateName);
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

            StationAdapterBodyEmitter.EmitRegistration(
                source,
                handlerBinding,
                new StationAdapterBodyEmitter.Options
                {
                    Pull = StationAdapterBodyEmitter.PullMode.LiteralNames,
                    UseNeutralWagonNames = false,
                    WagonNamesExpression = wagonNamesField,
                    ReturnMembersExpression = returnMembersField,
                    RefFlagsExpression = refFlagsField
                });
            source.AppendLine("        }");
        }

        /// <summary>
        /// Escapes string literals for inclusion in generated source code.
        /// </summary>
        private static string Escape(string value)
        {
            return GeneratedSourceEscape.Escape(value);
        }
    }
}
