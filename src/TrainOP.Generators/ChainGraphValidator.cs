using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Validates a route chain by running wagon-flow simulation and collecting diagnostics.
    /// </summary>
    internal static class ChainGraphValidator
    {
        /// <summary>
        /// Validates wagon availability, types, and return semantics across all stations in a chain.
        /// </summary>
        public static ImmutableArray<Diagnostic> Validate(RouteChain chain)
        {
            var initial = chain.Anchor.InitialWagons;
            return initial.IsDefaultOrEmpty
                ? ChainGraphSimulator.Simulate(chain).Diagnostics
                : ChainGraphSimulator.Simulate(chain, initial).Diagnostics;
        }
    }
}
