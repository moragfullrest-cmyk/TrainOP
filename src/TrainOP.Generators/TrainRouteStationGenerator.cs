using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    [Generator]
    public sealed class TrainRouteStationGenerator : IIncrementalGenerator
    {
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

        private static bool IsCandidateStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, "Station");
        }

        private static bool IsCandidateServiceStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, "ServiceStation");
        }

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
            if (!TryGetLambda(handlerArgument, semanticModel, out var lambdaSyntax, out var lambdaSymbol))
            {
                return null;
            }

            if (forServiceStation && IsLikelyBuiltinServiceStationHandler(lambdaSyntax, lambdaSymbol))
            {
                return null;
            }

            var schema = TryBuildSchema(lambdaSyntax, lambdaSymbol, semanticModel, forServiceStation);
            if (schema == null)
            {
                return null;
            }

            return new StationCallInfo(schema, lambdaSyntax.GetLocation());
        }

        private static StationCallInfo GetStationCall(GeneratorSyntaxContext context)
        {
            return GetRouteHandlerCall(context);
        }

        private static bool IsTrainRoute(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.TrainRoute", StringComparison.Ordinal);
        }

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
                    || string.Equals(memberAccess.Name.Identifier.ValueText, "AttachStation", StringComparison.Ordinal)
                    || string.Equals(memberAccess.Name.Identifier.ValueText, "ServiceStation", StringComparison.Ordinal))
                {
                    return IsTrainRouteReceiver(memberAccess.Expression, semanticModel.GetTypeInfo(memberAccess.Expression).Type, semanticModel);
                }
            }

            return false;
        }

        private static bool TryGetLambda(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out LambdaExpressionSyntax lambdaSyntax,
            out IMethodSymbol lambdaSymbol)
        {
            lambdaSyntax = null;
            lambdaSymbol = null;

            switch (expression)
            {
                case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                    lambdaSyntax = parenthesizedLambda;
                    break;
                case SimpleLambdaExpressionSyntax simpleLambda:
                    lambdaSyntax = simpleLambda;
                    break;
                default:
                    return false;
            }

            lambdaSymbol = semanticModel.GetSymbolInfo(lambdaSyntax).Symbol as IMethodSymbol;
            return lambdaSymbol != null;
        }

        private static HandlerSchema TryBuildSchema(
            LambdaExpressionSyntax lambdaSyntax,
            IMethodSymbol lambdaSymbol,
            SemanticModel semanticModel,
            bool forServiceStation = false)
        {
            var parameters = lambdaSymbol.Parameters;
            var wagons = new List<WagonBinding>();
            var includeManifest = false;
            var includeRedSignal = false;
            var includeSignalIssue = false;
            var hasCancellationToken = false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var parameterType = parameter.Type;
                if (parameter.IsDiscard)
                {
                    continue;
                }

                if (parameterType == null)
                {
                    return null;
                }

                if (IsCargoManifest(parameterType))
                {
                    includeManifest = true;
                    continue;
                }

                if (forServiceStation && IsRedSignal(parameterType))
                {
                    includeRedSignal = true;
                    continue;
                }

                if (forServiceStation && IsSignalIssue(parameterType))
                {
                    includeSignalIssue = true;
                    continue;
                }

                if (IsCancellationToken(parameterType))
                {
                    hasCancellationToken = true;
                    continue;
                }

                var name = parameter.Name;
                var location = GetParameterLocation(lambdaSyntax, name) ?? lambdaSyntax.GetLocation();
                var isByReference = WagonParameterAnalyzer.IsByReference(parameter);
                var isOptional = WagonParameterAnalyzer.IsOptionalNullableValueType(parameterType, out var underlyingType);
                var pullTypeDisplay = WagonParameterAnalyzer.GetPullTypeDisplay(parameterType, underlyingType, isOptional);
                var effectiveTypeSymbol = WagonParameterAnalyzer.GetEffectiveTypeSymbol(parameterType, underlyingType, isOptional);
                var typeDisplay = ManifestWagonTypes.ToWagonParameterTypeDisplay(parameterType, underlyingType, isOptional);
                if (string.IsNullOrWhiteSpace(typeDisplay))
                {
                    return null;
                }

                wagons.Add(new WagonBinding(
                    name,
                    typeDisplay,
                    effectiveTypeSymbol,
                    location,
                    isByReference,
                    isOptional,
                    pullTypeDisplay));
            }

            for (var i = 0; i < wagons.Count; i++)
            {
                if (!IsValidWagonParameterName(wagons[i].Name))
                {
                    return null;
                }
            }

            if (forServiceStation
                && wagons.Count == 0
                && !includeManifest
                && !includeSignalIssue
                && includeRedSignal)
            {
                return null;
            }

            if (!forServiceStation && includeManifest && wagons.Count == 0)
            {
                return null;
            }

            var returnShape = HandlerReturnInference.Infer(
                lambdaSyntax,
                lambdaSymbol,
                semanticModel,
                wagons.ToImmutableArray());

            return new HandlerSchema(
                wagons.ToImmutableArray(),
                includeManifest,
                isAsync: IsAsyncLambda(lambdaSyntax, lambdaSymbol),
                hasCancellationToken,
                returnShape,
                forServiceStation,
                includeRedSignal,
                includeSignalIssue);
        }

        private static bool IsAsyncLambda(LambdaExpressionSyntax lambdaSyntax, IMethodSymbol lambdaSymbol)
        {
            if (lambdaSyntax.AsyncKeyword != default)
            {
                return true;
            }

            var returnType = lambdaSymbol.ReturnType as INamedTypeSymbol;
            return returnType != null
                && returnType.IsGenericType
                && string.Equals(returnType.ConstructedFrom.ToDisplayString(), "System.Threading.Tasks.Task", StringComparison.Ordinal);
        }

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

        private static bool IsBuiltinTrainRouteMethod(IMethodSymbol methodSymbol, string methodName)
        {
            return methodSymbol != null
                && methodSymbol.MethodKind == MethodKind.Ordinary
                && methodSymbol.ContainingType != null
                && string.Equals(methodSymbol.ContainingType.ToDisplayString(), "TrainOP.TrainRoute", StringComparison.Ordinal)
                && string.Equals(methodSymbol.Name, methodName, StringComparison.Ordinal);
        }

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

        private static bool IsCargoManifest(ITypeSymbol typeSymbol)
        {
            return IsNamedType(typeSymbol, "TrainOP.CargoManifest");
        }

        private static bool IsCancellationToken(ITypeSymbol typeSymbol)
        {
            return IsNamedType(typeSymbol, "System.Threading.CancellationToken");
        }

        private static bool IsRedSignal(ITypeSymbol typeSymbol)
        {
            return IsNamedType(typeSymbol, "TrainOP.RedSignal");
        }

        private static bool IsSignalIssue(ITypeSymbol typeSymbol)
        {
            return IsNamedType(typeSymbol, "TrainOP.SignalIssue");
        }

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

            var tupleOrdinals = merged.TupleOrdinals;
            string tupleOrdinalsField = null;
            if (tupleOrdinals != null)
            {
                tupleOrdinalsField = "TupleOrdinals_" + delegateTypeId;
                source.Append("        private static readonly int[] ").Append(tupleOrdinalsField).Append(" = new int[] { ");
                for (var i = 0; i < tupleOrdinals.Length; i++)
                {
                    source.Append(tupleOrdinals[i]);
                    if (i < tupleOrdinals.Length - 1)
                    {
                        source.Append(", ");
                    }
                }

                source.AppendLine(" };");
            }

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
                source.Append("        public delegate System.Threading.Tasks.Task<object> ").Append(delegateName).Append("(");
                EmitDelegateParameters(source, schema, useNeutralParameterNames: true);
                source.AppendLine(");");
            }
            else
            {
                source.Append("        public delegate object ").Append(delegateName).Append("(");
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
                    source.Append("                var stationReturn = await handler(");
                    EmitHandlerCallArguments(source, schema, tokenVariable: "token", redVariable: "red");
                    source.AppendLine(").ConfigureAwait(false);");
                }
                else
                {
                    source.AppendLine("            return route.ServiceStation(stationName, (red, token) =>");
                    source.AppendLine("            {");
                    source.AppendLine("                var manifest = red.Manifest;");
                    EmitPullWagons(source, schema);
                    source.Append("                var stationReturn = handler(");
                    EmitHandlerCallArguments(source, schema, tokenVariable: "token", redVariable: "red");
                    source.AppendLine(");");
                }
            }
            else if (schema.IsAsync)
            {
                source.AppendLine("            return route.AttachStation(stationName, async (manifest, token) =>");
                source.AppendLine("            {");
                EmitPullWagons(source, schema);
                source.Append("                var stationReturn = await handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable: "token", redVariable: null);
                source.AppendLine(").ConfigureAwait(false);");
            }
            else
            {
                source.AppendLine("            return route.AttachStation(stationName, manifest =>");
                source.AppendLine("            {");
                EmitPullWagons(source, schema);
                source.Append("                var stationReturn = handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable: null, redVariable: null);
                source.AppendLine(");");
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
                EmitToSignalCall(source, wagonNamesField, schema, tupleOrdinalsField, returnMembersField, refFlagsField, "refLocalValues");
            }
            else
            {
                EmitToSignalCall(source, wagonNamesField, schema, tupleOrdinalsField, returnMembersField, null, null);
            }
            source.AppendLine("            });");
            source.AppendLine("        }");
        }

        private static void EmitToSignalCall(
            StringBuilder source,
            string wagonNamesField,
            HandlerSchema schema,
            string tupleOrdinalsField,
            string returnMembersField,
            string refFlagsField,
            string refLocalValuesExpression)
        {
            source.Append("                return StationMerge.ToSignal(manifest, stationReturn, stationName, ")
                .Append(wagonNamesField)
                .Append(", ")
                .Append(schema.RemoveOmittedRegularInputs ? "true" : "false")
                .Append(", ")
                .Append(tupleOrdinalsField ?? "null")
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

        private static void EmitDelegateParameters(StringBuilder source, HandlerSchema schema, bool useNeutralParameterNames)
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

        private static void EmitPullWagons(StringBuilder source, HandlerSchema schema)
        {
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                var wagon = schema.Wagons[i];
                if (wagon.IsOptional)
                {
                    source.Append("                var ").Append(wagon.Name).Append(" = manifest.HasWagon(\"")
                        .Append(Escape(wagon.Name))
                        .Append("\") ? manifest.PullWagon<")
                        .Append(wagon.PullTypeDisplay)
                        .Append(">(\"")
                        .Append(Escape(wagon.Name))
                        .Append("\") : default(")
                        .Append(wagon.TypeDisplay)
                        .AppendLine(");");
                }
                else
                {
                    source.Append("                var ").Append(wagon.Name).Append(" = manifest.PullWagon<")
                        .Append(wagon.PullTypeDisplay)
                        .Append(">(\"")
                        .Append(Escape(wagon.Name))
                        .AppendLine("\");");
                }
            }
        }

        private static void EmitHandlerCallArguments(
            StringBuilder source,
            HandlerSchema schema,
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

        private static Location GetParameterLocation(LambdaExpressionSyntax lambdaSyntax, string parameterName)
        {
            if (lambdaSyntax is SimpleLambdaExpressionSyntax simpleLambda)
            {
                return simpleLambda.Parameter.Identifier.ValueText == parameterName
                    ? simpleLambda.Parameter.Identifier.GetLocation()
                    : null;
            }

            if (lambdaSyntax is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
            {
                foreach (var syntaxParameter in parenthesizedLambda.ParameterList.Parameters)
                {
                    if (string.Equals(syntaxParameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
                    {
                        return syntaxParameter.Identifier.GetLocation();
                    }
                }
            }

            return null;
        }

        private static bool IsValidWagonParameterName(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                return false;
            }

            if (string.Equals(wagonName, "_", StringComparison.Ordinal))
            {
                return false;
            }

            if (!SyntaxFacts.IsValidIdentifier(wagonName))
            {
                return false;
            }

            return !SyntaxFacts.IsKeywordKind(SyntaxFacts.GetKeywordKind(wagonName));
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private sealed class StationCallInfo
        {
            public StationCallInfo(HandlerSchema schema, Location location)
            {
                Schema = schema;
                Location = location;
            }

            public HandlerSchema Schema { get; }

            public Location Location { get; }
        }

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

        private sealed class TypeSignatureGroup
        {
            private readonly DelegateTypeSignature _typeSignature;
            private readonly List<ReturnShape> _returnShapes = new List<ReturnShape>();
            private HandlerSchema _canonicalSchema;

            public TypeSignatureGroup(DelegateTypeSignature typeSignature)
            {
                _typeSignature = typeSignature;
            }

            public void Add(HandlerSchema schema, Location location, SourceProductionContext context)
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

            public MergedStationSchema ToMerged()
            {
                var merged = new MergedStationSchema(_canonicalSchema, _typeSignature.TypeId);
                for (var i = 0; i < _returnShapes.Count; i++)
                {
                    merged.AddReturnShape(_returnShapes[i]);
                }

                return merged;
            }

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

        private sealed class DelegateTypeSignature
        {
            public DelegateTypeSignature(
                bool isServiceStation,
                bool includeRedSignal,
                bool includeSignalIssue,
                bool includeManifest,
                bool isAsync,
                bool hasCancellationToken,
                ImmutableArray<WagonTypeSlot> wagonTypes)
            {
                IsServiceStation = isServiceStation;
                IncludeRedSignal = includeRedSignal;
                IncludeSignalIssue = includeSignalIssue;
                IncludeManifest = includeManifest;
                IsAsync = isAsync;
                HasCancellationToken = hasCancellationToken;
                WagonTypes = wagonTypes;
                TypeId = BuildTypeId(this);
            }

            public bool IsServiceStation { get; }

            public bool IncludeRedSignal { get; }

            public bool IncludeSignalIssue { get; }

            public bool IncludeManifest { get; }

            public bool IsAsync { get; }

            public bool HasCancellationToken { get; }

            public ImmutableArray<WagonTypeSlot> WagonTypes { get; }

            public string TypeId { get; }

            public static DelegateTypeSignature From(HandlerSchema schema)
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
                    wagonTypes.ToImmutable());
            }
        }

        private readonly struct WagonTypeSlot
        {
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

        private sealed class MergedStationSchema
        {
            private readonly List<ReturnShape> _returnShapes = new List<ReturnShape>();

            public MergedStationSchema(HandlerSchema signature, string delegateTypeId)
            {
                Signature = signature;
                DelegateTypeId = delegateTypeId;
            }

            public HandlerSchema Signature { get; }

            public string DelegateTypeId { get; }

            public int[] TupleOrdinals =>
                StationReturnMetadataBuilder.MergeTupleElementOrdinals(Signature.Wagons, _returnShapes);

            public string[] ReturnMembers =>
                StationReturnMetadataBuilder.MergeReturnMemberNames(_returnShapes);

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

            public static bool ReturnShapesEqual(ReturnShape left, ReturnShape right)
            {
                if (left.IsUnknown != right.IsUnknown
                    || left.IsCargoManifest != right.IsCargoManifest
                    || left.IsValueTuple != right.IsValueTuple
                    || left.IsUnnamedValueTuple != right.IsUnnamedValueTuple
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

        private sealed class HandlerSchema
        {
            public HandlerSchema(
                ImmutableArray<WagonBinding> wagons,
                bool includeManifest,
                bool isAsync,
                bool hasCancellationToken,
                ReturnShape returnShape,
                bool isServiceStation = false,
                bool includeRedSignal = false,
                bool includeSignalIssue = false)
            {
                Wagons = wagons;
                IncludeManifest = includeManifest;
                IsAsync = isAsync;
                HasCancellationToken = hasCancellationToken;
                ReturnShape = returnShape;
                IsServiceStation = isServiceStation;
                IncludeRedSignal = includeRedSignal;
                IncludeSignalIssue = includeSignalIssue;
                HasRefWagons = wagons.Any(w => w.IsByReference);
            }

            public ImmutableArray<WagonBinding> Wagons { get; }

            public bool IncludeManifest { get; }

            public bool IsAsync { get; }

            public bool HasCancellationToken { get; }

            public bool HasRefWagons { get; }

            public ReturnShape ReturnShape { get; }

            public bool IsServiceStation { get; }

            public bool IncludeRedSignal { get; }

            public bool IncludeSignalIssue { get; }

            public bool RemoveOmittedRegularInputs => Wagons.Length > 0;
        }

        private sealed class DelegateTypeSignatureComparer : IEqualityComparer<DelegateTypeSignature>
        {
            public static DelegateTypeSignatureComparer Instance { get; } = new DelegateTypeSignatureComparer();

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

        private static string BuildTypeId(DelegateTypeSignature signature)
        {
            var builder = new StringBuilder();
            builder.Append(signature.IsServiceStation ? "S1" : "S0");
            builder.Append(signature.IncludeRedSignal ? "R1" : "R0");
            builder.Append(signature.IncludeSignalIssue ? "I1" : "I0");
            builder.Append(signature.IncludeManifest ? "M1" : "M0");
            builder.Append(signature.IsAsync ? "A1" : "A0");
            builder.Append(signature.HasCancellationToken ? "C1" : "C0");
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
