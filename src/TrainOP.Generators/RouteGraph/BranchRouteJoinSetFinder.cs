using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;
namespace TrainOP.Generators
{
    /// <summary>
    /// Finds join sets in a syntax tree: forking receivers that are used as
    /// <c>MemberAccess.Expression</c> of Station / ServiceStation, including locals
    /// assigned from a forking RHS (<c>?:</c> / <c>??</c> / <c>switch</c>).
    /// </summary>
    internal static class BranchRouteJoinSetFinder
    {
        /// <summary>
        /// Finds all join sets in <paramref name="tree"/> where a forking receiver feeds a shared
        /// downstream Station call.
        /// </summary>
        public static ImmutableArray<BranchRouteJoinSet> Find(SyntaxTree tree, SemanticModel model)
        {
            if (tree == null || model == null)
            {
                return ImmutableArray<BranchRouteJoinSet>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<BranchRouteJoinSet>();

            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (!IsCandidateDownstreamStation(node))
                {
                    continue;
                }

                var invocation = (InvocationExpressionSyntax)node;
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                {
                    continue;
                }

                var peeled = ReceiverExpressionSyntaxPeel.UnwrapTransparent(memberAccess.Expression);
                if (JoinChainsStage.IsForkingExpression(peeled))
                {
                    var branches = BranchRouteGraphDiscoverer.Discover(memberAccess.Expression, model);
                    builder.Add(new BranchRouteJoinSet(
                        joinReceiver: memberAccess.Expression,
                        downstreamStation: invocation,
                        branches: branches));
                    continue;
                }

                // C-10/C-11: local assigned from forking RHS, then statement/fluent Station.
                if (peeled is IdentifierNameSyntax localIdentifier
                    && RouteOriginWindow.TryGetPrecedingForkingAssignment(
                        localIdentifier,
                        model,
                        out var forkExpression))
                {
                    var branches = BranchRouteGraphDiscoverer.Discover(forkExpression, model);
                    builder.Add(new BranchRouteJoinSet(
                        joinReceiver: forkExpression,
                        downstreamStation: invocation,
                        branches: branches));
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        /// Determines whether a node is a Station / ServiceStation candidate.
        /// </summary>
        private static bool IsCandidateDownstreamStation(SyntaxNode node)
        {
            return StationSyntaxHelper.IsCandidateRouteHandlerInvocation(node);
        }
    }
}
