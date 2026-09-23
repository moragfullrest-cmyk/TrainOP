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
        public static int Score(IRoutePart part) =>
            part switch
            {
                LocalBinding => 5,
                FactoryCall => 3,
                CreationSeed => 2,
                JoinSeed => 1,
                _ => 0
            };

        /// <summary>
        /// Whether the chain key should stamp on <see cref="IRoutePart.Location"/>
        /// (origin call-site) rather than root <c>SpanStart</c>.
        /// </summary>
        public static bool IsOriginKeyed(IRoutePart part)
        {
            if (!RouteOriginPorts.TryGetRoot(part, out var root))
            {
                return false;
            }

            return root switch
            {
                ObjectCreationExpressionSyntax => false,
                IdentifierNameSyntax => true,
                _ => part is FactoryCall or LocalBinding { Origin: FactoryCall }
            };
        }
    }
}
