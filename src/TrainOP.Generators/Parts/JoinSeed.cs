using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Synthetic root after merging fork arms (legacy BranchJoin).
    /// </summary>
    internal sealed class JoinSeed : IRoutePart
    {
        /// <summary>
        /// Creates a join seed from connected arms and join validation.
        /// </summary>
        public JoinSeed(
            ExpressionSyntax forkExpression,
            InvocationExpressionSyntax downstreamStation,
            ImmutableArray<JoinArm> arms,
            BranchRouteJoinValidation validation)
        {
            ForkExpression = forkExpression;
            DownstreamStation = downstreamStation;
            Arms = arms.IsDefault ? ImmutableArray<JoinArm>.Empty : arms;
            Validation = validation;
            Location = forkExpression?.GetLocation()
                ?? downstreamStation?.GetLocation()
                ?? Location.None;
            MergedTerminalWagons = validation != null && !validation.MergedTerminalWagons.IsDefault
                ? validation.MergedTerminalWagons
                : ImmutableArray<WagonBinding>.Empty;
        }

        /// <summary>
        /// Forking receiver (<c>?:</c> / <c>??</c> / <c>switch</c>).
        /// </summary>
        public ExpressionSyntax ForkExpression { get; }

        /// <summary>
        /// Downstream station that consumes the join; may be <c>null</c> for bare forks.
        /// </summary>
        public InvocationExpressionSyntax DownstreamStation { get; }

        /// <summary>
        /// Connected join arms.
        /// </summary>
        public ImmutableArray<JoinArm> Arms { get; }

        /// <summary>
        /// Result of <see cref="BranchRouteJoinValidator"/> validation.
        /// </summary>
        public BranchRouteJoinValidation Validation { get; }

        /// <summary>
        /// Merged terminal wagons when <see cref="Validation"/> allows merge.
        /// </summary>
        public ImmutableArray<WagonBinding> MergedTerminalWagons { get; }

        /// <inheritdoc />
        public Location Location { get; }

        /// <summary>
        /// Whether arms may merge into a shared downstream chain.
        /// </summary>
        public bool CanMerge => Validation != null && Validation.CanMerge;

        /// <summary>
        /// Projects arms into a legacy <see cref="BranchRouteJoinSet"/>.
        /// </summary>
        public BranchRouteJoinSet ToJoinSet()
        {
            var builder = ImmutableArray.CreateBuilder<BranchRouteGraph>(Arms.Length);
            foreach (var arm in Arms)
            {
                builder.Add(arm.ToBranchRouteGraph());
            }

            return new BranchRouteJoinSet(ForkExpression, DownstreamStation, builder.ToImmutable());
        }
    }
}
