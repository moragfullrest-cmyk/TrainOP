using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Builds the unified input schema for data-oriented station and service-station handlers.
    /// </summary>
    internal static class HandlerInputSchemaBuilder
    {
        /// <summary>
        /// Builds a handler binding from a lambda and its semantic model symbols.
        /// </summary>
        public static StationHandlerBinding TryBuild(
            LambdaExpressionSyntax lambdaSyntax,
            IMethodSymbol lambdaSymbol,
            SemanticModel semanticModel,
            bool forServiceStation = false)
        {
            var parameters = lambdaSymbol.Parameters;
            var wagons = ImmutableArray.CreateBuilder<WagonBinding>();
            var includeManifest = false;
            var includeRedSignal = false;
            var includeSignalIssue = false;
            var hasCancellationToken = false;
            var hasRefWagons = false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var parameterType = parameter.Type;
                if (parameterType == null)
                {
                    return null;
                }

                if (IsCancellationToken(parameterType))
                {
                    hasCancellationToken = true;
                    continue;
                }

                if (parameter.IsDiscard)
                {
                    continue;
                }

                if (IsCargoManifest(parameterType))
                {
                    if (forServiceStation)
                    {
                        return null;
                    }

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
                    return null;
                }

                var name = parameter.Name;
                if (!IsValidWagonParameterName(name))
                {
                    return null;
                }

                var isByReference = WagonParameterAnalyzer.IsByReference(parameter);
                if (isByReference)
                {
                    hasRefWagons = true;
                }

                var isOptional = WagonParameterAnalyzer.IsOptionalNullableValueType(parameterType, out var underlyingType);
                var pullTypeDisplay = WagonParameterAnalyzer.GetPullTypeDisplay(parameterType, underlyingType, isOptional);
                var effectiveTypeSymbol = WagonParameterAnalyzer.GetEffectiveTypeSymbol(parameterType, underlyingType, isOptional);
                var typeDisplay = ManifestWagonTypes.ToWagonParameterTypeDisplay(parameterType, underlyingType, isOptional);
                if (string.IsNullOrWhiteSpace(typeDisplay))
                {
                    return null;
                }

                var location = GetParameterLocation(lambdaSyntax, name) ?? lambdaSyntax.GetLocation();
                wagons.Add(new WagonBinding(
                    name,
                    typeDisplay,
                    effectiveTypeSymbol,
                    location,
                    isByReference,
                    isOptional,
                    pullTypeDisplay));
            }

            if (forServiceStation
                && wagons.Count == 0
                && includeRedSignal)
            {
                return null;
            }

            if (forServiceStation)
            {
                if (!includeRedSignal)
                {
                    return null;
                }

                for (var i = 0; i < wagons.Count; i++)
                {
                    if (!wagons[i].IsByReference)
                    {
                        return null;
                    }
                }
            }

            if (!forServiceStation && includeManifest && wagons.Count == 0)
            {
                return null;
            }

            var inputWagons = wagons.ToImmutable();
            var returnShape = HandlerReturnInference.Infer(lambdaSyntax, lambdaSymbol, semanticModel, inputWagons);
            return new StationHandlerBinding(
                inputWagons,
                includeManifest,
                IsAsyncLambda(lambdaSyntax, lambdaSymbol),
                hasCancellationToken,
                returnShape,
                forServiceStation,
                includeRedSignal,
                includeSignalIssue,
                hasRefWagons);
        }

        /// <summary>
        /// Determines whether a lambda is async based on syntax or return type.
        /// </summary>
        private static bool IsAsyncLambda(LambdaExpressionSyntax lambdaSyntax, IMethodSymbol lambdaSymbol)
        {
            if (lambdaSyntax.AsyncKeyword != default)
            {
                return true;
            }

            var returnType = lambdaSymbol.ReturnType;
            return HandlerReturnInference.IsTask(returnType);
        }

        /// <summary>
        /// Determines whether the type is CargoManifest.
        /// </summary>
        private static bool IsCargoManifest(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.CargoManifest", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the type is CancellationToken.
        /// </summary>
        private static bool IsCancellationToken(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (string.Equals(typeSymbol.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal)
                || string.Equals(typeSymbol.ToDisplayString(), "CancellationToken", StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Threading.CancellationToken", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the type is RedSignal.
        /// </summary>
        private static bool IsRedSignal(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.RedSignal", StringComparison.Ordinal);
        }

        /// <summary>
        /// Determines whether the type is SignalIssue.
        /// </summary>
        private static bool IsSignalIssue(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.SignalIssue", StringComparison.Ordinal);
        }

        /// <summary>
        /// Locates the source position of a lambda parameter by name.
        /// </summary>
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

        /// <summary>
        /// Determines whether a name is a valid wagon parameter identifier.
        /// </summary>
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
    }
}
