using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    internal static class ChainGraphValidator
    {
        public static ImmutableArray<Diagnostic> Validate(RouteChain chain)
        {
            return ChainGraphSimulator.Simulate(chain).Diagnostics;
        }
    }
}
