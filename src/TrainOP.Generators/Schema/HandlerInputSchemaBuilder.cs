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
            var includeSignalIssues = false;
            var includeVisitJournal = false;
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
                            if (!stationKind.IsServiceStation())
                            {
                                return null;
                            }

                            includeSignalIssue = true;
                            continue;

                        case HandlerInputKind.SignalIssues:
                            if (!stationKind.IsServiceStation())
                            {
                                return null;
                            }

                            includeSignalIssues = true;
                            continue;

                        case HandlerInputKind.VisitJournal:
                            if (!stationKind.IsServiceStation())
                            {
                                return null;
                            }

                            includeVisitJournal = true;
                            continue;
                    }
                }

                var name = parameter.Name;
                if (!IsValidWagonParameterName(name))
                {
                    return null;
                }

                var isByReference = WagonParameterMetadata.IsByReference(parameter);
                var isOut = WagonParameterMetadata.IsOut(parameter);
                var isRefReadonly = WagonParameterMetadata.IsRefReadonly(parameter);
                var isIn = WagonParameterMetadata.IsIn(parameter);
                var isParams = WagonParameterMetadata.IsParams(parameter)
                    || WagonParameterMetadata.EnclosingMethodDeclaresParams(handlerExpression, name);

                var option = WagonParameterMetadata.Classify(parameter, semanticModel);
                var isOptional = option.IsOptional;
                var underlyingType = option.UnderlyingType;
                var pullTypeDisplay = WagonParameterMetadata.GetPullTypeDisplay(parameterType, underlyingType, isOptional);
                var effectiveTypeSymbol = WagonParameterMetadata.GetEffectiveTypeSymbol(parameterType, underlyingType, isOptional);
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
                    pullTypeDisplay,
                    isOut,
                    isRefReadonly,
                    isIn,
                    isParams,
                    option.FallbackExpression,
                    option.HasNonConstantDefault));
            }

            if (stationKind.IsServiceStation())
            {
                // RedSignal (+ optional CargoManifest / CancellationToken) with no wagons is the built-in escape hatch.
                // A visit journal is a generated slot, so that shape stays on the data-oriented path.
                if (wagons.Count == 0 && !includeVisitJournal)
                {
                    return null;
                }

                if (!includeRedSignal && !includeSignalIssue && !includeSignalIssues && !includeVisitJournal)
                {
                    return null;
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
                includeSignalIssues,
                includeVisitJournal,
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
                IsAsyncHandler(handlerSymbol, handlerExpression),
                TryGetStraightExpression(body, semanticModel, handlerSymbol));
        }

        /// <summary>
        /// Returns the expression text when the handler body is a single expression without route signals,
        /// captured variables, or symbols the generated segment method cannot see.
        /// </summary>
        private static string TryGetStraightExpression(
            CSharpSyntaxNode body,
            SemanticModel semanticModel,
            IMethodSymbol handlerSymbol)
        {
            ExpressionSyntax expression = null;
            if (body is ArrowExpressionClauseSyntax arrow)
            {
                expression = arrow.Expression;
            }
            else if (body is ExpressionSyntax node)
            {
                expression = node;
            }

            if (expression == null || semanticModel == null)
            {
                return null;
            }

            var text = expression.ToFullString();
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf("RailwaySignals", StringComparison.Ordinal) >= 0)
            {
                return null;
            }

            var dataFlow = semanticModel.AnalyzeDataFlow(expression);
            if (!dataFlow.Succeeded || dataFlow.Captured.Length > 0 || !IsPasteableExpression(expression, semanticModel, handlerSymbol))
            {
                return null;
            }

            return text.Trim();
        }

        /// <summary>
        /// True when every referenced symbol is a handler parameter, declared inside the expression,
        /// or a type or member in the <c>System</c> namespace that <c>using System</c> can see.
        /// </summary>
        private static bool IsPasteableExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            IMethodSymbol handlerSymbol)
        {
            foreach (var node in expression.DescendantNodesAndSelf())
            {
                if (!(node is IdentifierNameSyntax) && !(node is GenericNameSyntax))
                {
                    continue;
                }

                if (node is IdentifierNameSyntax identifier && IsDeclarationName(identifier))
                {
                    continue;
                }

                var symbol = semanticModel.GetSymbolInfo(node).Symbol;
                if (symbol == null)
                {
                    if (node is IdentifierNameSyntax projected
                        && projected.Parent is AnonymousObjectMemberDeclaratorSyntax)
                    {
                        continue;
                    }

                    return false;
                }

                if (symbol.Kind == SymbolKind.ErrorType)
                {
                    return false;
                }

                if (IsDeclaredInside(symbol, expression)
                    || IsHandlerParameter(symbol, handlerSymbol)
                    || IsAnonymousTypeMember(symbol)
                    || IsSystemNamespaceSymbol(symbol))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// The name in <c>new { amount = 1 }</c> declares a member. <c>new { amount }</c> still names a value.
        /// </summary>
        private static bool IsDeclarationName(IdentifierNameSyntax identifier)
        {
            return identifier.Parent is NameEqualsSyntax;
        }

        private static bool IsDeclaredInside(ISymbol symbol, ExpressionSyntax expression)
        {
            var references = symbol.DeclaringSyntaxReferences;
            for (var i = 0; i < references.Length; i++)
            {
                var syntax = references[i].GetSyntax();
                if (syntax.SyntaxTree == expression.SyntaxTree && expression.Span.Contains(syntax.Span))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHandlerParameter(ISymbol symbol, IMethodSymbol handlerSymbol)
        {
            if (handlerSymbol == null || !(symbol is IParameterSymbol))
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, handlerSymbol);
        }

        private static bool IsAnonymousTypeMember(ISymbol symbol)
        {
            return symbol.ContainingType != null && symbol.ContainingType.IsAnonymousType;
        }

        /// <summary>
        /// The generated file imports <c>System</c> only, so <c>System.Linq</c> and user namespaces stay hops.
        /// </summary>
        private static bool IsSystemNamespaceSymbol(ISymbol symbol)
        {
            if (symbol is INamespaceSymbol namespaceSymbol)
            {
                return namespaceSymbol.IsGlobalNamespace
                    || string.Equals(namespaceSymbol.ToDisplayString(), "System", StringComparison.Ordinal);
            }

            var containing = symbol.ContainingNamespace;
            return containing != null
                && string.Equals(containing.ToDisplayString(), "System", StringComparison.Ordinal);
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

            return HandlerReturnTypeShape.IsTask(handlerSymbol.ReturnType);
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
