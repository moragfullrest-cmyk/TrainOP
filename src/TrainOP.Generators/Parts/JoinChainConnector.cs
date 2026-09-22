using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using TrainOP.Generators.Route;

namespace TrainOP.Generators.Parts
{
    /// <summary>
    /// Connects <see cref="JoinArm"/> parts into a <see cref="JoinSeed"/> and validates
    /// the merge via <see cref="BranchRouteJoinValidator"/>.
    /// </summary>
    internal static class JoinChainConnector
    {
        /// <summary>
        /// Materializes arms under <paramref name="forkExpression"/>, validates the join, and
        /// records Join edges on a <see cref="ChainConstructor"/>.
        /// </summary>
        public static bool TryConnect(
            ExpressionSyntax forkExpression,
            InvocationExpressionSyntax downstreamStation,
            SemanticModel semanticModel,
            out ChainConstructor constructor,
            out JoinSeed joinSeed)
        {
            constructor = null;
            joinSeed = null;

            if (!JoinArmMaterializer.TryMaterializeArms(forkExpression, semanticModel, out var arms))
            {
                return false;
            }

            return TryConnect(arms, forkExpression, downstreamStation, semanticModel, out constructor, out joinSeed);
        }

        /// <summary>
        /// Connects already-materialized arms from a legacy <see cref="BranchRouteJoinSet"/>.
        /// </summary>
        public static bool TryConnect(
            BranchRouteJoinSet joinSet,
            SemanticModel semanticModel,
            out ChainConstructor constructor,
            out JoinSeed joinSeed)
        {
            constructor = null;
            joinSeed = null;
            if (joinSet == null || joinSet.Branches.IsDefaultOrEmpty)
            {
                return false;
            }

            var armsBuilder = ImmutableArray.CreateBuilder<JoinArm>(joinSet.Branches.Length);
            foreach (var branch in joinSet.Branches)
            {
                var arm = JoinArm.FromBranchRouteGraph(branch);
                if (arm != null)
                {
                    armsBuilder.Add(arm);
                }
            }

            return TryConnect(
                armsBuilder.ToImmutable(),
                joinSet.JoinReceiver,
                joinSet.DownstreamStation,
                semanticModel,
                out constructor,
                out joinSeed);
        }

        private static bool TryConnect(
            ImmutableArray<JoinArm> arms,
            ExpressionSyntax forkExpression,
            InvocationExpressionSyntax downstreamStation,
            SemanticModel semanticModel,
            out ChainConstructor constructor,
            out JoinSeed joinSeed)
        {
            constructor = null;
            joinSeed = null;
            if (arms.IsDefaultOrEmpty)
            {
                return false;
            }

            var branches = ImmutableArray.CreateBuilder<BranchRouteGraph>(arms.Length);
            foreach (var arm in arms)
            {
                branches.Add(arm.ToBranchRouteGraph());
            }

            var joinSet = new BranchRouteJoinSet(
                forkExpression,
                downstreamStation,
                branches.ToImmutable());
            var validation = BranchRouteJoinValidator.Validate(joinSet, semanticModel);

            joinSeed = new JoinSeed(forkExpression, downstreamStation, arms, validation);
            constructor = new ChainConstructor();
            if (!constructor.TryAdd(joinSeed))
            {
                return false;
            }

            foreach (var arm in arms)
            {
                if (!constructor.TryJoin(arm, joinSeed, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
