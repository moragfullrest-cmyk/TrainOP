using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;
namespace TrainOP.Generators
{
    /// <summary>
    /// Discovers all branch route graphs under a forking TrainRoute receiver expression.
    /// </summary>
    internal static class BranchRouteGraphDiscoverer
    {
        /// <summary>
        /// Recursively discovers leaf branch graphs for <c>?:</c> / <c>??</c> / <c>switch</c> receivers.
        /// </summary>
        public static ImmutableArray<BranchRouteGraph> Discover(
            ExpressionSyntax receiver,
            SemanticModel semanticModel)
        {
            receiver = ReceiverExpressionSyntaxPeel.UnwrapTransparent(receiver);
            if (receiver == null)
            {
                return ImmutableArray<BranchRouteGraph>.Empty;
            }

            if (receiver is ConditionalExpressionSyntax conditional)
            {
                return Discover(conditional.WhenTrue, semanticModel)
                    .AddRange(Discover(conditional.WhenFalse, semanticModel));
            }

            if (receiver is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                return Discover(binary.Left, semanticModel)
                    .AddRange(Discover(binary.Right, semanticModel));
            }

            if (receiver is SwitchExpressionSyntax switchExpression)
            {
                var builder = ImmutableArray.CreateBuilder<BranchRouteGraph>();
                foreach (var arm in switchExpression.Arms)
                {
                    builder.AddRange(Discover(arm.Expression, semanticModel));
                }

                return builder.ToImmutable();
            }

            return ImmutableArray.Create(TryResolveLeaf(receiver, semanticModel));
        }

        /// <summary>
        /// Resolves a non-forking leaf into a <see cref="BranchRouteGraph"/>.
        /// </summary>
        private static BranchRouteGraph TryResolveLeaf(
            ExpressionSyntax leaf,
            SemanticModel semanticModel)
        {
            if (RouteChainWalker.IsBareUserDefinedFactoryInvocation(leaf, semanticModel))
            {
                return new BranchRouteGraph(leaf, isResolved: false, chain: null, simulation: null);
            }

            if (RouteChainWalker.TryBuildChainEndingAt(leaf, semanticModel, out var chain))
            {
                var simulation = ChainGraphSimulator.Simulate(chain);
                return new BranchRouteGraph(leaf, isResolved: true, chain, simulation);
            }

            return new BranchRouteGraph(leaf, isResolved: false, chain: null, simulation: null);
        }
    }
}
