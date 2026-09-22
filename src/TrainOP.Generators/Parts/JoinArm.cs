using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// One arm of a fork (<c>?:</c> / <c>??</c> / <c>switch</c>) before join.
    /// </summary>
    internal sealed class JoinArm : IRoutePart
    {
        /// <summary>
        /// Creates a join-arm part from a resolved (or unresolved) branch leaf.
        /// </summary>
        public JoinArm(
            ExpressionSyntax branchExpression,
            bool isResolved,
            RouteChain chain = null,
            ChainSimulationResult simulation = null)
        {
            BranchExpression = branchExpression;
            Location = branchExpression?.GetLocation();
            IsResolved = isResolved;
            Chain = chain;
            Simulation = simulation;
        }

        /// <summary>
        /// Leaf expression of this arm (after transparent unwrap at discovery time).
        /// </summary>
        public ExpressionSyntax BranchExpression { get; }

        /// <inheritdoc />
        public Location Location { get; }

        /// <summary>
        /// Whether a chain root was resolved for this leaf.
        /// </summary>
        public bool IsResolved { get; }

        /// <summary>
        /// Resolved route chain ending at the leaf, or <c>null</c> when unresolved.
        /// </summary>
        public RouteChain Chain { get; }

        /// <summary>
        /// Simulation of <see cref="Chain"/>, or <c>null</c> when unresolved.
        /// </summary>
        public ChainSimulationResult Simulation { get; }

        /// <summary>
        /// Projects this part into legacy <see cref="BranchRouteGraph"/>.
        /// </summary>
        public BranchRouteGraph ToBranchRouteGraph()
        {
            return new BranchRouteGraph(BranchExpression, IsResolved, Chain, Simulation);
        }

        /// <summary>
        /// Rebuilds a <see cref="JoinArm"/> from a legacy branch graph.
        /// </summary>
        public static JoinArm FromBranchRouteGraph(BranchRouteGraph graph)
        {
            if (graph == null)
            {
                return null;
            }

            return new JoinArm(
                graph.BranchExpression,
                graph.IsResolved,
                graph.Chain,
                graph.Simulation);
        }
    }
}
