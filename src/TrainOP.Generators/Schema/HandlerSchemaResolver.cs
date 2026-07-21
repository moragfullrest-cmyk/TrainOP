using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;
using TrainOP.Generators.Handlers;
namespace TrainOP.Generators
{
    /// <summary>
    /// Single entry point for resolving a handler's full input/output schema from syntax.
    /// </summary>
    internal static class HandlerSchemaResolver
    {
        /// <summary>
        /// Resolves a Station or ServiceStation invocation into a full handler schema.
        /// </summary>
        public static HandlerSchemaResult ResolveInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            HandlerStationKind stationKind)
        {
            if (!StationSyntaxHelper.TryParseRouteHandlerInvocation(
                    invocation,
                    out var parsedKind,
                    out var memberAccess)
                || parsedKind != stationKind)
            {
                return HandlerSchemaResult.Failed(HandlerSchemaFailure.InvalidShape);
            }

            return ResolveParsedInvocation(invocation, semanticModel, stationKind, memberAccess);
        }

        /// <summary>
        /// Resolves a Station or ServiceStation invocation that was already parsed by
        /// <see cref="StationSyntaxHelper.TryParseRouteHandlerInvocation"/>.
        /// </summary>
        internal static HandlerSchemaResult ResolveParsedInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            HandlerStationKind stationKind,
            MemberAccessExpressionSyntax memberAccess)
        {
            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (!StationSyntaxHelper.IsTrainRouteReceiver(memberAccess.Expression, receiverType, semanticModel))
            {
                return HandlerSchemaResult.Failed(HandlerSchemaFailure.NotTrainRouteReceiver);
            }

            if (StationSyntaxHelper.IsBuiltinTrainRouteHandler(invocation, semanticModel, stationKind.ToMethodName()))
            {
                return HandlerSchemaResult.Failed(HandlerSchemaFailure.BuiltinHandler);
            }

            var handlerExpression = invocation.ArgumentList.Arguments[1].Expression;
            if (!StationSyntaxHelper.TryResolveHandler(handlerExpression, semanticModel, out var resolved)
                || resolved == null)
            {
                return HandlerSchemaResult.Failed(
                    HandlerSchemaFailure.UnresolvedHandler,
                    handlerLocation: handlerExpression.GetLocation());
            }

            if (stationKind.IsServiceStation()
                && IsLikelyBuiltinServiceStationHandler(resolved))
            {
                return HandlerSchemaResult.Failed(
                    HandlerSchemaFailure.BuiltinServiceHandler,
                    handlerLocation: resolved.Location);
            }

            var stationName = StationSyntaxHelper.ResolveStationNameForAnalysis(
                invocation.ArgumentList.Arguments[0].Expression,
                semanticModel);
            var schemaResult = ResolveHandler(resolved, semanticModel, stationKind);
            if (!schemaResult.IsSuccess)
            {
                return schemaResult;
            }

            return HandlerSchemaResult.Success(
                schemaResult.Schema,
                resolved.Location,
                stationName);
        }

        /// <summary>
        /// Builds the full handler schema from an already-resolved handler symbol.
        /// </summary>
        public static HandlerSchemaResult ResolveHandler(
            ResolvedHandler resolved,
            SemanticModel semanticModel,
            HandlerStationKind stationKind)
        {
            if (resolved?.Symbol == null)
            {
                return HandlerSchemaResult.Failed(HandlerSchemaFailure.UnresolvedHandler);
            }

            if (stationKind.IsServiceStation()
                && IsLikelyBuiltinServiceStationHandler(resolved))
            {
                return HandlerSchemaResult.Failed(
                    HandlerSchemaFailure.BuiltinServiceHandler,
                    handlerLocation: resolved.Location);
            }

            var schema = HandlerInputSchemaBuilder.TryBuild(
                resolved,
                semanticModel,
                stationKind);
            if (schema == null)
            {
                return HandlerSchemaResult.Failed(
                    HandlerSchemaFailure.InvalidSchema,
                    handlerLocation: resolved.Location);
            }

            return HandlerSchemaResult.Success(schema, resolved.Location);
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
            if (FrameworkParameterSchemaClassifier.IsRedSignal(parameter.Type))
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
    }
}
