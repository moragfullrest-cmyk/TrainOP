using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    internal static class StationSyntaxHelper
    {
        public static bool IsCandidateStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, "Station");
        }

        public static bool IsCandidateServiceStationInvocation(SyntaxNode node)
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

        public static bool IsTrainRoute(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.TrainRoute", StringComparison.Ordinal);
        }

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

        public static bool IsTrainRouteCreation(ObjectCreationExpressionSyntax objectCreation, SemanticModel semanticModel)
        {
            var typeInfo = semanticModel.GetTypeInfo(objectCreation);
            return IsTrainRoute(typeInfo.Type) || IsTrainRoute(typeInfo.ConvertedType);
        }

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

            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
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

        private static bool IsRedSignal(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.RedSignal", StringComparison.Ordinal);
        }

        private static bool IsSignalIssue(ITypeSymbol typeSymbol)
        {
            return string.Equals(typeSymbol.ToDisplayString(), "TrainOP.SignalIssue", StringComparison.Ordinal);
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
    }
}
