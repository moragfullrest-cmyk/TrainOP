using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Structural validation of a single <see cref="PartEdge"/> before it is accepted by
    /// <see cref="ChainConstructor"/>.
    /// </summary>
    internal static class PartEdgeValidator
    {
        /// <summary>
        /// Validates that <paramref name="edge"/> may be connected.
        /// </summary>
        /// <returns><see langword="true"/> when the edge is accepted.</returns>
        public static bool TryValidate(
            PartEdge edge,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            diagnostics = ImmutableArray<Diagnostic>.Empty;

            return edge.Kind switch
            {
                PartEdgeKind.Bind => IsOriginSeed(edge.From) && edge.To is LocalBinding,
                PartEdgeKind.Append => IsAppendUpstream(edge.From) && edge.To is StationLink,
                // J2 fills port rules; accept structurally for now.
                PartEdgeKind.Join => edge.From is JoinArm && edge.To is JoinSeed,
                // E1 fills port rules; accept structurally for now.
                PartEdgeKind.Extend => edge.From is FactoryCall && edge.To is ExtensionTail,
                _ => false
            };
        }

        private static bool IsOriginSeed(IRoutePart part) =>
            part is CreationSeed or FactoryCall;

        private static bool IsAppendUpstream(IRoutePart part) =>
            part is CreationSeed or FactoryCall or LocalBinding or StationLink or JoinSeed;
    }
}
