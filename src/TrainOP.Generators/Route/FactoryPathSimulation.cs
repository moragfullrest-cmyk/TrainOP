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
            Location location,
            bool isAsync = false)
        {
            TerminalWagons = terminalWagons;
            HasUnknownReturn = hasUnknownReturn;
            Location = location;
            IsAsync = isAsync;
        }

        public ImmutableArray<WagonBinding> TerminalWagons { get; }

        public bool HasUnknownReturn { get; }

        public Location Location { get; }

        /// <summary>
        /// True when this path contains an async station, including an async factory it continues.
        /// </summary>
        public bool IsAsync { get; }
    }
}
