using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Preference / identity helpers for origin parts (Assembler merge, adapter select).
    /// </summary>
    /// <remarks>
    /// Scores live on part shape — callers must not switch on <see cref="RouteChainAnchorKind"/>
    /// for preference or origin-keying.
    /// </remarks>
    internal static class RoutePartPreference
    {
        /// <summary>
        /// Preference score for a materialized origin part (higher wins on merge / select).
        /// </summary>
        public static int Score(IRoutePart part)
        {
            switch (part)
            {
                case LocalBinding _:
                    return 5;
                case FactoryCall _:
                    return 3;
                case CreationSeed _:
                    return 2;
                case JoinSeed _:
                    return 1;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Preference score for a legacy anchor by root/port shape (no kind-switch hot path).
        /// </summary>
        public static int ScoreLegacyAnchor(RouteChainAnchor anchor)
        {
            if (anchor?.Root == null)
            {
                return 0;
            }

            // Local binding window (identifier root) outranks raw factory invocation.
            if (anchor.Root is IdentifierNameSyntax)
            {
                return 4;
            }

            if (FactoryCall.TryFromLegacyAnchor(anchor, out _))
            {
                return 3;
            }

            if (anchor.Root is ObjectCreationExpressionSyntax)
            {
                return 2;
            }

            // Synthetic BranchJoin: station/fork invocation root without a factory method stamp.
            if (anchor.Root is InvocationExpressionSyntax && anchor.FactoryMethod == null)
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Whether the chain key should stamp on <see cref="RouteChainAnchor.Location"/>
        /// (origin call-site) rather than <c>Root.SpanStart</c>.
        /// </summary>
        public static bool IsOriginKeyed(RouteChainAnchor anchor)
        {
            if (anchor?.Root == null)
            {
                return false;
            }

            if (anchor.Root is ObjectCreationExpressionSyntax)
            {
                return false;
            }

            if (anchor.Root is IdentifierNameSyntax)
            {
                return true;
            }

            if (FactoryCall.TryFromLegacyAnchor(anchor, out _))
            {
                return true;
            }

            return false;
        }
    }
}
