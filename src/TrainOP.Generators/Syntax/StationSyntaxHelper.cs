using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
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
        /// True when <paramref name="expression"/> is the receiver of a Station / ServiceStation member access.
        /// </summary>
        internal static bool IsRouteHandlerReceiver(ExpressionSyntax expression)
        {
            return TryGetRouteHandlerMemberAccess(expression, out _);
        }

        /// <summary>
        /// When <paramref name="receiverExpression"/> is the receiver of a Station / ServiceStation call,
        /// returns that invocation.
        /// </summary>
        internal static bool TryGetRouteHandlerInvocation(
            ExpressionSyntax receiverExpression,
            out InvocationExpressionSyntax invocation)
        {
            invocation = null;
            if (!TryGetRouteHandlerMemberAccess(receiverExpression, out var memberAccess)
                || memberAccess.Parent is not InvocationExpressionSyntax parentInvocation
                || !ReferenceEquals(parentInvocation.Expression, memberAccess))
            {
                return false;
            }

            invocation = parentInvocation;
            return true;
        }

        private static bool TryGetRouteHandlerMemberAccess(
            ExpressionSyntax expression,
            out MemberAccessExpressionSyntax memberAccess)
        {
            memberAccess = null;
            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(expression);
            if (receiver.Parent is not MemberAccessExpressionSyntax access
                || !ReferenceEquals(access.Expression, receiver)
                || !IsStationOrServiceStationMethodName(access.Name.Identifier.ValueText))
            {
                return false;
            }

            memberAccess = access;
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
        /// Determines whether a type symbol is <c>TrainRoute</c> or a user-declared descendant.
        /// </summary>
        public static bool IsTrainRoute(ITypeSymbol typeSymbol)
        {
            for (var current = typeSymbol as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if (ReturnTypeDisplayHelper.EqualsTypeName(
                        current.ToDisplayString(),
                        ReturnTypeDisplayHelper.TrainRouteTypeName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when <paramref name="typeSymbol"/> is TrainRoute or an unresolved error type.
        /// </summary>
        internal static bool IsTrainRouteOrError(ITypeSymbol typeSymbol)
        {
            return IsTrainRoute(typeSymbol) || typeSymbol?.TypeKind == TypeKind.Error;
        }

        /// <summary>
        /// Determines whether a factory return type is <c>TrainRoute</c> or
        /// <c>Task&lt;TrainRoute&gt;</c> / <c>ValueTask&lt;TrainRoute&gt;</c> (async factory under <c>await</c>).
        /// </summary>
        public static bool IsTrainRouteFactoryReturnType(ITypeSymbol typeSymbol)
        {
            if (IsTrainRoute(typeSymbol))
            {
                return true;
            }

            return TryGetTaskLikeOfTrainRoute(typeSymbol, out _);
        }

        /// <summary>
        /// If <paramref name="typeSymbol"/> is <c>Task&lt;TrainRoute&gt;</c> or
        /// <c>ValueTask&lt;TrainRoute&gt;</c>, returns the element <c>TrainRoute</c> type.
        /// </summary>
        public static bool TryGetTaskLikeOfTrainRoute(
            ITypeSymbol typeSymbol,
            out ITypeSymbol trainRouteType)
        {
            trainRouteType = null;
            if (typeSymbol is not INamedTypeSymbol named
                || named.TypeArguments.Length != 1
                || !IsTrainRoute(named.TypeArguments[0]))
            {
                return false;
            }

            if (!IsTaskLikeTypeName(named.Name)
                || !IsSystemThreadingTasksNamespace(named.ContainingNamespace))
            {
                return false;
            }

            trainRouteType = named.TypeArguments[0];
            return true;
        }

        private static bool IsTaskLikeTypeName(string typeName)
        {
            return string.Equals(typeName, "Task", StringComparison.Ordinal)
                || string.Equals(typeName, "ValueTask", StringComparison.Ordinal);
        }

        private static bool IsSystemThreadingTasksNamespace(INamespaceSymbol ns)
        {
            if (ns == null || ns.IsGlobalNamespace)
            {
                return false;
            }

            return string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns the single <c>out TrainRoute</c> parameter when the method has exactly one.
        /// </summary>
        public static bool TryGetSingleOutTrainRouteParameter(
            IMethodSymbol methodSymbol,
            out IParameterSymbol outParameter)
        {
            outParameter = null;
            if (methodSymbol == null)
            {
                return false;
            }

            IParameterSymbol found = null;
            foreach (var parameter in methodSymbol.Parameters)
            {
                if (parameter.RefKind != RefKind.Out
                    || !IsTrainRoute(parameter.Type))
                {
                    continue;
                }

                if (found != null)
                {
                    return false;
                }

                found = parameter;
            }

            if (found == null)
            {
                return false;
            }

            outParameter = found;
            return true;
        }

        /// <summary>
        /// Matches an <c>out TrainRoute</c> argument at a call site to its parameter.
        /// </summary>
        public static bool TryMatchOutTrainRouteArgument(
            InvocationExpressionSyntax invocation,
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            out IParameterSymbol outParameter,
            out ArgumentSyntax outArgument)
        {
            outParameter = null;
            outArgument = null;
            if (invocation?.ArgumentList == null
                || methodSymbol == null
                || semanticModel == null)
            {
                return false;
            }

            var arguments = invocation.ArgumentList.Arguments;
            for (var i = 0; i < arguments.Count && i < methodSymbol.Parameters.Length; i++)
            {
                var parameter = methodSymbol.Parameters[i];
                if (parameter.RefKind != RefKind.Out
                    || !IsTrainRoute(parameter.Type))
                {
                    continue;
                }

                var argument = arguments[i];
                if (!argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
                {
                    continue;
                }

                outParameter = parameter;
                outArgument = argument;
                return true;
            }

            return false;
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

            // Analyzer-only compilations lack generated Station stubs, so
            // `var route = new TrainRoute().Station(...)` types the local as error.
            // Still treat the identifier as a TrainRoute receiver; chain assembly
            // validates known origins (unknown → TOP005).
            if (receiverType?.TypeKind == TypeKind.Error
                && receiverExpression is IdentifierNameSyntax)
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
            if (result.Failure is not (
                HandlerSchemaFailure.UnresolvedHandler or HandlerSchemaFailure.InvalidSchema))
            {
                return false;
            }

            handlerLocation = result.HandlerLocation
                ?? invocation.ArgumentList.Arguments[1].Expression.GetLocation();
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
                && ReturnTypeDisplayHelper.EqualsTypeName(
                    methodSymbol.ContainingType.ToDisplayString(),
                    ReturnTypeDisplayHelper.TrainRouteTypeName)
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
            return StationHandlerResolver.TryResolveHandler(expression, semanticModel, out resolved);
        }

        /// <summary>
        /// Enclosing method declaration, when <paramref name="node"/> sits inside one.
        /// </summary>
        internal static MethodDeclarationSyntax GetEnclosingMethodDeclaration(SyntaxNode node)
        {
            return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        }

        /// <summary>
        /// Symbol of the method that encloses <paramref name="node"/>.
        /// </summary>
        internal static IMethodSymbol GetEnclosingMethod(SyntaxNode node, SemanticModel semanticModel)
        {
            var methodDeclaration = GetEnclosingMethodDeclaration(node);
            if (methodDeclaration == null)
            {
                return null;
            }

            return semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        }
    }
}
