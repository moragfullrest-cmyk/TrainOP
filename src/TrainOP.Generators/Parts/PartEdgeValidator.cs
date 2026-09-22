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

            if (edge.From == null || edge.To == null)
            {
                return false;
            }

            switch (edge.Kind)
            {
                case PartEdgeKind.Bind:
                    return IsOriginSeed(edge.From) && edge.To is LocalBinding;

                case PartEdgeKind.Append:
                    return IsAppendUpstream(edge.From) && edge.To is StationLink;

                case PartEdgeKind.Join:
                    // J2 fills port rules; accept structurally for now.
                    return edge.From is JoinArm && edge.To is JoinSeed;

                case PartEdgeKind.Extend:
                    // E1 fills port rules; accept structurally for now.
                    return edge.From is FactoryCall && edge.To is ExtensionTail;

                default:
                    return false;
            }
        }

        private static bool IsOriginSeed(IRoutePart part)
        {
            return part is CreationSeed || part is FactoryCall;
        }

        private static bool IsAppendUpstream(IRoutePart part)
        {
            return part is CreationSeed
                || part is FactoryCall
                || part is LocalBinding
                || part is StationLink
                || part is JoinSeed;
        }
    }
}
