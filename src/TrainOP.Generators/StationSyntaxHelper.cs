using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
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
                if (IsRouteExtensionMethodName(memberAccess.Name.Identifier.ValueText))
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
            stationName = null;
            handlerLocation = null;
            handlerBinding = null;

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

            if (!TryResolveHandler(invocation.ArgumentList.Arguments[1].Expression, semanticModel, out var resolved)
                || resolved == null)
            {
                return false;
            }

            handlerBinding = HandlerInputSchemaBuilder.TryBuild(resolved, semanticModel, forServiceStation);
            if (handlerBinding == null)
            {
                return false;
            }

            handlerLocation = resolved.Location;
            stationName = invocation.ArgumentList.Arguments[0].ToString().Trim('"');
            if (invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal)
            {
                stationName = literal.Token.ValueText;
            }

            return true;
        }

        /// <summary>
        /// Determines whether a data-oriented Station/ServiceStation call has an unsupported handler argument (TOP009).
        /// </summary>
        public static bool TryGetUnsupportedStationHandler(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            out Location handlerLocation)
        {
            handlerLocation = null;
            if (invocation?.ArgumentList == null || invocation.ArgumentList.Arguments.Count != 2)
            {
                return false;
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            var forServiceStation = string.Equals(methodName, "ServiceStation", StringComparison.Ordinal);
            if (!forServiceStation
                && !string.Equals(methodName, "Station", StringComparison.Ordinal))
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (!IsTrainRouteReceiver(memberAccess.Expression, receiverType, semanticModel))
            {
                return false;
            }

            if (IsBuiltinTrainRouteHandler(invocation, semanticModel, methodName))
            {
                return false;
            }

            var handlerArgument = invocation.ArgumentList.Arguments[1].Expression;
            if (!TryResolveHandler(handlerArgument, semanticModel, out var resolved) || resolved == null)
            {
                handlerLocation = handlerArgument.GetLocation();
                return true;
            }

            if (forServiceStation && IsLikelyBuiltinServiceStationHandler(resolved))
            {
                return false;
            }

            if (HandlerInputSchemaBuilder.TryBuild(resolved, semanticModel, forServiceStation) != null)
            {
                return false;
            }

            handlerLocation = handlerArgument.GetLocation();
            return true;
        }

        /// <summary>
        /// Determines whether an invocation resolves to a built-in TrainRoute handler method.
        /// </summary>
        internal static bool IsBuiltinTrainRouteHandler(
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

        /// <summary>
        /// Heuristically detects built-in RedSignal-only service station handlers.
        /// </summary>
        internal static bool IsLikelyBuiltinServiceStationHandler(ResolvedHandler resolved)
        {
            if (resolved?.Symbol == null)
            {
                return false;
            }

            var parameters = resolved.Symbol.Parameters;
            if (parameters.Length != 1)
            {
                return false;
            }

            var parameter = parameters[0];
            if (IsRedSignalType(parameter.Type))
            {
                return true;
            }

            if (parameter.Type == null
                || parameter.Type.TypeKind == TypeKind.Error
                || parameter.Type.TypeKind == TypeKind.Dynamic)
            {
                return UsesRedSignalSurface(resolved, parameter.Name);
            }

            return false;
        }

        private static bool IsRedSignalType(ITypeSymbol typeSymbol)
        {
            return typeSymbol != null
                && string.Equals(typeSymbol.ToDisplayString(), "TrainOP.RedSignal", StringComparison.Ordinal);
        }

        private static bool UsesRedSignalSurface(ResolvedHandler resolved, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            var root = (SyntaxNode)resolved.Body ?? resolved.Expression;
            if (root == null)
            {
                return false;
            }

            foreach (var memberAccess in root.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
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
        /// Determines whether a method name is a generated or built-in route extension used in chains.
        /// </summary>
        private static bool IsRouteExtensionMethodName(string methodName)
        {
            return string.Equals(methodName, "Station", StringComparison.Ordinal)
                || string.Equals(methodName, "RegisterStation", StringComparison.Ordinal)
                || string.Equals(methodName, "ServiceStation", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves a data-oriented handler argument to a method symbol in the current compilation.
        /// Supports lambdas, anonymous methods, and method groups / local functions with source declarations.
        /// </summary>
        public static bool TryResolveHandler(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out ResolvedHandler resolved)
        {
            resolved = null;
            if (expression == null || semanticModel == null)
            {
                return false;
            }

            expression = UnwrapHandlerExpression(expression);

            if (expression is ParenthesizedLambdaExpressionSyntax
                || expression is SimpleLambdaExpressionSyntax)
            {
                var lambdaSyntax = (LambdaExpressionSyntax)expression;
                var lambdaSymbol = semanticModel.GetSymbolInfo(lambdaSyntax).Symbol as IMethodSymbol;
                if (lambdaSymbol == null)
                {
                    return false;
                }

                resolved = new ResolvedHandler(
                    HandlerKind.Lambda,
                    lambdaSymbol,
                    GetAnonymousFunctionBody(lambdaSyntax),
                    lambdaSyntax.GetLocation(),
                    expression);
                return true;
            }

            if (expression is AnonymousMethodExpressionSyntax anonymousMethod)
            {
                var anonymousSymbol = semanticModel.GetSymbolInfo(anonymousMethod).Symbol as IMethodSymbol;
                if (anonymousSymbol == null)
                {
                    return false;
                }

                resolved = new ResolvedHandler(
                    HandlerKind.AnonymousMethod,
                    anonymousSymbol,
                    GetAnonymousFunctionBody(anonymousMethod),
                    anonymousMethod.GetLocation(),
                    expression);
                return true;
            }

            if (!TryResolveMethodGroup(expression, semanticModel, out var methodSymbol))
            {
                return false;
            }

            if (!IsInspectableInCompilation(methodSymbol, semanticModel.Compilation))
            {
                return false;
            }

            resolved = new ResolvedHandler(
                HandlerKind.MethodGroup,
                methodSymbol,
                TryGetDeclaredMethodBody(methodSymbol),
                methodSymbol.Locations.Length > 0
                    ? methodSymbol.Locations[0]
                    : expression.GetLocation(),
                expression);
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

            if (!TryResolveHandler(expression, semanticModel, out var resolved)
                || resolved.Kind != HandlerKind.Lambda)
            {
                return false;
            }

            lambdaSyntax = resolved.Expression as LambdaExpressionSyntax;
            lambdaSymbol = resolved.Symbol;
            return lambdaSyntax != null && lambdaSymbol != null;
        }

        /// <summary>
        /// Builds a station handler binding from a resolved handler.
        /// </summary>
        public static StationHandlerBinding TryBuildHandlerBinding(
            ResolvedHandler resolved,
            SemanticModel semanticModel,
            bool forServiceStation = false)
        {
            return HandlerInputSchemaBuilder.TryBuild(resolved, semanticModel, forServiceStation);
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
            if (lambdaSyntax == null || lambdaSymbol == null)
            {
                return null;
            }

            var resolved = new ResolvedHandler(
                HandlerKind.Lambda,
                lambdaSymbol,
                GetAnonymousFunctionBody(lambdaSyntax),
                lambdaSyntax.GetLocation(),
                lambdaSyntax);
            return HandlerInputSchemaBuilder.TryBuild(resolved, semanticModel, forServiceStation);
        }

        private static ExpressionSyntax UnwrapHandlerExpression(ExpressionSyntax expression)
        {
            while (expression != null)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is CastExpressionSyntax cast)
                {
                    expression = cast.Expression;
                    continue;
                }

                break;
            }

            return expression;
        }

        private static bool TryResolveMethodGroup(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out IMethodSymbol methodSymbol)
        {
            methodSymbol = null;
            var symbolInfo = semanticModel.GetSymbolInfo(expression);
            if (TryPickUniqueMethod(symbolInfo.Symbol, symbolInfo.CandidateSymbols, out methodSymbol))
            {
                return true;
            }

            if (expression is IdentifierNameSyntax
                || expression is MemberAccessExpressionSyntax
                || expression is GenericNameSyntax
                || expression is MemberBindingExpressionSyntax)
            {
                var members = semanticModel.GetMemberGroup(expression);
                return TryPickUniqueMethod(null, members, out methodSymbol);
            }

            return false;
        }

        private static bool TryPickUniqueMethod(
            ISymbol primary,
            System.Collections.Immutable.ImmutableArray<ISymbol> candidates,
            out IMethodSymbol methodSymbol)
        {
            methodSymbol = null;

            if (IsHandlerMethodCandidate(primary as IMethodSymbol))
            {
                methodSymbol = (IMethodSymbol)primary;
                return true;
            }

            if (candidates.IsDefaultOrEmpty)
            {
                return false;
            }

            IMethodSymbol chosen = null;
            foreach (var candidate in candidates)
            {
                if (!IsHandlerMethodCandidate(candidate as IMethodSymbol))
                {
                    continue;
                }

                var method = (IMethodSymbol)candidate;
                if (chosen == null)
                {
                    chosen = method;
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(chosen, method))
                {
                    methodSymbol = null;
                    return false;
                }
            }

            methodSymbol = chosen;
            return methodSymbol != null;
        }

        private static bool IsHandlerMethodCandidate(IMethodSymbol method)
        {
            if (method == null)
            {
                return false;
            }

            return method.MethodKind == MethodKind.Ordinary
                || method.MethodKind == MethodKind.LocalFunction
                || method.MethodKind == MethodKind.ExplicitInterfaceImplementation;
        }

        private static bool IsInspectableInCompilation(IMethodSymbol method, Compilation compilation)
        {
            if (method == null || compilation == null)
            {
                return false;
            }

            if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            {
                return false;
            }

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.SyntaxTree != null
                    && compilation.ContainsSyntaxTree(reference.SyntaxTree))
                {
                    return true;
                }
            }

            return false;
        }

        private static CSharpSyntaxNode GetAnonymousFunctionBody(AnonymousFunctionExpressionSyntax syntax)
        {
            return syntax?.Body;
        }

        private static CSharpSyntaxNode TryGetDeclaredMethodBody(IMethodSymbol methodSymbol)
        {
            foreach (var reference in methodSymbol.DeclaringSyntaxReferences)
            {
                var node = reference.GetSyntax();
                if (node is MethodDeclarationSyntax methodDeclaration)
                {
                    return (CSharpSyntaxNode)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
                }

                if (node is LocalFunctionStatementSyntax localFunction)
                {
                    return (CSharpSyntaxNode)localFunction.Body ?? localFunction.ExpressionBody;
                }
            }

            return null;
        }
    }
}
