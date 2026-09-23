using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// One fork-join site with validation and optional merged terminals.
    /// </summary>
    internal sealed class JoinedChain
    {
        /// <summary>
        /// Creates a joined-chain IR node from a discovered join set and its validation.
        /// </summary>
        public JoinedChain(
            BranchRouteJoinSet joinSet,
            BranchRouteJoinValidation validation,
            TerminalSet mergedTerminals = null)
        {
            JoinSet = joinSet;
            Validation = validation;
            MergedTerminals = mergedTerminals
                ?? (validation != null && validation.CanMerge
                    ? TerminalSetAdapters.FromJoin(validation.MergedTerminalWagons)
                    : TerminalSet.Unknown(TerminalSet.Origin.Join));
        }

        /// <summary>Discovered fork receiver and leaf branches.</summary>
        public BranchRouteJoinSet JoinSet { get; }

        /// <summary>Merge validation (TOP* diagnostics unchanged).</summary>
        public BranchRouteJoinValidation Validation { get; }

        /// <summary>
        /// Merged terminal wagons when <see cref="BranchRouteJoinValidation.CanMerge"/>;
        /// otherwise an unknown <see cref="TerminalSet.Origin.Join"/> set.
        /// </summary>
        public TerminalSet MergedTerminals { get; }
    }
}
