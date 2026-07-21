using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace TrainOP.Generators.Route
{
    /// <summary>
    /// A set of branch route graphs that converge at one downstream Station call site.
    /// </summary>
    internal sealed class BranchRouteJoinSet
    {
        /// <summary>
        /// Creates a join set for a forking receiver and its discovered leaf branches.
        /// </summary>
        public BranchRouteJoinSet(
            ExpressionSyntax joinReceiver,
            InvocationExpressionSyntax downstreamStation,
            ImmutableArray<BranchRouteGraph> branches)
        {
            JoinReceiver = joinReceiver;
            DownstreamStation = downstreamStation;
            Branches = branches;
        }

        /// <summary>
        /// The forking expression that is the direct (after peel) receiver of a shared downstream
        /// <c>.Station</c>.
        /// </summary>
        public ExpressionSyntax JoinReceiver { get; }

        /// <summary>
        /// The <c>.Station</c> / <c>.ServiceStation</c> that uses the fork as
        /// receiver; may be <c>null</c> when the join set is built from a bare fork without walking
        /// the parent.
        /// </summary>
        public InvocationExpressionSyntax DownstreamStation { get; }

        /// <summary>
        /// Leaf branch graphs from <see cref="BranchRouteGraphDiscoverer.Discover"/>.
        /// </summary>
        public ImmutableArray<BranchRouteGraph> Branches { get; }
    }
}
