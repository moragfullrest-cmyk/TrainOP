using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Stage 1a entry: resolve station / service-station call sites into
    /// <see cref="StationHandlerBinding"/> without changing resolver semantics.
    /// </summary>
    internal static class StationSignatureStage
    {
        /// <summary>
        /// Resolves a Station or ServiceStation invocation already parsed by
        /// <see cref="StationSyntaxHelper.TryParseRouteHandlerInvocation"/>.
        /// Thin wrap of <see cref="HandlerSchemaResolver.ResolveParsedInvocation"/>.
        /// </summary>
        internal static HandlerSchemaResult ResolveParsedInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            HandlerStationKind stationKind,
            MemberAccessExpressionSyntax memberAccess)
        {
            return HandlerSchemaResolver.ResolveParsedInvocation(
                invocation,
                semanticModel,
                stationKind,
                memberAccess);
        }

        /// <summary>
        /// Builds the full handler schema from an already-resolved handler symbol.
        /// Thin wrap of <see cref="HandlerSchemaResolver.ResolveHandler"/>.
        /// </summary>
        internal static HandlerSchemaResult ResolveHandler(
            ResolvedHandler resolved,
            SemanticModel semanticModel,
            HandlerStationKind stationKind)
        {
            return HandlerSchemaResolver.ResolveHandler(
                resolved,
                semanticModel,
                stationKind);
        }
    }
}
