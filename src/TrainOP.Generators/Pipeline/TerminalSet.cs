using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Stage 5 IR: a terminal wagon set tagged with how it was produced.
    /// </summary>
    internal sealed class TerminalSet
    {
        /// <summary>
        /// Provenance of a <see cref="TerminalSet"/> (variant folding for stage 5).
        /// </summary>
        internal enum Origin
        {
            /// <summary>Linear <see cref="ChainGraphSimulator"/> walk.</summary>
            Linear = 0,

            /// <summary>Factory return-path simulation.</summary>
            FactoryPath = 1,

            /// <summary>Merged terminals after branch join validation.</summary>
            Join = 2,

            /// <summary>Upstream seed from a chain anchor (<c>InitialWagons</c>).</summary>
            AnchorSeed = 3,
        }

        /// <summary>
        /// Creates a terminal set with wagons, provenance, and unknown-return flag.
        /// </summary>
        public TerminalSet(
            ImmutableArray<WagonBinding> wagons,
            Origin origin,
            bool hasUnknownReturn = false)
        {
            Wagons = wagons.IsDefault ? ImmutableArray<WagonBinding>.Empty : wagons;
            Provenance = origin;
            HasUnknownReturn = hasUnknownReturn;
        }

        /// <summary>Terminal wagons in live / merge order (empty when unknown).</summary>
        public ImmutableArray<WagonBinding> Wagons { get; }

        /// <summary>How this set was produced.</summary>
        public Origin Provenance { get; }

        /// <summary>
        /// When true, <see cref="Wagons"/> must not be trusted for merge / schema export.
        /// </summary>
        public bool HasUnknownReturn { get; }

        /// <summary>Empty known terminal set for the given origin.</summary>
        public static TerminalSet Empty(Origin origin)
        {
            return new TerminalSet(ImmutableArray<WagonBinding>.Empty, origin, hasUnknownReturn: false);
        }

        /// <summary>Unknown terminal set (empty wagons) for the given origin.</summary>
        public static TerminalSet Unknown(Origin origin)
        {
            return new TerminalSet(ImmutableArray<WagonBinding>.Empty, origin, hasUnknownReturn: true);
        }
    }

    /// <summary>
    /// Adapters between legacy wagon arrays / simulation results and <see cref="TerminalSet"/>.
    /// Diagnostics consumers keep using <see cref="WagonBinding"/> arrays; provenance is additive.
    /// </summary>
    internal static class TerminalSetAdapters
    {
        /// <summary>
        /// Wraps a linear (or join-seeded) chain simulation result.
        /// </summary>
        public static TerminalSet FromSimulation(
            ChainSimulationResult result,
            TerminalSet.Origin origin = TerminalSet.Origin.Linear)
        {
            if (result == null)
            {
                return TerminalSet.Unknown(origin);
            }

            if (result.HasUnknownReturn)
            {
                return TerminalSet.Unknown(origin);
            }

            return new TerminalSet(result.TerminalWagons, origin, hasUnknownReturn: false);
        }

        /// <summary>
        /// Wraps one factory return-path simulation.
        /// </summary>
        public static TerminalSet FromFactoryPath(FactoryPathSimulation path)
        {
            if (path == null)
            {
                return TerminalSet.Unknown(TerminalSet.Origin.FactoryPath);
            }

            if (path.HasUnknownReturn)
            {
                return TerminalSet.Unknown(TerminalSet.Origin.FactoryPath);
            }

            return new TerminalSet(path.TerminalWagons, TerminalSet.Origin.FactoryPath, hasUnknownReturn: false);
        }

        /// <summary>
        /// Wraps merged terminal wagons from a successful join validation.
        /// </summary>
        public static TerminalSet FromJoin(ImmutableArray<WagonBinding> mergedTerminalWagons)
        {
            if (mergedTerminalWagons.IsDefault)
            {
                return TerminalSet.Empty(TerminalSet.Origin.Join);
            }

            return new TerminalSet(mergedTerminalWagons, TerminalSet.Origin.Join, hasUnknownReturn: false);
        }

        /// <summary>
        /// Wraps anchor <c>InitialWagons</c> as an upstream seed set.
        /// </summary>
        public static TerminalSet FromAnchorSeed(ImmutableArray<WagonBinding> initialWagons)
        {
            if (initialWagons.IsDefaultOrEmpty)
            {
                return TerminalSet.Empty(TerminalSet.Origin.AnchorSeed);
            }

            return new TerminalSet(initialWagons, TerminalSet.Origin.AnchorSeed, hasUnknownReturn: false);
        }

        /// <summary>
        /// Extracts wagon bindings for legacy consumers (diagnostics / schema paths).
        /// </summary>
        public static ImmutableArray<WagonBinding> ToWagons(TerminalSet terminals)
        {
            if (terminals == null || terminals.HasUnknownReturn)
            {
                return ImmutableArray<WagonBinding>.Empty;
            }

            return terminals.Wagons;
        }
    }
}
