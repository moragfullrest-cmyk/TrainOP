using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TrainOP.Generators.Models
{
    /// <summary>
    /// A single leaf branch under a forking TrainRoute receiver (<c>?:</c> / <c>??</c> / <c>switch</c>).
    /// </summary>
    internal sealed class BranchRouteGraph
    {
        /// <summary>
        /// Creates a branch route graph for a leaf expression, optionally with a resolved chain and simulation.
        /// </summary>
        public BranchRouteGraph(
            ExpressionSyntax branchExpression,
            bool isResolved,
            RouteChain chain,
            ChainSimulationResult simulation)
        {
            BranchExpression = branchExpression;
            IsResolved = isResolved;
            Chain = chain;
            Simulation = simulation;
        }

        /// <summary>
        /// Leaf expression of this branch (after transparent unwrap at discovery time).
        /// </summary>
        public ExpressionSyntax BranchExpression { get; }

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
    }
}
