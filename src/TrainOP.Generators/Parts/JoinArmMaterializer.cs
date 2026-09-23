using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Materializes <see cref="JoinArm"/> parts from a forking TrainRoute receiver.
    /// </summary>
    internal static class JoinArmMaterializer
    {
        /// <summary>
        /// Attempts to materialize all leaf arms under <paramref name="forkExpression"/>.
        /// </summary>
        public static bool TryMaterializeArms(
            ExpressionSyntax forkExpression,
            SemanticModel semanticModel,
            out ImmutableArray<JoinArm> arms)
        {
            arms = ImmutableArray<JoinArm>.Empty;
            if (forkExpression == null || semanticModel == null)
            {
                return false;
            }

            forkExpression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(forkExpression);
            if (!JoinChainsStage.IsForkingExpression(forkExpression))
            {
                return false;
            }

            var graphs = BranchRouteGraphDiscoverer.Discover(forkExpression, semanticModel);
            if (graphs.IsDefaultOrEmpty)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<JoinArm>(graphs.Length);
            foreach (var graph in graphs)
            {
                var arm = JoinArm.FromBranchRouteGraph(graph);
                if (arm != null)
                {
                    builder.Add(arm);
                }
            }

            arms = builder.ToImmutable();
            return arms.Length > 0;
        }

        /// <summary>
        /// Attempts to materialize a single leaf expression as a <see cref="JoinArm"/>.
        /// </summary>
        public static bool TryMaterializeLeaf(
            ExpressionSyntax leafExpression,
            SemanticModel semanticModel,
            out JoinArm arm)
        {
            arm = null;
            if (leafExpression == null || semanticModel == null)
            {
                return false;
            }

            // Reuse discoverer leaf resolution by wrapping a degenerate non-fork path:
            // Discover on a non-fork returns a single TryResolveLeaf graph.
            leafExpression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(leafExpression);
            if (JoinChainsStage.IsForkingExpression(leafExpression))
            {
                return false;
            }

            var graphs = BranchRouteGraphDiscoverer.Discover(leafExpression, semanticModel);
            if (graphs.IsDefaultOrEmpty)
            {
                return false;
            }

            arm = JoinArm.FromBranchRouteGraph(graphs[0]);
            return arm != null;
        }
    }
}
