using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
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

                void AddCall(StationHandlerBinding schema, Location location, InvocationExpressionSyntax invocation)
                {
                    if (schema == null || invocation == null)
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
                    var typeSignature = DelegateTypeSignature.From(schema);
                    var groupingKey = HandlerFuncTypeBuilder.BuildGroupingKey(schema, typeSignature.TypeId);
                    if (!groups.TryGetValue(groupingKey, out var group))
                    {
                        group = new TypeSignatureGroup(typeSignature);
                        groups[groupingKey] = group;
                    }

                    group.Add(schema, location, chainBinding, productionContext);
                }

                foreach (var call in calls
                    .Where(static call => call != null)
                    .OrderBy(static call => call.Location.SourceSpan.Start))
                {
                    AddCall(call.Schema, call.Location, call.Invocation);
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

                        var schema = merged.Signature;
                        var delegateName = (schema.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + merged.DelegateTypeId;
                        var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(schema, delegateName);
                        if (HandlerFuncTypeBuilder.RequiresCustomDelegate(schema))
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
                                schema.ExtensionMethodName,
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
            var forServiceStation = string.Equals(methodName, "ServiceStation", StringComparison.Ordinal);
            if (!forServiceStation
                && !string.Equals(methodName, "Station", StringComparison.Ordinal))
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

            var schema = HandlerInputSchemaBuilder.TryBuild(resolved, semanticModel, forServiceStation);
            if (schema == null)
            {
                return null;
            }

            return new StationCallInfo(schema, resolved.Location, invocation);
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
                var emissionKey = HandlerFuncTypeBuilder.BuildGroupingKey(merged.Signature, merged.DelegateTypeId);
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
        /// Resolves the generated station return variable type.
        /// </summary>
        private static string GetStationReturnTypeDisplay(StationHandlerBinding schema)
        {
            if (schema.ReturnShape.IsVoid)
            {
                return "global::System.Object";
            }

            if (!string.IsNullOrWhiteSpace(schema.ReturnShape.ReturnTypeDisplay)
                && !schema.ReturnShape.UseGenericReturn
                && !schema.ReturnShape.IsUnknown)
            {
                return schema.ReturnShape.ReturnTypeDisplay;
            }

            return HandlerFuncTypeBuilder.ResolveCanonicalFuncReturnType(schema);
        }

        /// <summary>
        /// Emits overload resolution priority so sync handlers win over async counterparts
        /// when a throw-only lambda is compatible with both delegate shapes.
        /// </summary>
        private static void EmitOverloadResolutionPriority(StringBuilder source, StationHandlerBinding schema)
        {
            var priority = schema.IsAsync ? 0 : 1;
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
            return merged.DelegateTypeId + "|" + FormatWagonNames(merged.Signature.Wagons);
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
                    consolidated = new MergedStationSchema(merged.Signature, merged.DelegateTypeId);
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
            var schema = merged.Signature;
            var delegateTypeId = merged.DelegateTypeId;
            var coreMethodName = "StationCore_" + delegateTypeId;
            ChainAwareStationCodegen.EmitBindingTables(
                source,
                delegateTypeId,
                merged.ChainBindings,
                schema,
                merged.ReturnMembers);
            source.AppendLine();

            var delegateName = (schema.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + delegateTypeId;
            if (HandlerFuncTypeBuilder.RequiresCustomDelegate(schema))
            {
                HandlerFuncTypeBuilder.EmitCustomDelegateDeclaration(source, schema, delegateName);
                source.AppendLine();
            }

            var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(schema, delegateName);
            var routeMethodName = schema.ExtensionMethodName;
            EmitOverloadResolutionPriority(source, schema);
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

            EmitChainAwareStationRegistration(source, schema);
            source.AppendLine("        }");
        }

        /// <summary>
        /// Emits RegisterStation/ServiceStation registration using hoisted inputNames, returnMembers, and refFlags.
        /// </summary>
        private static void EmitChainAwareStationRegistration(StringBuilder source, StationHandlerBinding schema)
        {
            if (schema.IsServiceStation)
            {
                if (schema.IsAsync)
                {
                    source.AppendLine("            return route.ServiceStation(stationName, async (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                    EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
                else
                {
                    source.AppendLine("            return route.ServiceStation(stationName, (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                    EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
            }
            else if (schema.IsAsync)
            {
                source.AppendLine("            return route.RegisterStation(stationName, async (manifest, token) =>");
                source.AppendLine("            {");
                ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: null);
            }
            else if (schema.HasCancellationToken)
            {
                source.AppendLine("            return route.RegisterStation(stationName, (manifest, token) =>");
                source.AppendLine("            {");
                ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: null);
            }
            else
            {
                source.AppendLine("            return route.RegisterStation(stationName, manifest =>");
                source.AppendLine("            {");
                ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                EmitChainAwareHandlerInvocation(source, schema, tokenVariable: null, redVariable: null);
            }

            if (schema.HasRefWagons)
            {
                source.Append("                var refLocalValues = new object[] { ");
                for (var i = 0; i < schema.Wagons.Length; i++)
                {
                    source.Append("wagon").Append(i);
                    if (i < schema.Wagons.Length - 1)
                    {
                        source.Append(", ");
                    }
                }

                source.AppendLine(" };");
            }

            EmitChainAwareStationReturnMerge(source, schema);
            source.AppendLine("            });");
        }

        /// <summary>
        /// Emits chain-dispatch adapters that resolve wagon names via ParameterInfo (no interceptors).
        /// </summary>
        private static void EmitReflectionChainAwareSchemaMembers(StringBuilder source, MergedStationSchema merged)
        {
            var schema = merged.Signature;
            var delegateTypeId = merged.DelegateTypeId;
            var delegateName = (schema.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + delegateTypeId;
            if (HandlerFuncTypeBuilder.RequiresCustomDelegate(schema))
            {
                HandlerFuncTypeBuilder.EmitCustomDelegateDeclaration(source, schema, delegateName);
                source.AppendLine();
            }

            var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(schema, delegateName);
            var routeMethodName = schema.ExtensionMethodName;
            EmitOverloadResolutionPriority(source, schema);
            source.Append("        public static TrainRoute ")
                .Append(routeMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");
            source.AppendLine("            var inputNames = StationHandlerParameterNames.GetWagonInputNames(handler);");
            if (schema.HasRefWagons)
            {
                source.AppendLine("            var refFlags = StationHandlerParameterNames.GetWagonRefFlags(handler);");
            }

            if (schema.IsServiceStation)
            {
                if (schema.IsAsync)
                {
                    source.AppendLine("            return route.ServiceStation(stationName, async (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                    EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
                else
                {
                    source.AppendLine("            return route.ServiceStation(stationName, (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                    EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
            }
            else if (schema.IsAsync)
            {
                source.AppendLine("            return route.RegisterStation(stationName, async (manifest, token) =>");
                source.AppendLine("            {");
                ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: null);
            }
            else if (schema.HasCancellationToken)
            {
                source.AppendLine("            return route.RegisterStation(stationName, (manifest, token) =>");
                source.AppendLine("            {");
                ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                EmitChainAwareHandlerInvocation(source, schema, tokenVariable: "token", redVariable: null);
            }
            else
            {
                source.AppendLine("            return route.RegisterStation(stationName, manifest =>");
                source.AppendLine("            {");
                ChainAwareStationCodegen.EmitPullWagonsFromNameArray(source, schema, "inputNames");
                EmitChainAwareHandlerInvocation(source, schema, tokenVariable: null, redVariable: null);
            }

            if (schema.HasRefWagons)
            {
                source.Append("                var refLocalValues = new object[] { ");
                for (var i = 0; i < schema.Wagons.Length; i++)
                {
                    source.Append("wagon").Append(i);
                    if (i < schema.Wagons.Length - 1)
                    {
                        source.Append(", ");
                    }
                }

                source.AppendLine(" };");
            }

            EmitReflectionStationReturnMerge(source, schema);
            source.AppendLine("            });");
            source.AppendLine("        }");
        }

        private static void EmitReflectionStationReturnMerge(StringBuilder source, StationHandlerBinding schema)
        {
            if (schema.IsServiceStation)
            {
                source.Append("                return StationMerge.ToServiceSignal(manifest, stationReturn, stationName, inputNames, ")
                    .Append(schema.HasRefWagons ? "refFlags" : "null")
                    .Append(", ")
                    .Append(schema.HasRefWagons ? "refLocalValues" : "null")
                    .AppendLine(");");
                return;
            }

            EmitStationReturnMerge(
                source,
                schema,
                "inputNames",
                TypedStationReturnCodegen.BuildCompileTimeReturnMembersExpression(schema),
                schema.HasRefWagons ? "refFlags" : null,
                schema.HasRefWagons ? "refLocalValues" : null);
        }

        private static void EmitChainAwareHandlerInvocation(
            StringBuilder source,
            StationHandlerBinding schema,
            string tokenVariable,
            string redVariable)
        {
            var stationReturnType = GetStationReturnTypeDisplay(schema);
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("                await handler(");
                    EmitChainAwareHandlerCallArguments(source, schema, tokenVariable, redVariable);
                    source.AppendLine(").ConfigureAwait(false);");
                    source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                    return;
                }

                source.Append("                ").Append(stationReturnType).Append(" stationReturn = await handler(");
                EmitChainAwareHandlerCallArguments(source, schema, tokenVariable, redVariable);
                source.AppendLine(").ConfigureAwait(false);");
                return;
            }

            if (schema.ReturnShape.IsVoid)
            {
                source.Append("                handler(");
                EmitChainAwareHandlerCallArguments(source, schema, tokenVariable, redVariable);
                source.AppendLine(");");
                source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                return;
            }

            source.Append("                ").Append(stationReturnType).Append(" stationReturn = handler(");
            EmitChainAwareHandlerCallArguments(source, schema, tokenVariable, redVariable);
            source.AppendLine(");");
        }

        private static void EmitChainAwareHandlerCallArguments(
            StringBuilder source,
            StationHandlerBinding schema,
            string tokenVariable,
            string redVariable)
        {
            var needsComma = false;
            if (schema.IsServiceStation)
            {
                ChainAwareStationCodegen.EmitNeutralWagonHandlerCallArguments(source, schema, ref needsComma);

                if (schema.IncludeRedSignal)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append(redVariable ?? "red");
                    needsComma = true;
                }
            }
            else
            {
                if (schema.IncludeRedSignal)
                {
                    source.Append(redVariable ?? "red");
                    needsComma = true;
                }

                if (schema.IncludeSignalIssue)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append("issue");
                    needsComma = true;
                }

                if (schema.IncludeManifest)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append("manifest");
                    needsComma = true;
                }

                ChainAwareStationCodegen.EmitNeutralWagonHandlerCallArguments(source, schema, ref needsComma);
            }

            if (schema.HasCancellationToken)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                source.Append(tokenVariable ?? "default");
            }
        }

        private static void EmitChainAwareStationReturnMerge(StringBuilder source, StationHandlerBinding schema)
        {
            if (schema.IsServiceStation)
            {
                source.Append("                return StationMerge.ToServiceSignal(manifest, stationReturn, stationName, inputNames, refFlags, ")
                    .Append(schema.HasRefWagons ? "refLocalValues" : "null")
                    .AppendLine(");");
                return;
            }

            EmitStationReturnMerge(
                source,
                schema,
                "inputNames",
                "returnMembers",
                schema.HasRefWagons ? "refFlags" : null,
                schema.HasRefWagons ? "refLocalValues" : null);
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
            var schema = merged.Signature;
            var delegateTypeId = merged.DelegateTypeId;
            var wagonNamesField = "WagonNames_" + delegateTypeId;
            var delegateName = (schema.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + delegateTypeId;
            var hasRefWagons = schema.HasRefWagons;
            string refFlagsField = hasRefWagons ? "RefFlags_" + delegateTypeId : null;
            string returnMembersField = metadata.ReturnMembers != null ? "ReturnMembers_" + delegateTypeId : null;
            if (emitMetadata)
            {
                if (hasRefWagons)
                {
                    source.Append("        private static readonly bool[] ").Append(refFlagsField).Append(" = new bool[] { ");
                    for (var i = 0; i < schema.Wagons.Length; i++)
                    {
                        source.Append(schema.Wagons[i].IsByReference ? "true" : "false");
                        if (i < schema.Wagons.Length - 1)
                        {
                            source.Append(", ");
                        }
                    }

                    source.AppendLine(" };");
                }

                source.Append("        private static readonly string[] ").Append(wagonNamesField).Append(" = new string[] { ");
                for (var i = 0; i < schema.Wagons.Length; i++)
                {
                    source.Append("\"").Append(Escape(schema.Wagons[i].Name)).Append("\"");
                    if (i < schema.Wagons.Length - 1)
                    {
                        source.Append(", ");
                    }
                }

                source.AppendLine(" };");

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

                if (HandlerFuncTypeBuilder.RequiresCustomDelegate(schema))
                {
                    HandlerFuncTypeBuilder.EmitCustomDelegateDeclaration(source, schema, delegateName);
                    source.AppendLine();
                }
            }

            var handlerTypeName = HandlerFuncTypeBuilder.BuildHandlerTypeName(schema, delegateName);
            var routeMethodName = schema.ExtensionMethodName;
            var stationLabelExpression = "stationName";
            EmitOverloadResolutionPriority(source, schema);
            source.Append("        public static TrainRoute ")
                .Append(routeMethodName)
                .Append("(this TrainRoute route, string stationName, ")
                .Append(handlerTypeName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");

            if (schema.IsServiceStation)
            {
                if (schema.IsAsync)
                {
                    source.Append("            return route.ServiceStation(").Append(stationLabelExpression).AppendLine(", async (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    WagonBindingCodegen.EmitPullWagonStatements(source, schema);
                    EmitHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
                else
                {
                    source.Append("            return route.ServiceStation(").Append(stationLabelExpression).AppendLine(", (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    WagonBindingCodegen.EmitPullWagonStatements(source, schema);
                    EmitHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
            }
            else if (schema.IsAsync)
            {
                source.Append("            return route.RegisterStation(").Append(stationLabelExpression).AppendLine(", async (manifest, token) =>");
                source.AppendLine("            {");
                WagonBindingCodegen.EmitPullWagonStatements(source, schema);
                EmitHandlerInvocation(source, schema, tokenVariable: "token", redVariable: null);
            }
            else if (schema.HasCancellationToken)
            {
                source.Append("            return route.RegisterStation(").Append(stationLabelExpression).AppendLine(", (manifest, token) =>");
                source.AppendLine("            {");
                WagonBindingCodegen.EmitPullWagonStatements(source, schema);
                EmitHandlerInvocation(source, schema, tokenVariable: "token", redVariable: null);
            }
            else
            {
                source.Append("            return route.RegisterStation(").Append(stationLabelExpression).AppendLine(", manifest =>");
                source.AppendLine("            {");
                WagonBindingCodegen.EmitPullWagonStatements(source, schema);
                EmitHandlerInvocation(source, schema, tokenVariable: null, redVariable: null);
            }

            if (hasRefWagons)
            {
                source.Append("                var refLocalValues = new object[] { ");
                for (var i = 0; i < schema.Wagons.Length; i++)
                {
                    source.Append(schema.Wagons[i].Name);
                    if (i < schema.Wagons.Length - 1)
                    {
                        source.Append(", ");
                    }
                }

                source.AppendLine(" };");
                EmitStationReturnMerge(
                    source,
                    schema,
                    wagonNamesField,
                    returnMembersField,
                    refFlagsField,
                    "refLocalValues");
            }
            else if (schema.IsServiceStation)
            {
                EmitStationReturnMerge(
                    source,
                    schema,
                    wagonNamesField,
                    returnMembersField,
                    refFlagsField,
                    null);
            }
            else
            {
                EmitStationReturnMerge(
                    source,
                    schema,
                    wagonNamesField,
                    returnMembersField,
                    null,
                    null);
            }
            source.AppendLine("            });");
            source.AppendLine("        }");
        }

        /// <summary>
        /// Emits handler invocation and assigns the station return value for merge.
        /// </summary>
        private static void EmitHandlerInvocation(
            StringBuilder source,
            StationHandlerBinding schema,
            string tokenVariable,
            string redVariable)
        {
            var stationReturnType = GetStationReturnTypeDisplay(schema);
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("                await handler(");
                    EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
                    source.AppendLine(").ConfigureAwait(false);");
                    source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                    return;
                }

                source.Append("                ").Append(stationReturnType).Append(" stationReturn = await handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
                source.AppendLine(").ConfigureAwait(false);");
                return;
            }

            if (schema.ReturnShape.IsVoid)
            {
                source.Append("                handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
                source.AppendLine(");");
                source.Append("                ").Append(stationReturnType).AppendLine(" stationReturn = default;");
                return;
            }

            source.Append("                ").Append(stationReturnType).Append(" stationReturn = handler(");
            EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
            source.AppendLine(");");
        }

        /// <summary>
        /// Emits typed data merge or StationMerge conversion for a handler return value.
        /// </summary>
        private static void EmitStationReturnMerge(
            StringBuilder source,
            StationHandlerBinding schema,
            string wagonNamesField,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            if (TypedStationReturnCodegen.CanEmitTypedDataMerge(schema, returnMembersField))
            {
                TypedStationReturnCodegen.EmitTypedDataMerge(
                    source,
                    schema,
                    wagonNamesField,
                    returnMembersField,
                    refFlagsField,
                    refLocalValuesExpression);
                return;
            }

            EmitToSignalCall(
                source,
                wagonNamesField,
                schema,
                returnMembersField,
                refFlagsField,
                refLocalValuesExpression);
        }

        /// <summary>
        /// Emits the StationMerge.ToSignal call that merges handler output back into the manifest.
        /// </summary>
        private static void EmitToSignalCall(
            StringBuilder source,
            string wagonNamesField,
            StationHandlerBinding schema,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            var stationLabelExpression = "stationName";
            if (schema.IsServiceStation)
            {
                source.Append("                return StationMerge.ToServiceSignal(manifest, stationReturn, ")
                    .Append(stationLabelExpression)
                    .Append(", ")
                    .Append(wagonNamesField)
                    .Append(", ")
                    .Append(refFlagsField)
                    .Append(", ")
                    .Append(refLocalValuesExpression ?? "null")
                    .AppendLine(");");
                return;
            }

            source.Append("                return StationMerge.ToSignal(manifest, stationReturn, ")
                .Append(stationLabelExpression)
                .Append(", ")
                .Append(wagonNamesField)
                .Append(", ")
                .Append(schema.RemoveOmittedRegularInputs ? "true" : "false")
                .Append(", ")
                .Append(returnMembersField ?? "null");

            if (refFlagsField != null)
            {
                source.Append(", ")
                    .Append(refFlagsField)
                    .Append(", ")
                    .Append(refLocalValuesExpression)
                    .AppendLine(");");
            }
            else
            {
                source.AppendLine(");");
            }
        }

        /// <summary>
        /// Emits argument expressions for invoking the generated handler delegate.
        /// </summary>
        private static void EmitHandlerCallArguments(
            StringBuilder source,
            StationHandlerBinding schema,
            string tokenVariable,
            string redVariable)
        {
            var needsComma = false;
            if (schema.IsServiceStation)
            {
                EmitWagonHandlerCallArguments(source, schema, ref needsComma);

                if (schema.IncludeRedSignal)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append(redVariable ?? "red");
                    needsComma = true;
                }
            }
            else
            {
                if (schema.IncludeRedSignal)
                {
                    source.Append(redVariable ?? "red");
                    needsComma = true;
                }

                if (schema.IncludeSignalIssue)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append(redVariable ?? "red").Append(".Issue");
                    needsComma = true;
                }

                if (schema.IncludeManifest)
                {
                    if (needsComma)
                    {
                        source.Append(", ");
                    }

                    source.Append("manifest");
                    needsComma = true;
                }

                EmitWagonHandlerCallArguments(source, schema, ref needsComma);
            }

            if (schema.HasCancellationToken)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                source.Append(tokenVariable ?? "default");
            }
        }

        /// <summary>
        /// Emits wagon argument expressions for invoking the generated handler delegate.
        /// </summary>
        private static void EmitWagonHandlerCallArguments(
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

                source.Append(wagon.Name);
                needsComma = true;
            }
        }

        /// <summary>
        /// Escapes string literals for inclusion in generated source code.
        /// </summary>
        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// Holds handler schema metadata discovered from a single station invocation site.
        /// </summary>
        private sealed class StationCallInfo
        {
            /// <summary>
            /// Creates a station call record with schema and source location.
            /// </summary>
            public StationCallInfo(StationHandlerBinding schema, Location location, InvocationExpressionSyntax invocation)
            {
                Schema = schema;
                Location = location;
                Invocation = invocation;
                InvocationLocation = invocation.GetLocation();
            }

            public StationHandlerBinding Schema { get; }

            public Location Location { get; }

            public InvocationExpressionSyntax Invocation { get; }

            public Location InvocationLocation { get; }
        }

        /// <summary>
        /// Compares two wagon binding arrays for matching names in the same order.
        /// </summary>
        private static bool WagonNamesMatch(ImmutableArray<WagonBinding> left, ImmutableArray<WagonBinding> right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Formats wagon names as a comma-separated diagnostic string.
        /// </summary>
        private static string FormatWagonNames(ImmutableArray<WagonBinding> wagons)
        {
            if (wagons.Length == 0)
            {
                return "(none)";
            }

            var builder = new StringBuilder();
            for (var i = 0; i < wagons.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(wagons[i].Name);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Accumulates handler schemas that share the same delegate type signature.
        /// </summary>
        private sealed class TypeSignatureGroup
        {
            private readonly DelegateTypeSignature _typeSignature;
            private readonly List<ReturnShape> _returnShapes = new List<ReturnShape>();
            private readonly List<ChainSiteBinding> _chainBindings = new List<ChainSiteBinding>();
            private readonly List<StationEntry> _entries = new List<StationEntry>();
            private StationHandlerBinding _canonicalSchema;

            /// <summary>
            /// Creates a group keyed by delegate type signature.
            /// </summary>
            public TypeSignatureGroup(DelegateTypeSignature typeSignature)
            {
                _typeSignature = typeSignature;
            }

            /// <summary>
            /// Adds a handler schema to the group and reports conflicting wagon names.
            /// </summary>
            public void Add(
                StationHandlerBinding schema,
                Location location,
                ChainSiteBinding chainBinding,
                SourceProductionContext context)
            {
                _entries.Add(new StationEntry(schema, location, chainBinding));
                if (chainBinding != null
                    && !_chainBindings.Exists(existing =>
                        ChainStationCallIndex.BuildLocationKey(existing.InvocationLocation)
                        == ChainStationCallIndex.BuildLocationKey(chainBinding.InvocationLocation)))
                {
                    _chainBindings.Add(chainBinding);
                }

                if (_canonicalSchema == null)
                {
                    _canonicalSchema = schema;
                }

                AddReturnShape(schema.ReturnShape);
            }

            /// <summary>
            /// Produces a merged schema with combined return-shape metadata for code generation.
            /// </summary>
            public MergedStationSchema ToMerged(SourceProductionContext context)
            {
                var merged = new MergedStationSchema(_canonicalSchema, _typeSignature.TypeId);
                for (var i = 0; i < _returnShapes.Count; i++)
                {
                    merged.AddReturnShape(_returnShapes[i]);
                }

                if (RequiresChainDispatch())
                {
                    ReportNonChainConflicts(context);
                    merged.SetChainBindings(_chainBindings);
                }
                else
                {
                    ReportCanonicalConflicts(context);
                }

                return merged;
            }

            private void ReportCanonicalConflicts(SourceProductionContext context)
            {
                if (_canonicalSchema == null)
                {
                    return;
                }

                for (var i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    if (entry.ChainBinding != null)
                    {
                        continue;
                    }

                    if (!WagonNamesMatch(_canonicalSchema.Wagons, entry.Schema.Wagons))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            TrainRouteDiagnostics.ConflictingWagonNames,
                            entry.Location,
                            FormatWagonNames(entry.Schema.Wagons),
                            FormatWagonNames(_canonicalSchema.Wagons)));
                    }
                }
            }

            private bool RequiresChainDispatch()
            {
                if (_chainBindings.Count == 0)
                {
                    return false;
                }

                var wagonNameSets = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < _entries.Count; i++)
                {
                    wagonNameSets.Add(FormatWagonNames(_entries[i].Schema.Wagons));
                }

                return wagonNameSets.Count > 1;
            }

            private void ReportNonChainConflicts(SourceProductionContext context)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    var left = _entries[i];
                    if (left.ChainBinding != null)
                    {
                        continue;
                    }

                    for (var j = 0; j < _entries.Count; j++)
                    {
                        if (i == j)
                        {
                            continue;
                        }

                        var right = _entries[j];
                        if (!WagonNamesMatch(left.Schema.Wagons, right.Schema.Wagons))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                TrainRouteDiagnostics.ConflictingWagonNames,
                                left.Location,
                                FormatWagonNames(left.Schema.Wagons),
                                FormatWagonNames(right.Schema.Wagons)));
                            break;
                        }
                    }
                }
            }

            private sealed class StationEntry
            {
                public StationEntry(StationHandlerBinding schema, Location location, ChainSiteBinding chainBinding)
                {
                    Schema = schema;
                    Location = location;
                    ChainBinding = chainBinding;
                }

                public StationHandlerBinding Schema { get; }

                public Location Location { get; }

                public ChainSiteBinding ChainBinding { get; }
            }

            /// <summary>
            /// Records a distinct return shape for later merge metadata generation.
            /// </summary>
            private void AddReturnShape(ReturnShape returnShape)
            {
                for (var i = 0; i < _returnShapes.Count; i++)
                {
                    if (MergedStationSchema.ReturnShapesEqual(_returnShapes[i], returnShape))
                    {
                        return;
                    }
                }

                _returnShapes.Add(returnShape);
            }
        }

        /// <summary>
        /// Describes a handler delegate type signature used to group generated extension methods.
        /// </summary>
        private sealed class DelegateTypeSignature
        {
            /// <summary>
            /// Creates a delegate type signature from handler flags and wagon type slots.
            /// </summary>
            public DelegateTypeSignature(
                bool isServiceStation,
                bool includeRedSignal,
                bool includeSignalIssue,
                bool includeManifest,
                bool isAsync,
                bool hasCancellationToken,
                bool isVoid,
                bool useGenericReturn,
                string returnTypeKey,
                ImmutableArray<WagonTypeSlot> wagonTypes)
            {
                IsServiceStation = isServiceStation;
                IncludeRedSignal = includeRedSignal;
                IncludeSignalIssue = includeSignalIssue;
                IncludeManifest = includeManifest;
                IsAsync = isAsync;
                HasCancellationToken = hasCancellationToken;
                IsVoid = isVoid;
                UseGenericReturn = useGenericReturn;
                ReturnTypeKey = returnTypeKey;
                WagonTypes = wagonTypes;
                TypeId = BuildTypeId(this);
            }

            public bool IsServiceStation { get; }

            public bool IncludeRedSignal { get; }

            public bool IncludeSignalIssue { get; }

            public bool IncludeManifest { get; }

            public bool IsAsync { get; }

            public bool HasCancellationToken { get; }

            public bool IsVoid { get; }

            public bool UseGenericReturn { get; }

            public string ReturnTypeKey { get; }

            public ImmutableArray<WagonTypeSlot> WagonTypes { get; }

            public string TypeId { get; }

            /// <summary>
            /// Builds a delegate type signature from a handler schema.
            /// </summary>
            public static DelegateTypeSignature From(StationHandlerBinding schema)
            {
                var wagonTypes = ImmutableArray.CreateBuilder<WagonTypeSlot>(schema.Wagons.Length);
                for (var i = 0; i < schema.Wagons.Length; i++)
                {
                    var wagon = schema.Wagons[i];
                    wagonTypes.Add(new WagonTypeSlot(
                        wagon.TypeDisplay,
                        wagon.IsByReference,
                        wagon.IsOptional,
                        wagon.PullTypeDisplay));
                }

                return new DelegateTypeSignature(
                    schema.IsServiceStation,
                    schema.IncludeRedSignal,
                    schema.IncludeSignalIssue,
                    schema.IncludeManifest,
                    schema.IsAsync,
                    schema.HasCancellationToken,
                    schema.ReturnShape.IsVoid,
                    schema.ReturnShape.UseGenericReturn,
                    BuildReturnTypeKey(schema.ReturnShape),
                    wagonTypes.ToImmutable());
            }

            private static string BuildReturnTypeKey(ReturnShape returnShape)
            {
                if (returnShape.IsVoid)
                {
                    return "void";
                }

                if (returnShape.IsExplicitSignalReturn)
                {
                    return ReturnTypeDisplayBuilder.SignalReturnTypeDisplay;
                }

                if (returnShape.UseGenericReturn
                    || returnShape.IsUnknown
                    || string.Equals(returnShape.ReturnTypeDisplay, "global::System.Object", StringComparison.Ordinal))
                {
                    return "object";
                }

                if (returnShape.IsValueTuple)
                {
                    return "tuple:" + HandlerFuncTypeBuilder.ResolveCanonicalTupleReturnTypeDisplay(returnShape);
                }

                return returnShape.ReturnTypeDisplay ?? "object";
            }
        }

        /// <summary>
        /// Captures one wagon parameter's type and binding flags within a delegate signature.
        /// </summary>
        private readonly struct WagonTypeSlot
        {
            /// <summary>
            /// Creates a wagon type slot from display strings and binding flags.
            /// </summary>
            public WagonTypeSlot(
                string typeDisplay,
                bool isByReference,
                bool isOptional,
                string pullTypeDisplay)
            {
                TypeDisplay = typeDisplay;
                IsByReference = isByReference;
                IsOptional = isOptional;
                PullTypeDisplay = pullTypeDisplay;
            }

            public string TypeDisplay { get; }

            public bool IsByReference { get; }

            public bool IsOptional { get; }

            public string PullTypeDisplay { get; }
        }

        /// <summary>
        /// Merges a canonical handler schema with combined return-shape metadata for emission.
        /// </summary>
        private sealed class MergedStationSchema
        {
            private readonly List<ReturnShape> _returnShapes = new List<ReturnShape>();
            private List<ChainSiteBinding> _chainBindings = new List<ChainSiteBinding>();

            /// <summary>
            /// Creates a merged schema from a canonical handler signature and delegate type id.
            /// </summary>
            public MergedStationSchema(StationHandlerBinding signature, string delegateTypeId)
            {
                Signature = signature;
                DelegateTypeId = delegateTypeId;
            }

            public StationHandlerBinding Signature { get; }

            public string DelegateTypeId { get; }

            public IReadOnlyList<ChainSiteBinding> ChainBindings => _chainBindings;

            public bool UsesChainDispatch =>
                _chainBindings.Count > 0
                && HasDistinctWagonNameSets()
                && !Signature.IsServiceStation;

            private bool HasDistinctWagonNameSets()
            {
                var wagonNameSets = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < _chainBindings.Count; i++)
                {
                    wagonNameSets.Add(FormatWagonNames(_chainBindings[i].Schema.Wagons));
                }

                return wagonNameSets.Count > 1;
            }

            public string[] ReturnMembers =>
                StationReturnMetadataBuilder.MergeReturnMemberNames(_returnShapes);

            /// <summary>
            /// Attaches chain-site bindings discovered for this delegate signature group.
            /// </summary>
            public void SetChainBindings(List<ChainSiteBinding> chainBindings)
            {
                _chainBindings = chainBindings ?? new List<ChainSiteBinding>();
            }

            /// <summary>
            /// Adds distinct return shapes from another merged schema with the same emission signature.
            /// </summary>
            public void MergeFrom(MergedStationSchema other)
            {
                if (other == null)
                {
                    return;
                }

                for (var i = 0; i < other._returnShapes.Count; i++)
                {
                    AddReturnShape(other._returnShapes[i]);
                }
            }

            /// <summary>
            /// Adds a distinct return shape to the merged metadata set.
            /// </summary>
            public void AddReturnShape(ReturnShape returnShape)
            {
                for (var i = 0; i < _returnShapes.Count; i++)
                {
                    if (ReturnShapesEqual(_returnShapes[i], returnShape))
                    {
                        return;
                    }
                }

                _returnShapes.Add(returnShape);
            }

            /// <summary>
            /// Determines whether two return shapes are equivalent for merge purposes.
            /// </summary>
            public static bool ReturnShapesEqual(ReturnShape left, ReturnShape right)
            {
                if (left.IsUnknown != right.IsUnknown
                    || left.IsVoid != right.IsVoid
                    || left.IsCargoManifest != right.IsCargoManifest
                    || left.IsValueTuple != right.IsValueTuple
                    || left.Members.Length != right.Members.Length)
                {
                    return false;
                }

                for (var i = 0; i < left.Members.Length; i++)
                {
                    if (!string.Equals(left.Members[i].Name, right.Members[i].Name, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Compares delegate type signatures for equality when grouping handler schemas.
        /// </summary>
        private sealed class DelegateTypeSignatureComparer : IEqualityComparer<DelegateTypeSignature>
        {
            public static DelegateTypeSignatureComparer Instance { get; } = new DelegateTypeSignatureComparer();

            /// <summary>
            /// Determines whether two delegate type signatures are equal.
            /// </summary>
            public bool Equals(DelegateTypeSignature x, DelegateTypeSignature y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                if (x.IsServiceStation != y.IsServiceStation
                    || x.IncludeRedSignal != y.IncludeRedSignal
                    || x.IncludeSignalIssue != y.IncludeSignalIssue
                    || x.IncludeManifest != y.IncludeManifest
                    || x.IsAsync != y.IsAsync
                    || x.HasCancellationToken != y.HasCancellationToken
                    || x.IsVoid != y.IsVoid
                    || x.UseGenericReturn != y.UseGenericReturn
                    || !string.Equals(x.ReturnTypeKey, y.ReturnTypeKey, StringComparison.Ordinal)
                    || x.WagonTypes.Length != y.WagonTypes.Length)
                {
                    return false;
                }

                for (var i = 0; i < x.WagonTypes.Length; i++)
                {
                    var left = x.WagonTypes[i];
                    var right = y.WagonTypes[i];
                    if (!string.Equals(left.TypeDisplay, right.TypeDisplay, StringComparison.Ordinal)
                        || left.IsByReference != right.IsByReference
                        || left.IsOptional != right.IsOptional
                        || !string.Equals(left.PullTypeDisplay, right.PullTypeDisplay, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Computes a hash code for a delegate type signature.
            /// </summary>
            public int GetHashCode(DelegateTypeSignature obj)
            {
                if (obj == null)
                {
                    return 0;
                }

                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + obj.IsServiceStation.GetHashCode();
                    hash = (hash * 31) + obj.IncludeRedSignal.GetHashCode();
                    hash = (hash * 31) + obj.IncludeSignalIssue.GetHashCode();
                    hash = (hash * 31) + obj.IncludeManifest.GetHashCode();
                    hash = (hash * 31) + obj.IsAsync.GetHashCode();
                    hash = (hash * 31) + obj.HasCancellationToken.GetHashCode();
                    hash = (hash * 31) + obj.IsVoid.GetHashCode();
                    hash = (hash * 31) + obj.UseGenericReturn.GetHashCode();
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.ReturnTypeKey ?? string.Empty);
                    foreach (var wagon in obj.WagonTypes)
                    {
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.TypeDisplay);
                        hash = (hash * 31) + wagon.IsByReference.GetHashCode();
                        hash = (hash * 31) + wagon.IsOptional.GetHashCode();
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.PullTypeDisplay);
                    }

                    return hash;
                }
            }
        }

        /// <summary>
        /// Builds a short hash-based identifier from a delegate type signature.
        /// </summary>
        private static string BuildTypeId(DelegateTypeSignature signature)
        {
            var builder = new StringBuilder();
            builder.Append(signature.IsServiceStation ? "S1" : "S0");
            builder.Append(signature.IncludeRedSignal ? "R1" : "R0");
            builder.Append(signature.IncludeSignalIssue ? "I1" : "I0");
            builder.Append(signature.IncludeManifest ? "M1" : "M0");
            builder.Append(signature.IsAsync ? "A1" : "A0");
            builder.Append(signature.HasCancellationToken ? "C1" : "C0");
            builder.Append(signature.IsVoid ? "V1" : "V0");
            builder.Append(signature.UseGenericReturn ? "G1" : "G0");
            builder.Append('|').Append(signature.ReturnTypeKey ?? string.Empty);
            for (var i = 0; i < signature.WagonTypes.Length; i++)
            {
                var wagon = signature.WagonTypes[i];
                builder.Append('|').Append(wagon.TypeDisplay);
                builder.Append(':').Append(wagon.IsByReference ? "R1" : "R0");
                builder.Append(':').Append(wagon.IsOptional ? "O1" : "O0");
                builder.Append(':').Append(wagon.PullTypeDisplay);
            }

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(builder.ToString());
                var hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(16);
                for (var i = 0; i < 8; i++)
                {
                    result.Append(hash[i].ToString("x2"));
                }

                return result.ToString();
            }
        }
    }
}
