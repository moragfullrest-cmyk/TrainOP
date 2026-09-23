using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Preference / identity helpers for origin parts (Assembler merge, chain select).
    /// </summary>
    /// <remarks>
    /// Scores live on part shape — callers must not invent a kind enum for preference.
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
        /// Whether the chain key should stamp on <see cref="IRoutePart.Location"/>
        /// (origin call-site) rather than root <c>SpanStart</c>.
        /// </summary>
        public static bool IsOriginKeyed(IRoutePart part)
        {
            if (part == null || !RouteOriginPorts.TryGetRoot(part, out var root))
            {
                return false;
            }

            if (root is ObjectCreationExpressionSyntax)
            {
                return false;
            }

            if (root is IdentifierNameSyntax)
            {
                return true;
            }

            if (part is FactoryCall || (part as LocalBinding)?.Origin is FactoryCall)
            {
                return true;
            }

            return false;
        }
    }
}
