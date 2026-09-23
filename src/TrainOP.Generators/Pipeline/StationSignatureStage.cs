using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Resolves station / service-station call sites into
    /// <see cref="StationHandlerBinding"/> without changing resolver semantics.
    /// </summary>
    internal static class StationSignatureStage
    {
        /// <summary>
        /// Resolves a Station or ServiceStation invocation already parsed by
        /// <see cref="StationSyntaxHelper.TryParseRouteHandlerInvocation"/>.
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
    }
}
