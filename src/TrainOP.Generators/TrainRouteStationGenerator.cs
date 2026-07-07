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
        /// Registers syntax-driven discovery of station handlers and emits grouped extension source code.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var stationCalls = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    IsCandidateStationInvocation(node) || IsCandidateServiceStationInvocation(node),
                static (generatorContext, _) => GetRouteHandlerCall(generatorContext));

            var combined = context.CompilationProvider.Combine(stationCalls.Collect());

            context.RegisterSourceOutput(combined, static (productionContext, source) =>
            {
                var calls = source.Right;
                var groups = new Dictionary<DelegateTypeSignature, TypeSignatureGroup>(DelegateTypeSignatureComparer.Instance);

                foreach (var call in calls
                    .Where(static call => call != null)
                    .OrderBy(static call => call.Location.SourceSpan.Start))
                {
                    var typeSignature = DelegateTypeSignature.From(call.Schema);
                    if (!groups.TryGetValue(typeSignature, out var group))
                    {
                        group = new TypeSignatureGroup(typeSignature);
                        groups[typeSignature] = group;
                    }

                    group.Add(call.Schema, call.Location, productionContext);
                }

                if (groups.Count == 0)
                {
                    return;
                }

                EmitExtensions(
                    productionContext,
                    groups.Values
                        .Select(group => group.ToMerged())
                        .OrderBy(x => x.DelegateTypeId, StringComparer.Ordinal)
                        .ToImmutableArray());
            });
        }

        /// <summary>
        /// Determines whether a syntax node looks like a Station method invocation.
        /// </summary>
        private static bool IsCandidateStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, "Station");
        }

        /// <summary>
        /// Determines whether a syntax node looks like a ServiceStation method invocation.
        /// </summary>
        private static bool IsCandidateServiceStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, "ServiceStation");
        }

        /// <summary>
        /// Determines whether a syntax node is an invocation of the given route handler method name.
        /// </summary>
        private static bool IsCandidateRouteHandlerInvocation(SyntaxNode node, string methodName)
        {
            if (!(node is InvocationExpressionSyntax invocation))
            {
                return false;
            }

            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            return string.Equals(memberAccess.Name.Identifier.ValueText, methodName, StringComparison.Ordinal);
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
            if (!forServiceStation && !string.Equals(methodName, "Station", StringComparison.Ordinal))
            {
                return null;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (!IsTrainRouteReceiver(memberAccess.Expression, receiverType, semanticModel))
            {
                return null;
            }

            if (IsBuiltinTrainRouteHandler(invocation, semanticModel, methodName))
            {
                return null;
            }

            var handlerArgument = invocation.ArgumentList.Arguments[1].Expression;
            if (!StationSyntaxHelper.TryGetLambda(handlerArgument, semanticModel, out var lambdaSyntax, out var lambdaSymbol))
            {
                return null;
            }

            if (forServiceStation && IsLikelyBuiltinServiceStationHandler(lambdaSyntax, lambdaSymbol))
            {
                return null;
            }

            var schema = HandlerInputSchemaBuilder.TryBuild(lambdaSyntax, lambdaSymbol, semanticModel, forServiceStation);
            if (schema == null)
            {
                return null;
            }

            return new StationCallInfo(schema, lambdaSyntax.GetLocation());
        }

        /// <summary>
        /// Extracts handler schema metadata from a candidate Station invocation.
        /// </summary>
        private static StationCallInfo GetStationCall(GeneratorSyntaxContext context)
        {
            return GetRouteHandlerCall(context);
        }

        /// <summary>
        /// Determines whether a type symbol is TrainRoute.
        /// </summary>
        private static bool IsTrainRoute(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.TrainRoute", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether an expression is or derives from a TrainRoute receiver.
        /// </summary>
        private static bool IsTrainRouteReceiver(
            ExpressionSyntax receiverExpression,
            ITypeSymbol receiverType,
            SemanticModel semanticModel)
        {
            if (IsTrainRoute(receiverType))
            {
                return true;
            }

            if (receiverExpression is ObjectCreationExpressionSyntax objectCreation)
            {
                var typeInfo = semanticModel.GetTypeInfo(objectCreation);
                return IsTrainRoute(typeInfo.Type) || IsTrainRoute(typeInfo.ConvertedType);
            }

            if (receiverExpression is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (string.Equals(memberAccess.Name.Identifier.ValueText, "Station", StringComparison.Ordinal)
                    || string.Equals(memberAccess.Name.Identifier.ValueText, "RegisterStation", StringComparison.Ordinal)
                    || string.Equals(memberAccess.Name.Identifier.ValueText, "ServiceStation", StringComparison.Ordinal))
                {
                    return IsTrainRouteReceiver(memberAccess.Expression, semanticModel.GetTypeInfo(memberAccess.Expression).Type, semanticModel);
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether an invocation resolves to a built-in TrainRoute handler method.
        /// </summary>
        private static bool IsBuiltinTrainRouteHandler(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            string methodName)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (IsBuiltinTrainRouteMethod(symbolInfo.Symbol as IMethodSymbol, methodName))
            {
                return true;
            }

            foreach (var candidate in symbolInfo.CandidateSymbols)
            {
                if (IsBuiltinTrainRouteMethod(candidate as IMethodSymbol, methodName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a method symbol is a built-in TrainRoute handler of the given name.
        /// </summary>
        private static bool IsBuiltinTrainRouteMethod(IMethodSymbol methodSymbol, string methodName)
        {
            return methodSymbol != null
                && methodSymbol.MethodKind == MethodKind.Ordinary
                && methodSymbol.ContainingType != null
                && string.Equals(methodSymbol.ContainingType.ToDisplayString(), "TrainOP.TrainRoute", StringComparison.Ordinal)
                && string.Equals(methodSymbol.Name, methodName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Heuristically detects built-in RedSignal-only service station handlers.
        /// </summary>
        private static bool IsLikelyBuiltinServiceStationHandler(
            LambdaExpressionSyntax lambdaSyntax,
            IMethodSymbol lambdaSymbol)
        {
            var parameters = lambdaSymbol.Parameters;
            if (parameters.Length != 1)
            {
                return false;
            }

            var parameter = parameters[0];
            if (IsRedSignal(parameter.Type))
            {
                return true;
            }

            if (parameter.Type == null
                || parameter.Type.TypeKind == TypeKind.Error
                || parameter.Type.TypeKind == TypeKind.Dynamic)
            {
                return UsesRedSignalSurface(lambdaSyntax, parameter.Name);
            }

            return false;
        }

        /// <summary>
        /// Determines whether a lambda body uses RedSignal.Manifest or RedSignal.Issue surfaces.
        /// </summary>
        private static bool UsesRedSignalSurface(LambdaExpressionSyntax lambdaSyntax, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            foreach (var memberAccess in lambdaSyntax.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Expression is IdentifierNameSyntax identifier
                    && string.Equals(identifier.Identifier.ValueText, parameterName, StringComparison.Ordinal)
                    && (string.Equals(memberAccess.Name.Identifier.ValueText, "Manifest", StringComparison.Ordinal)
                        || string.Equals(memberAccess.Name.Identifier.ValueText, "Issue", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether a type symbol matches the given metadata name.
        /// </summary>
        private static bool IsNamedType(ITypeSymbol typeSymbol, string metadataName)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            return string.Equals(typeSymbol.ToDisplayString(), metadataName, StringComparison.Ordinal)
                || string.Equals(
                    typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "global::" + metadataName,
                    StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the type is RedSignal.
        /// </summary>
        private static bool IsRedSignal(ITypeSymbol typeSymbol)
        {
            return IsNamedType(typeSymbol, "TrainOP.RedSignal");
        }

        /// <summary>
        /// Emits the TrainRouteStationExtensions source file for all merged handler schemas.
        /// </summary>
        private static void EmitExtensions(SourceProductionContext context, ImmutableArray<MergedStationSchema> schemas)
        {
            var source = new StringBuilder();
            source.AppendLine("using System;");
            source.AppendLine("using System.Threading;");
            source.AppendLine("using System.Threading.Tasks;");
            source.AppendLine();
            source.AppendLine("namespace TrainOP");
            source.AppendLine("{");
            source.AppendLine("    public static class TrainRouteStationExtensions");
            source.AppendLine("    {");

            for (var i = 0; i < schemas.Length; i++)
            {
                if (i > 0)
                {
                    source.AppendLine();
                }

                EmitSchemaMembers(source, schemas[i]);
            }

            source.AppendLine("    }");
            source.AppendLine("}");

            context.AddSource("TrainRouteStation.Extensions.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// Emits delegate, metadata fields, and extension method members for one merged schema.
        /// </summary>
        private static void EmitSchemaMembers(StringBuilder source, MergedStationSchema merged)
        {
            var schema = merged.Signature;
            var delegateTypeId = merged.DelegateTypeId;
            var wagonNamesField = "WagonNames_" + delegateTypeId;
            var hasRefWagons = schema.HasRefWagons;
            string refFlagsField = null;
            if (hasRefWagons)
            {
                refFlagsField = "RefFlags_" + delegateTypeId;
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

            var returnMembers = merged.ReturnMembers;
            string returnMembersField = null;
            if (returnMembers != null)
            {
                returnMembersField = "ReturnMembers_" + delegateTypeId;
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

            var delegateName = (schema.IsServiceStation ? "TrainServiceStationHandler_" : "TrainStationHandler_") + delegateTypeId;
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("        public delegate System.Threading.Tasks.Task ").Append(delegateName).Append("(");
                }
                else
                {
                    source.Append("        public delegate System.Threading.Tasks.Task<object> ").Append(delegateName).Append("(");
                }

                EmitDelegateParameters(source, schema, useNeutralParameterNames: true);
                source.AppendLine(");");
            }
            else
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("        public delegate void ").Append(delegateName).Append("(");
                }
                else
                {
                    source.Append("        public delegate object ").Append(delegateName).Append("(");
                }

                EmitDelegateParameters(source, schema, useNeutralParameterNames: true);
                source.AppendLine(");");
            }

            source.AppendLine();

            var routeMethodName = schema.IsServiceStation ? "ServiceStation" : "Station";
            source.Append("        public static TrainRoute ").Append(routeMethodName).Append("(this TrainRoute route, string stationName, ")
                .Append(delegateName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");

            if (schema.IsServiceStation)
            {
                if (schema.IsAsync)
                {
                    source.AppendLine("            return route.ServiceStation(stationName, async (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    EmitPullWagons(source, schema);
                    EmitHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
                else
                {
                    source.AppendLine("            return route.ServiceStation(stationName, (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    EmitPullWagons(source, schema);
                    EmitHandlerInvocation(source, schema, tokenVariable: "token", redVariable: "red");
                }
            }
            else if (schema.IsAsync)
            {
                source.AppendLine("            return route.RegisterStation(stationName, async (manifest, token) =>");
                source.AppendLine("            {");
                EmitPullWagons(source, schema);
                EmitHandlerInvocation(source, schema, tokenVariable: "token", redVariable: null);
            }
            else
            {
                source.AppendLine("            return route.RegisterStation(stationName, manifest =>");
                source.AppendLine("            {");
                EmitPullWagons(source, schema);
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
                EmitToSignalCall(source, wagonNamesField, schema, returnMembersField, refFlagsField, "refLocalValues");
            }
            else
            {
                EmitToSignalCall(source, wagonNamesField, schema, returnMembersField, null, null);
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
            if (schema.IsAsync)
            {
                if (schema.ReturnShape.IsVoid)
                {
                    source.Append("                await handler(");
                    EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
                    source.AppendLine(").ConfigureAwait(false);");
                    source.AppendLine("                object stationReturn = null;");
                    return;
                }

                source.Append("                var stationReturn = await handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
                source.AppendLine(").ConfigureAwait(false);");
                return;
            }

            if (schema.ReturnShape.IsVoid)
            {
                source.Append("                handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
                source.AppendLine(");");
                source.AppendLine("                object stationReturn = null;");
                return;
            }

            source.Append("                var stationReturn = handler(");
            EmitHandlerCallArguments(source, schema, tokenVariable, redVariable);
            source.AppendLine(");");
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
            source.Append("                return StationMerge.ToSignal(manifest, stationReturn, stationName, ")
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
        /// Emits delegate parameter declarations for a handler schema.
        /// </summary>
        private static void EmitDelegateParameters(StringBuilder source, StationHandlerBinding schema, bool useNeutralParameterNames)
        {
            var needsComma = false;
            if (schema.IncludeRedSignal)
            {
                source.Append("RedSignal ").Append(useNeutralParameterNames ? "pRed" : "red");
                needsComma = true;
            }

            if (schema.IncludeSignalIssue)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                source.Append("SignalIssue ").Append(useNeutralParameterNames ? "pIssue" : "issue");
                needsComma = true;
            }

            if (schema.IncludeManifest)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                source.Append("CargoManifest ").Append(useNeutralParameterNames ? "pManifest" : "manifest");
                needsComma = true;
            }

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

                var parameterName = useNeutralParameterNames ? "p" + i : wagon.Name;
                source.Append(wagon.TypeDisplay).Append(" ").Append(parameterName);
                needsComma = true;
            }

            if (schema.HasCancellationToken)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                source.Append("CancellationToken ").Append(useNeutralParameterNames ? "pToken" : "cancellationToken");
            }
        }

        /// <summary>
        /// Emits manifest wagon pull statements for each handler input wagon.
        /// </summary>
        private static void EmitPullWagons(StringBuilder source, StationHandlerBinding schema)
        {
            WagonBindingCodegen.EmitPullWagonStatements(source, schema);
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
            public StationCallInfo(StationHandlerBinding schema, Location location)
            {
                Schema = schema;
                Location = location;
            }

            public StationHandlerBinding Schema { get; }

            public Location Location { get; }
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
            public void Add(StationHandlerBinding schema, Location location, SourceProductionContext context)
            {
                if (_canonicalSchema == null)
                {
                    _canonicalSchema = schema;
                }
                else if (!WagonNamesMatch(_canonicalSchema.Wagons, schema.Wagons))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        TrainRouteDiagnostics.ConflictingWagonNames,
                        location,
                        FormatWagonNames(schema.Wagons),
                        FormatWagonNames(_canonicalSchema.Wagons)));
                }

                AddReturnShape(schema.ReturnShape);
            }

            /// <summary>
            /// Produces a merged schema with combined return-shape metadata for code generation.
            /// </summary>
            public MergedStationSchema ToMerged()
            {
                var merged = new MergedStationSchema(_canonicalSchema, _typeSignature.TypeId);
                for (var i = 0; i < _returnShapes.Count; i++)
                {
                    merged.AddReturnShape(_returnShapes[i]);
                }

                return merged;
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
                ImmutableArray<WagonTypeSlot> wagonTypes)
            {
                IsServiceStation = isServiceStation;
                IncludeRedSignal = includeRedSignal;
                IncludeSignalIssue = includeSignalIssue;
                IncludeManifest = includeManifest;
                IsAsync = isAsync;
                HasCancellationToken = hasCancellationToken;
                IsVoid = isVoid;
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
                    wagonTypes.ToImmutable());
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

            public string[] ReturnMembers =>
                StationReturnMetadataBuilder.MergeReturnMemberNames(_returnShapes);

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
                var result = new StringBuilder(8);
                for (var i = 0; i < 4; i++)
                {
                    result.Append(hash[i].ToString("x2"));
                }

                return result.ToString();
            }
        }
    }
}
