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

            var handlerExpression = invocation.ArgumentList.Arguments[1].Expression;
            if (!StationSyntaxHelper.TryResolveHandler(handlerExpression, semanticModel, out var resolved)
                || resolved == null)
            {
                return HandlerSchemaResult.Failed(
                    HandlerSchemaFailure.UnresolvedHandler,
                    handlerLocation: handlerExpression.GetLocation());
            }

            // Cold compilation lists TrainRoute.ServiceStation as an overload candidate for any
            // .ServiceStation call. RedSignal handlers (optional CancellationToken) are true builtins;
            // data-oriented handlers (wagons + RedSignal) must still resolve so factory StationCount / adapters emit.
            if (stationKind.IsServiceStation()
                && IsLikelyBuiltinServiceStationHandler(resolved))
            {
                return HandlerSchemaResult.Failed(
                    HandlerSchemaFailure.BuiltinServiceHandler,
                    handlerLocation: resolved.Location);
            }

            // Skip the TrainRoute.* builtin-symbol check for data ServiceStation: candidates always
            // include TrainRoute.ServiceStation before generated extensions exist.
            if (!stationKind.IsServiceStation()
                && StationSyntaxHelper.IsBuiltinTrainRouteHandler(
                    invocation,
                    semanticModel,
                    stationKind.ToMethodName()))
            {
                return HandlerSchemaResult.Failed(HandlerSchemaFailure.BuiltinHandler);
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
        /// Heuristically detects built-in RedSignal service station handlers
        /// (<c>RedSignal</c> with optional <c>CargoManifest</c> / <c>CancellationToken</c>, no data-oriented wagons).
        /// </summary>
        internal static bool IsLikelyBuiltinServiceStationHandler(ResolvedHandler resolved)
        {
            if (resolved?.Symbol == null)
            {
                return false;
            }

            var parameters = resolved.Symbol.Parameters;
            if (parameters.Length == 1)
            {
                return IsRedSignalParameter(resolved, parameters[0]);
            }

            if (parameters.Length == 2
                && IsRedSignalParameter(resolved, parameters[0]))
            {
                if (FrameworkParameterSchemaClassifier.IsCancellationToken(parameters[1].Type)
                    || FrameworkParameterSchemaClassifier.IsCargoManifest(parameters[1].Type))
                {
                    return true;
                }
            }

            if (parameters.Length == 3
                && IsRedSignalParameter(resolved, parameters[0])
                && FrameworkParameterSchemaClassifier.IsCargoManifest(parameters[1].Type)
                && FrameworkParameterSchemaClassifier.IsCancellationToken(parameters[2].Type))
            {
                return true;
            }

            return false;
        }

        private static bool IsRedSignalParameter(ResolvedHandler resolved, IParameterSymbol parameter)
        {
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
                    && (string.Equals(memberAccess.Name.Identifier.ValueText, "Issue", StringComparison.Ordinal)
                        || string.Equals(memberAccess.Name.Identifier.ValueText, "Issues", StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
