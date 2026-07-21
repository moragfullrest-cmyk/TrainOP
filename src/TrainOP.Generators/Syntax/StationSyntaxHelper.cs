using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using TrainOP.Generators.Handlers;
namespace TrainOP.Generators
{
    /// <summary>
    /// Parses and classifies TrainRoute station and service-station invocation syntax.
    /// </summary>
    internal static class StationSyntaxHelper
    {
        /// <summary>
        /// Determines whether a syntax node looks like a Station or ServiceStation invocation.
        /// </summary>
        public static bool IsCandidateRouteHandlerInvocation(SyntaxNode node)
        {
            return node is InvocationExpressionSyntax invocation
                && TryParseRouteHandlerInvocation(invocation, out _, out _);
        }

        /// <summary>
        /// Determines whether a syntax node looks like a Station method invocation.
        /// </summary>
        public static bool IsCandidateStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, HandlerStationKind.Station);
        }

        /// <summary>
        /// Determines whether a syntax node looks like a ServiceStation method invocation.
        /// </summary>
        public static bool IsCandidateServiceStationInvocation(SyntaxNode node)
        {
            return IsCandidateRouteHandlerInvocation(node, HandlerStationKind.ServiceStation);
        }

        /// <summary>
        /// Parses a route handler invocation into its station kind and member access.
        /// </summary>
        public static bool TryParseRouteHandlerInvocation(
            InvocationExpressionSyntax invocation,
            out HandlerStationKind stationKind,
            out MemberAccessExpressionSyntax memberAccess)
        {
            stationKind = default;
            memberAccess = null;

            if (invocation?.Expression is not MemberAccessExpressionSyntax candidateMemberAccess)
            {
                return false;
            }

            if (invocation.ArgumentList?.Arguments.Count != 2)
            {
                return false;
            }

            if (!HandlerStationKindExtensions.TryParseMethodName(
                    candidateMemberAccess.Name.Identifier.ValueText,
                    out stationKind))
            {
                return false;
            }

            memberAccess = candidateMemberAccess;
            return true;
        }

        /// <summary>
        /// True when <paramref name="methodName"/> is <c>Station</c> or <c>ServiceStation</c>.
        /// </summary>
        public static bool IsStationOrServiceStationMethodName(string methodName)
        {
            return HandlerStationKindExtensions.IsStationOrServiceStationMethodName(methodName);
        }

        /// <summary>
        /// True when <paramref name="methodName"/> is <c>Station</c>.
        /// </summary>
        public static bool IsStationMethodName(string methodName)
        {
            return HandlerStationKindExtensions.TryParseMethodName(methodName, out var kind)
                && kind == HandlerStationKind.Station;
        }

        /// <summary>
        /// True when <paramref name="methodName"/> is <c>ServiceStation</c>.
        /// </summary>
        public static bool IsServiceStationMethodName(string methodName)
        {
            return HandlerStationKindExtensions.TryParseMethodName(methodName, out var kind)
                && kind == HandlerStationKind.ServiceStation;
        }

        /// <summary>
        /// Determines whether an invocation has the syntactic shape of a route handler call
        /// (member access, matching method name, exactly two arguments).
        /// </summary>
        public static bool MatchesRouteHandlerShape(
            InvocationExpressionSyntax invocation,
            HandlerStationKind stationKind,
            out MemberAccessExpressionSyntax memberAccess)
        {
            return MatchesRouteHandlerShape(invocation, stationKind.ToMethodName(), out memberAccess);
        }

        /// <summary>
        /// Determines whether an invocation has the syntactic shape of a route handler call
        /// (member access, matching method name, exactly two arguments).
        /// </summary>
        public static bool MatchesRouteHandlerShape(
            InvocationExpressionSyntax invocation,
            string methodName,
            out MemberAccessExpressionSyntax memberAccess)
        {
            memberAccess = null;
            if (!TryParseRouteHandlerInvocation(invocation, out var stationKind, out var parsedMemberAccess))
            {
                return false;
            }

            if (!string.Equals(stationKind.ToMethodName(), methodName, StringComparison.Ordinal))
            {
                return false;
            }

            memberAccess = parsedMemberAccess;
            return true;
        }

        /// <summary>
        /// Determines whether an invocation has the syntactic shape of Station or ServiceStation.
        /// </summary>
        public static bool MatchesStationOrServiceStationShape(
            InvocationExpressionSyntax invocation,
            out MemberAccessExpressionSyntax memberAccess)
        {
            return TryParseRouteHandlerInvocation(invocation, out _, out memberAccess);
        }

        /// <summary>
        /// Determines whether a syntax node is an invocation of the given route handler method name.
        /// </summary>
        private static bool IsCandidateRouteHandlerInvocation(SyntaxNode node, HandlerStationKind stationKind)
        {
            return node is InvocationExpressionSyntax invocation
                && TryParseRouteHandlerInvocation(invocation, out var parsedKind, out _)
                && parsedKind == stationKind;
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
            receiverExpression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(receiverExpression);
            if (receiverExpression == null)
            {
                return false;
            }

            if (IsTrainRoute(receiverType))
            {
                return true;
            }

            if (receiverExpression is ObjectCreationExpressionSyntax objectCreation)
            {
                var typeInfo = semanticModel.GetTypeInfo(objectCreation);
                return IsTrainRoute(typeInfo.Type) || IsTrainRoute(typeInfo.ConvertedType);
            }

            if (receiverExpression is ConditionalExpressionSyntax conditional)
            {
                return IsTrainRouteReceiver(
                        conditional.WhenTrue,
                        semanticModel.GetTypeInfo(conditional.WhenTrue).Type,
                        semanticModel)
                    && IsTrainRouteReceiver(
                        conditional.WhenFalse,
                        semanticModel.GetTypeInfo(conditional.WhenFalse).Type,
                        semanticModel);
            }

            if (receiverExpression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                return IsTrainRouteReceiver(
                        binary.Left,
                        semanticModel.GetTypeInfo(binary.Left).Type,
                        semanticModel)
                    && IsTrainRouteReceiver(
                        binary.Right,
                        semanticModel.GetTypeInfo(binary.Right).Type,
                        semanticModel);
            }

            if (receiverExpression is SwitchExpressionSyntax switchExpression)
            {
                if (switchExpression.Arms.Count == 0)
                {
                    return false;
                }

                foreach (var arm in switchExpression.Arms)
                {
                    if (!IsTrainRouteReceiver(
                            arm.Expression,
                            semanticModel.GetTypeInfo(arm.Expression).Type,
                            semanticModel))
                    {
                        return false;
                    }
                }

                return true;
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
                HandlerStationKind.Station,
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
                HandlerStationKind.ServiceStation,
                out stationName,
                out handlerLocation,
                out handlerBinding);
        }

        /// <summary>
        /// Attempts to parse a data-oriented route handler invocation.
        /// </summary>
        private static bool TryGetDataRouteHandlerInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            HandlerStationKind stationKind,
            out string stationName,
            out Location handlerLocation,
            out StationHandlerBinding handlerBinding)
        {
            stationName = null;
            handlerLocation = null;
            handlerBinding = null;

            if (!TryParseRouteHandlerInvocation(invocation, out var parsedKind, out var memberAccess)
                || parsedKind != stationKind)
            {
                return false;
            }

            var result = HandlerSchemaResolver.ResolveParsedInvocation(
                invocation,
                semanticModel,
                stationKind,
                memberAccess);
            if (!result.IsSuccess)
            {
                return false;
            }

            handlerBinding = result.Schema;
            handlerLocation = result.HandlerLocation;
            stationName = result.StationName;
            return true;
        }

        /// <summary>
        /// Resolves a compile-time station name when possible (literal, const, nameof, literal initializer).
        /// </summary>
        internal static bool TryResolveStationName(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out string stationName)
        {
            stationName = null;
            if (expression == null || semanticModel == null)
            {
                return false;
            }

            var constant = semanticModel.GetConstantValue(expression);
            if (constant.HasValue
                && constant.Value is string constantString
                && !string.IsNullOrEmpty(constantString))
            {
                stationName = constantString;
                return true;
            }

            if (expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                stationName = literal.Token.ValueText;
                return !string.IsNullOrEmpty(stationName);
            }

            if (TryGetStringLiteralInitializerValue(expression, semanticModel, out stationName))
            {
                return !string.IsNullOrEmpty(stationName);
            }

            return false;
        }

        /// <summary>
        /// Resolves a station name for chain analysis; falls back to identifier or source text.
        /// </summary>
        internal static string ResolveStationNameForAnalysis(
            ExpressionSyntax expression,
            SemanticModel semanticModel)
        {
            if (TryResolveStationName(expression, semanticModel, out var resolved))
            {
                return resolved;
            }

            if (expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.ValueText;
            }

            return expression.ToString().Trim('"');
        }

        private static bool TryGetStringLiteralInitializerValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out string value)
        {
            value = null;
            var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
            if (symbol is not ILocalSymbol and not IFieldSymbol)
            {
                return false;
            }

            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is not VariableDeclaratorSyntax declarator)
                {
                    continue;
                }

                if (declarator.Initializer?.Value is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    value = literal.Token.ValueText;
                    return true;
                }
            }

            return false;
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
            if (!TryParseRouteHandlerInvocation(invocation, out var stationKind, out var memberAccess))
            {
                return false;
            }

            var result = HandlerSchemaResolver.ResolveParsedInvocation(
                invocation,
                semanticModel,
                stationKind,
                memberAccess);
            switch (result.Failure)
            {
                case HandlerSchemaFailure.None:
                case HandlerSchemaFailure.InvalidShape:
                case HandlerSchemaFailure.NotTrainRouteReceiver:
                case HandlerSchemaFailure.BuiltinHandler:
                case HandlerSchemaFailure.BuiltinServiceHandler:
                    return false;

                case HandlerSchemaFailure.UnresolvedHandler:
                case HandlerSchemaFailure.InvalidSchema:
                    handlerLocation = result.HandlerLocation
                        ?? invocation.ArgumentList.Arguments[1].Expression.GetLocation();
                    return true;

                default:
                    return false;
            }
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
        /// Determines whether a method name is a generated or built-in route extension used in chains.
        /// </summary>
        private static bool IsRouteExtensionMethodName(string methodName)
        {
            return StationSyntaxHelper.IsStationMethodName(methodName)
                || string.Equals(methodName, "RegisterStation", StringComparison.Ordinal)
                || StationSyntaxHelper.IsServiceStationMethodName(methodName);
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
