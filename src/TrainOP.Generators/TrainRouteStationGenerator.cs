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
                static (node, _) => IsCandidateStationInvocation(node),
                static (generatorContext, _) => GetStationCall(generatorContext));

            var combined = context.CompilationProvider.Combine(stationCalls.Collect());

            context.RegisterSourceOutput(combined, static (productionContext, source) =>
            {
                var calls = source.Right;
                var schemas = new Dictionary<HandlerSchema, Location>(HandlerSchemaComparer.Instance);

                foreach (var call in calls)
                {
                    if (call == null)
                    {
                        continue;
                    }

                    if (!schemas.ContainsKey(call.Schema))
                    {
                        schemas[call.Schema] = call.Location;
                    }
                }

                if (schemas.Count == 0)
                {
                    return;
                }

                EmitExtensions(
                    productionContext,
                    schemas.Keys.OrderBy(x => x.SchemaId, StringComparer.Ordinal).ToImmutableArray());
            });
        }

        private static bool IsCandidateStationInvocation(SyntaxNode node)
        {
            if (!(node is InvocationExpressionSyntax invocation))
            {
                return false;
            }

            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            return string.Equals(memberAccess.Name.Identifier.ValueText, "Station", StringComparison.Ordinal);
        }

        private static StationCallInfo GetStationCall(GeneratorSyntaxContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.ArgumentList.Arguments.Count != 2)
            {
                return null;
            }

            var semanticModel = context.SemanticModel;
            var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (!IsTrainRouteReceiver(memberAccess.Expression, receiverType, semanticModel))
            {
                return null;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "Station", StringComparison.Ordinal))
            {
                return null;
            }

            var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol != null
                && methodSymbol.MethodKind == MethodKind.Ordinary
                && methodSymbol.ContainingType != null
                && string.Equals(methodSymbol.ContainingType.ToDisplayString(), "TrainOP.TrainRoute", StringComparison.Ordinal)
                && string.Equals(methodSymbol.Name, "Station", StringComparison.Ordinal))
            {
                return null;
            }

            var handlerArgument = invocation.ArgumentList.Arguments[1].Expression;
            if (!TryGetLambda(handlerArgument, semanticModel, out var lambdaSyntax, out var lambdaSymbol))
            {
                return null;
            }

            var schema = TryBuildSchema(lambdaSyntax, lambdaSymbol);
            if (schema == null)
            {
                return null;
            }

            return new StationCallInfo(schema, lambdaSyntax.GetLocation());
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
                    || string.Equals(memberAccess.Name.Identifier.ValueText, "AttachStation", StringComparison.Ordinal))
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

        private static HandlerSchema TryBuildSchema(LambdaExpressionSyntax lambdaSyntax, IMethodSymbol lambdaSymbol)
        {
            var parameters = lambdaSymbol.Parameters;
            var wagons = new List<WagonBinding>();
            var includeManifest = false;
            var hasCancellationToken = false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var parameterType = parameter.Type;
                if (parameterType == null)
                {
                    return null;
                }

                var typeDisplay = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (IsCargoManifest(parameterType))
                {
                    includeManifest = true;
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

            if (includeManifest && wagons.Count == 0)
            {
                return null;
            }

            return new HandlerSchema(
                wagons.ToImmutableArray(),
                includeManifest,
                IsAsyncLambda(lambdaSyntax, lambdaSymbol),
                hasCancellationToken);
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

        private static bool IsCargoManifest(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.CargoManifest", StringComparison.Ordinal);
        }

        private static bool IsCancellationToken(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal);
        }

        private static void EmitExtensions(SourceProductionContext context, ImmutableArray<HandlerSchema> schemas)
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

        private static void EmitSchemaMembers(StringBuilder source, HandlerSchema schema)
        {
            var schemaId = schema.SchemaId;
            var wagonNamesField = "WagonNames_" + schemaId;
            var hasRefWagons = schema.HasRefWagons;
            string refFlagsField = null;
            if (hasRefWagons)
            {
                refFlagsField = "RefFlags_" + schemaId;
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
            source.AppendLine();

            var delegateName = "TrainStationHandler_" + schemaId;
            if (schema.IsAsync)
            {
                source.Append("        public delegate System.Threading.Tasks.Task<object> ").Append(delegateName).Append("(");
                EmitDelegateParameters(source, schema);
                source.AppendLine(");");
            }
            else
            {
                source.Append("        public delegate object ").Append(delegateName).Append("(");
                EmitDelegateParameters(source, schema);
                source.AppendLine(");");
            }

            source.AppendLine();

            source.Append("        public static TrainRoute Station(this TrainRoute route, string stationName, ")
                .Append(delegateName)
                .AppendLine(" handler)");
            source.AppendLine("        {");
            source.AppendLine("            if (route == null) throw new ArgumentNullException(nameof(route));");
            source.AppendLine("            if (handler == null) throw new ArgumentNullException(nameof(handler));");

            if (schema.IsAsync)
            {
                source.AppendLine("            return route.AttachStation(stationName, async (manifest, token) =>");
                source.AppendLine("            {");
                EmitPullCars(source, schema);
                source.Append("                var stationReturn = await handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable: "token");
                source.AppendLine(").ConfigureAwait(false);");
            }
            else
            {
                source.AppendLine("            return route.AttachStation(stationName, manifest =>");
                source.AppendLine("            {");
                EmitPullCars(source, schema);
                source.Append("                var stationReturn = handler(");
                EmitHandlerCallArguments(source, schema, tokenVariable: null);
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
                source.Append("                return StationMerge.ToSignal(manifest, stationReturn, stationName, ")
                    .Append(wagonNamesField)
                    .Append(", ")
                    .Append(schema.RemoveOmittedRegularInputs ? "true" : "false")
                    .Append(", ")
                    .Append(refFlagsField)
                    .AppendLine(", refLocalValues);");
            }
            else
            {
                source.Append("                return StationMerge.ToSignal(manifest, stationReturn, stationName, ")
                    .Append(wagonNamesField)
                    .Append(", ")
                    .Append(schema.RemoveOmittedRegularInputs ? "true" : "false")
                    .AppendLine(");");
            }
            source.AppendLine("            });");
            source.AppendLine("        }");
        }

        private static void EmitDelegateParameters(StringBuilder source, HandlerSchema schema)
        {
            var needsComma = false;
            if (schema.IncludeManifest)
            {
                source.Append("CargoManifest manifest");
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

                source.Append(wagon.TypeDisplay).Append(" ").Append(wagon.Name);
                needsComma = true;
            }

            if (schema.HasCancellationToken)
            {
                if (needsComma)
                {
                    source.Append(", ");
                }

                source.Append("CancellationToken cancellationToken");
            }
        }

        private static void EmitPullCars(StringBuilder source, HandlerSchema schema)
        {
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                var wagon = schema.Wagons[i];
                if (wagon.IsOptional)
                {
                    source.Append("                var ").Append(wagon.Name).Append(" = manifest.HasCar(\"")
                        .Append(Escape(wagon.Name))
                        .Append("\") ? manifest.PullCar<")
                        .Append(wagon.PullTypeDisplay)
                        .Append(">(\"")
                        .Append(Escape(wagon.Name))
                        .Append("\") : default(")
                        .Append(wagon.TypeDisplay)
                        .AppendLine(");");
                }
                else
                {
                    source.Append("                var ").Append(wagon.Name).Append(" = manifest.PullCar<")
                        .Append(wagon.PullTypeDisplay)
                        .Append(">(\"")
                        .Append(Escape(wagon.Name))
                        .AppendLine("\");");
                }
            }
        }

        private static void EmitHandlerCallArguments(StringBuilder source, HandlerSchema schema, string tokenVariable)
        {
            var needsComma = false;
            if (schema.IncludeManifest)
            {
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

        private sealed class HandlerSchema
        {
            public HandlerSchema(
                ImmutableArray<WagonBinding> wagons,
                bool includeManifest,
                bool isAsync,
                bool hasCancellationToken)
            {
                Wagons = wagons;
                IncludeManifest = includeManifest;
                IsAsync = isAsync;
                HasCancellationToken = hasCancellationToken;
                HasRefWagons = wagons.Any(w => w.IsByReference);
                SchemaId = BuildSchemaId(this);
            }

            public ImmutableArray<WagonBinding> Wagons { get; }

            public bool IncludeManifest { get; }

            public bool IsAsync { get; }

            public bool HasCancellationToken { get; }

            public bool HasRefWagons { get; }

            public bool RemoveOmittedRegularInputs => Wagons.Length > 0;

            public string SchemaId { get; }
        }

        private sealed class HandlerSchemaComparer : IEqualityComparer<HandlerSchema>
        {
            public static HandlerSchemaComparer Instance { get; } = new HandlerSchemaComparer();

            public bool Equals(HandlerSchema x, HandlerSchema y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                if (x.IncludeManifest != y.IncludeManifest
                    || x.IsAsync != y.IsAsync
                    || x.HasCancellationToken != y.HasCancellationToken
                    || x.Wagons.Length != y.Wagons.Length)
                {
                    return false;
                }

                for (var i = 0; i < x.Wagons.Length; i++)
                {
                    var left = x.Wagons[i];
                    var right = y.Wagons[i];
                    if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                        || !string.Equals(left.TypeDisplay, right.TypeDisplay, StringComparison.Ordinal)
                        || left.IsByReference != right.IsByReference
                        || left.IsOptional != right.IsOptional
                        || !string.Equals(left.PullTypeDisplay, right.PullTypeDisplay, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(HandlerSchema obj)
            {
                if (obj == null)
                {
                    return 0;
                }

                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + obj.IncludeManifest.GetHashCode();
                    hash = (hash * 31) + obj.IsAsync.GetHashCode();
                    hash = (hash * 31) + obj.HasCancellationToken.GetHashCode();
                    foreach (var wagon in obj.Wagons)
                    {
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.Name);
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.TypeDisplay);
                        hash = (hash * 31) + wagon.IsByReference.GetHashCode();
                        hash = (hash * 31) + wagon.IsOptional.GetHashCode();
                        hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.PullTypeDisplay);
                    }

                    return hash;
                }
            }
        }

        private static string BuildSchemaId(HandlerSchema schema)
        {
            var builder = new StringBuilder();
            builder.Append(schema.IncludeManifest ? "M1" : "M0");
            builder.Append(schema.IsAsync ? "A1" : "A0");
            builder.Append(schema.HasCancellationToken ? "C1" : "C0");
            for (var i = 0; i < schema.Wagons.Length; i++)
            {
                var wagon = schema.Wagons[i];
                builder.Append('|').Append(wagon.Name).Append(':').Append(wagon.TypeDisplay);
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
