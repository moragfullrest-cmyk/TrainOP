using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Route
{
    /// <summary>
    /// Outcome of simulating one factory return path.
    /// </summary>
    internal sealed class FactoryPathSimulation
    {
        /// <summary>
        /// Creates a factory path simulation result.
        /// </summary>
        public FactoryPathSimulation(
            ImmutableArray<WagonBinding> terminalWagons,
            bool hasUnknownReturn,
            Location location)
        {
            TerminalWagons = terminalWagons;
            HasUnknownReturn = hasUnknownReturn;
            Location = location;
        }

        public ImmutableArray<WagonBinding> TerminalWagons { get; }

        public bool HasUnknownReturn { get; }

        public Location Location { get; }
    }
}
