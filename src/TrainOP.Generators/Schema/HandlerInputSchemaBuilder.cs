using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Builds the unified input/output schema for data-oriented station and service-station handlers.
    /// Prefer resolving through <see cref="HandlerSchemaResolver"/> at call sites.
    /// </summary>
    internal static class HandlerInputSchemaBuilder
    {
        /// <summary>
        /// Builds a handler binding from a resolved handler symbol and optional body.
        /// </summary>
        public static StationHandlerBinding TryBuild(
            ResolvedHandler resolved,
            SemanticModel semanticModel,
            HandlerStationKind stationKind = HandlerStationKind.Station)
        {
            if (resolved?.Symbol == null)
            {
                return null;
            }

            return TryBuild(
                resolved.Symbol,
                resolved.Body,
                resolved.Location,
                resolved.Expression,
                semanticModel,
                stationKind);
        }

        private static StationHandlerBinding TryBuild(
            IMethodSymbol handlerSymbol,
            CSharpSyntaxNode body,
            Location handlerLocation,
            ExpressionSyntax handlerExpression,
            SemanticModel semanticModel,
            HandlerStationKind stationKind)
        {
            var parameters = handlerSymbol.Parameters;
            var wagons = ImmutableArray.CreateBuilder<WagonBinding>();
            var includeManifest = false;
            var includeRedSignal = false;
            var includeSignalIssue = false;
            var hasCancellationToken = false;
            var fallbackLocation = handlerLocation ?? handlerExpression?.GetLocation();

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var parameterType = parameter.Type;
                if (parameterType == null)
                {
                    return null;
                }

                if (parameter.IsDiscard)
                {
                    continue;
                }

                if (FrameworkParameterSchemaClassifier.TryClassify(parameterType, out var frameworkKind))
                {
                    switch (frameworkKind)
                    {
                        case HandlerInputKind.CancellationToken:
                            hasCancellationToken = true;
                            continue;

                        case HandlerInputKind.CargoManifest:
                            if (stationKind.IsServiceStation())
                            {
                                return null;
                            }

                            includeManifest = true;
                            continue;

                        case HandlerInputKind.RedSignal:
                            if (!stationKind.IsServiceStation())
                            {
                                break;
                            }

                            includeRedSignal = true;
                            continue;

                        case HandlerInputKind.SignalIssue:
                            if (stationKind.IsServiceStation())
                            {
                                return null;
                            }

                            includeSignalIssue = true;
                            continue;
                    }
                }

                var name = parameter.Name;
                if (!IsValidWagonParameterName(name))
                {
                    return null;
                }

                var isByReference = WagonParameterAnalyzer.IsByReference(parameter);

                var isOptional = WagonParameterAnalyzer.IsOptionalNullableValueType(parameterType, out var underlyingType);
                var pullTypeDisplay = WagonParameterAnalyzer.GetPullTypeDisplay(parameterType, underlyingType, isOptional);
                var effectiveTypeSymbol = WagonParameterAnalyzer.GetEffectiveTypeSymbol(parameterType, underlyingType, isOptional);
                var typeDisplay = ManifestWagonTypes.ToWagonParameterTypeDisplay(parameterType, underlyingType, isOptional);
                if (string.IsNullOrWhiteSpace(typeDisplay))
                {
                    return null;
                }

                var location = GetParameterLocation(handlerSymbol, handlerExpression, name) ?? fallbackLocation;
                wagons.Add(new WagonBinding(
                    name,
                    typeDisplay,
                    effectiveTypeSymbol,
                    location,
                    isByReference,
                    isOptional,
                    pullTypeDisplay));
            }

            if (stationKind.IsServiceStation()
                && wagons.Count == 0
                && includeRedSignal)
            {
                return null;
            }

            if (stationKind.IsServiceStation())
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

            if (!stationKind.IsServiceStation() && includeManifest && wagons.Count == 0)
            {
                return null;
            }

            var inputWagons = wagons.ToImmutable();
            var input = new HandlerInputParameters(
                inputWagons,
                stationKind,
                includeManifest,
                includeRedSignal,
                includeSignalIssue,
                hasCancellationToken);

            var returnShape = HandlerReturnSchemaInference.Infer(
                handlerSymbol,
                body,
                fallbackLocation,
                semanticModel,
                inputWagons);
            return new StationHandlerBinding(
                input,
                HandlerOutputParameters.From(returnShape),
                IsAsyncHandler(handlerSymbol, handlerExpression));
        }

        /// <summary>
        /// Determines whether a handler is async based on declaration syntax or return type.
        /// </summary>
        private static bool IsAsyncHandler(IMethodSymbol handlerSymbol, ExpressionSyntax handlerExpression)
        {
            if (handlerSymbol.IsAsync)
            {
                return true;
            }

            if (handlerExpression is AnonymousFunctionExpressionSyntax anonymousFunction
                && anonymousFunction.AsyncKeyword != default)
            {
                return true;
            }

            foreach (var reference in handlerSymbol.DeclaringSyntaxReferences)
            {
                var node = reference.GetSyntax();
                if (node is MethodDeclarationSyntax methodDeclaration
                    && methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
                {
                    return true;
                }

                if (node is LocalFunctionStatementSyntax localFunction
                    && localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword))
                {
                    return true;
                }
            }

            return HandlerReturnSchemaInference.IsTask(handlerSymbol.ReturnType);
        }

        /// <summary>
        /// Locates the source position of a handler parameter by name.
        /// </summary>
        private static Location GetParameterLocation(
            IMethodSymbol handlerSymbol,
            ExpressionSyntax handlerExpression,
            string parameterName)
        {
            if (handlerExpression is SimpleLambdaExpressionSyntax simpleLambda)
            {
                return simpleLambda.Parameter.Identifier.ValueText == parameterName
                    ? simpleLambda.Parameter.Identifier.GetLocation()
                    : null;
            }

            if (handlerExpression is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
            {
                foreach (var syntaxParameter in parenthesizedLambda.ParameterList.Parameters)
                {
                    if (string.Equals(syntaxParameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
                    {
                        return syntaxParameter.Identifier.GetLocation();
                    }
                }
            }

            if (handlerExpression is AnonymousMethodExpressionSyntax anonymousMethod
                && anonymousMethod.ParameterList != null)
            {
                foreach (var syntaxParameter in anonymousMethod.ParameterList.Parameters)
                {
                    if (string.Equals(syntaxParameter.Identifier.ValueText, parameterName, StringComparison.Ordinal))
                    {
                        return syntaxParameter.Identifier.GetLocation();
                    }
                }
            }

            foreach (var parameter in handlerSymbol.Parameters)
            {
                if (!string.Equals(parameter.Name, parameterName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (parameter.Locations.Length > 0)
                {
                    return parameter.Locations[0];
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
