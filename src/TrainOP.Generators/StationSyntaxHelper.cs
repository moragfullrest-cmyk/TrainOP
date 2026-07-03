using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Parses and classifies TrainRoute station and service-station invocation syntax.
    /// </summary>
    internal static class StationSyntaxHelper
    {
        /// <summary>
        /// Determines whether a syntax node looks like a Station method invocation.
        /// </summary>
        public static bool IsCandidateStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, "Station");
        }

        /// <summary>
        /// Determines whether a syntax node looks like a ServiceStation method invocation.
        /// </summary>
        public static bool IsCandidateServiceStationInvocation(SyntaxNode node)
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
        /// Determines whether a type symbol is TrainRoute.
        /// </summary>
        public static bool IsTrainRoute(ITypeSymbol typeSymbol)
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
        public static bool IsTrainRouteReceiver(
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
                    return IsTrainRouteReceiver(
                        memberAccess.Expression,
                        semanticModel.GetTypeInfo(memberAccess.Expression).Type,
                        semanticModel);
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether an object creation expression constructs a TrainRoute.
        /// </summary>
        public static bool IsTrainRouteCreation(ObjectCreationExpressionSyntax objectCreation, SemanticModel semanticModel)
        {
            var typeInfo = semanticModel.GetTypeInfo(objectCreation);
            return IsTrainRoute(typeInfo.Type) || IsTrainRoute(typeInfo.ConvertedType);
        }

        /// <summary>
        /// Attempts to parse a data-oriented Station invocation into handler binding metadata.
        /// </summary>
        public static bool TryGetDataStationInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            out string stationName,
            out Location handlerLocation,
            out StationHandlerBinding handlerBinding)
        {
            return TryGetDataRouteHandlerInvocation(
                invocation,
                semanticModel,
                "Station",
                forServiceStation: false,
                out stationName,
                out handlerLocation,
                out handlerBinding);
        }

        /// <summary>
        /// Attempts to parse a data-oriented ServiceStation invocation into handler binding metadata.
        /// </summary>
        public static bool TryGetDataServiceStationInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            out string stationName,
            out Location handlerLocation,
            out StationHandlerBinding handlerBinding)
        {
            return TryGetDataRouteHandlerInvocation(
                invocation,
                semanticModel,
                "ServiceStation",
                forServiceStation: true,
                out stationName,
                out handlerLocation,
                out handlerBinding);
        }

        /// <summary>
        /// Attempts to parse a data-oriented route handler invocation of the given method name.
        /// </summary>
        private static bool TryGetDataRouteHandlerInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            string methodName,
            bool forServiceStation,
            out string stationName,
            out Location handlerLocation,
            out StationHandlerBinding handlerBinding)
        {
            stationName = null;
            handlerLocation = null;
            handlerBinding = null;

            if (invocation.ArgumentList.Arguments.Count != 2)
            {
                return false;
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, methodName, StringComparison.Ordinal))
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (!IsTrainRouteReceiver(memberAccess.Expression, receiverType, semanticModel))
            {
                return false;
            }

            var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (methodSymbol != null
                && methodSymbol.MethodKind == MethodKind.Ordinary
                && methodSymbol.ContainingType != null
                && string.Equals(methodSymbol.ContainingType.ToDisplayString(), "TrainOP.TrainRoute", StringComparison.Ordinal)
                && string.Equals(methodSymbol.Name, methodName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryGetLambda(invocation.ArgumentList.Arguments[1].Expression, semanticModel, out var lambdaSyntax, out var lambdaSymbol))
            {
                return false;
            }

            handlerBinding = TryBuildHandlerBinding(lambdaSyntax, lambdaSymbol, semanticModel, forServiceStation);
            if (handlerBinding == null)
            {
                return false;
            }

            handlerLocation = lambdaSyntax.GetLocation();
            stationName = invocation.ArgumentList.Arguments[0].ToString().Trim('"');
            if (invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal)
            {
                stationName = literal.Token.ValueText;
            }

            return true;
        }

        /// <summary>
        /// Resolves a lambda expression and its method symbol from a handler argument.
        /// </summary>
        public static bool TryGetLambda(
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

        /// <summary>
        /// Builds a station handler binding from a lambda and its semantic model symbols.
        /// </summary>
        public static StationHandlerBinding TryBuildHandlerBinding(
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
                if (!IsValidWagonParameterName(name))
                {
                    return null;
                }

                var isByReference = WagonParameterAnalyzer.IsByReference(parameter);
                var isOptional = WagonParameterAnalyzer.IsOptionalNullableValueType(parameterType, out var underlyingType);
                var typeDisplay = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var pullTypeDisplay = WagonParameterAnalyzer.GetPullTypeDisplay(parameterType, underlyingType, isOptional);
                var effectiveTypeSymbol = WagonParameterAnalyzer.GetEffectiveTypeSymbol(parameterType, underlyingType, isOptional);
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

            var returnShape = HandlerReturnInference.Infer(lambdaSyntax, lambdaSymbol, semanticModel, wagons.ToImmutable());
            return new StationHandlerBinding(
                wagons.ToImmutable(),
                includeManifest,
                IsAsyncLambda(lambdaSyntax, lambdaSymbol),
                hasCancellationToken,
                returnShape,
                forServiceStation,
                includeRedSignal,
                includeSignalIssue);
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

            var returnType = lambdaSymbol.ReturnType as INamedTypeSymbol;
            return returnType != null
                && returnType.IsGenericType
                && string.Equals(returnType.ConstructedFrom.ToDisplayString(), "System.Threading.Tasks.Task", StringComparison.Ordinal);
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
            return string.Equals(typeSymbol.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal);
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
