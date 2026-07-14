using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// Result of validating whether forking route branches can be merged at a join station.
    /// </summary>
    internal sealed class BranchRouteJoinValidation
    {
        /// <summary>
        /// Creates a join-validation result with mergeability, diagnostics, and optional merged terminals.
        /// </summary>
        public BranchRouteJoinValidation(
            bool canMerge,
            ImmutableArray<Diagnostic> diagnostics,
            ImmutableArray<WagonBinding> mergedTerminalWagons)
        {
            CanMerge = canMerge;
            Diagnostics = diagnostics;
            MergedTerminalWagons = mergedTerminalWagons;
        }

        /// <summary>
        /// Whether branch terminal wagon schemas are compatible and may be merged.
        /// </summary>
        public bool CanMerge { get; }

        /// <summary>
        /// Diagnostics explaining why a join failed; empty when merge succeeds.
        /// </summary>
        public ImmutableArray<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// Intersection of compatible terminal wagons across branches; empty when <see cref="CanMerge"/> is false.
        /// </summary>
        public ImmutableArray<WagonBinding> MergedTerminalWagons { get; }
    }
}
