using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Chain
{
    /// <summary>
    /// Outcome of simulating wagon flow through a route chain.
    /// </summary>
    internal sealed class ChainSimulationResult
    {
        /// <summary>
        /// Creates a simulation result with terminal wagons, unknown-return flag, and diagnostics.
        /// </summary>
        public ChainSimulationResult(
            ImmutableArray<WagonBinding> terminalWagons,
            bool hasUnknownReturn,
            ImmutableArray<Diagnostic> diagnostics)
        {
            TerminalWagons = terminalWagons;
            HasUnknownReturn = hasUnknownReturn;
            Diagnostics = diagnostics;
        }

        /// <summary>
        /// Live wagons at the end of the chain, in live order.
        /// Empty when <see cref="HasUnknownReturn"/> is set (terminal state is not trustworthy for merge).
        /// </summary>
        public ImmutableArray<WagonBinding> TerminalWagons { get; }

        public bool HasUnknownReturn { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
