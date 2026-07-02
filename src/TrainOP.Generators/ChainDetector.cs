using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    internal static class ChainDetector
    {
        public static ImmutableArray<RouteChain> DetectChains(SyntaxTree syntaxTree, SemanticModel semanticModel)
        {
            var chains = new List<RouteChain>();

            foreach (var node in syntaxTree.GetRoot().DescendantNodes())
            {
                if (!(node is ObjectCreationExpressionSyntax objectCreation))
                {
                    continue;
                }

                if (!StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel))
                {
                    continue;
                }

                var stations = ImmutableArray.CreateBuilder<StationChainLink>();
                var current = (ExpressionSyntax)objectCreation;

                while (TryGetNextStationInvocation(current, out var stationInvocation))
                {
                    if (StationSyntaxHelper.TryGetDataStationInvocation(
                        stationInvocation,
                        semanticModel,
                        out var stationName,
                        out var handlerLocation,
                        out var handlerBinding))
                    {
                        stations.Add(new StationChainLink(
                            stationName,
                            stationInvocation.ArgumentList.Arguments[0].GetLocation(),
                            handlerLocation,
                            handlerBinding));
                    }

                    current = stationInvocation;
                }

                if (stations.Count > 0)
                {
                    chains.Add(new RouteChain(objectCreation.GetLocation(), stations.ToImmutable()));
                }
            }

            return ImmutableArray.CreateRange(chains);
        }

        public static ImmutableArray<InvocationExpressionSyntax> CollectChainedStationInvocations(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel)
        {
            var chained = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

            foreach (var node in syntaxTree.GetRoot().DescendantNodes())
            {
                if (!(node is ObjectCreationExpressionSyntax objectCreation))
                {
                    continue;
                }

                if (!StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel))
                {
                    continue;
                }

                var current = (ExpressionSyntax)objectCreation;
                while (TryGetNextStationInvocation(current, out var stationInvocation))
                {
                    chained.Add(stationInvocation);
                    current = stationInvocation;
                }
            }

            return chained.ToImmutable();
        }

        public static ImmutableArray<InvocationExpressionSyntax> DetectOrphanStationInvocations(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            ISet<InvocationExpressionSyntax> chainedInvocations)
        {
            var orphans = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

            foreach (var node in syntaxTree.GetRoot().DescendantNodes())
            {
                if (!(node is InvocationExpressionSyntax invocation))
                {
                    continue;
                }

                if (!StationSyntaxHelper.IsCandidateStationInvocation(invocation))
                {
                    continue;
                }

                if (chainedInvocations.Contains(invocation))
                {
                    continue;
                }

                if (StationSyntaxHelper.TryGetDataStationInvocation(
                    invocation,
                    semanticModel,
                    out _,
                    out _,
                    out _))
                {
                    orphans.Add(invocation);
                }
            }

            return orphans.ToImmutable();
        }

        private static bool TryGetNextStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            stationInvocation = null;

            if (TryGetDirectStationInvocation(current, out stationInvocation))
            {
                return true;
            }

            if (!(current.Parent is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            if (!IsTransparentRouteMethod(memberAccess.Name.Identifier.ValueText))
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, current))
            {
                return false;
            }

            if (!(memberAccess.Parent is InvocationExpressionSyntax transparentInvocation))
            {
                return false;
            }

            if (!ReferenceEquals(transparentInvocation.Expression, memberAccess))
            {
                return false;
            }

            return TryGetNextStationInvocation(transparentInvocation, out stationInvocation);
        }

        private static bool TryGetDirectStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            stationInvocation = null;

            if (!(current.Parent is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "Station", StringComparison.Ordinal))
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, current))
            {
                return false;
            }

            if (!(memberAccess.Parent is InvocationExpressionSyntax invocation))
            {
                return false;
            }

            if (!ReferenceEquals(invocation.Expression, memberAccess))
            {
                return false;
            }

            if (invocation.ArgumentList.Arguments.Count != 2)
            {
                return false;
            }

            stationInvocation = invocation;
            return true;
        }

        private static bool IsTransparentRouteMethod(string methodName)
        {
            return string.Equals(methodName, "AttachRedSignalStation", StringComparison.Ordinal);
        }
    }
}
