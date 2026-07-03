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

                while (TryAdvanceChain(current, semanticModel, stations, out current, null))
                {
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

                while (TryAdvanceChain(current, semanticModel, null, out current, chained))
                {
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

                if (!StationSyntaxHelper.IsCandidateStationInvocation(invocation)
                    && !StationSyntaxHelper.IsCandidateServiceStationInvocation(invocation))
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
                        out _)
                    || StationSyntaxHelper.TryGetDataServiceStationInvocation(
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

        private static bool TryAdvanceChain(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            ImmutableArray<StationChainLink>.Builder stations,
            out ExpressionSyntax next,
            ImmutableArray<InvocationExpressionSyntax>.Builder chainedInvocations)
        {
            next = current;

            if (TryGetDirectServiceStationInvocation(current, out var serviceInvocation)
                && StationSyntaxHelper.TryGetDataServiceStationInvocation(
                    serviceInvocation,
                    semanticModel,
                    out var serviceStationName,
                    out var serviceHandlerLocation,
                    out var serviceHandlerBinding))
            {
                stations?.Add(new StationChainLink(
                    serviceStationName,
                    serviceInvocation.ArgumentList.Arguments[0].GetLocation(),
                    serviceHandlerLocation,
                    serviceHandlerBinding));
                chainedInvocations?.Add(serviceInvocation);
                next = serviceInvocation;
                return true;
            }

            if (!TryGetNextStationInvocation(current, out var stationInvocation))
            {
                return false;
            }

            if (StationSyntaxHelper.TryGetDataStationInvocation(
                stationInvocation,
                semanticModel,
                out var stationName,
                out var handlerLocation,
                out var handlerBinding))
            {
                stations?.Add(new StationChainLink(
                    stationName,
                    stationInvocation.ArgumentList.Arguments[0].GetLocation(),
                    handlerLocation,
                    handlerBinding));
            }

            chainedInvocations?.Add(stationInvocation);
            next = stationInvocation;
            return true;
        }

        private static bool TryGetDirectServiceStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax serviceStationInvocation)
        {
            serviceStationInvocation = null;

            if (!(current.Parent is MemberAccessExpressionSyntax memberAccess))
            {
                return false;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "ServiceStation", StringComparison.Ordinal))
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

            serviceStationInvocation = invocation;
            return true;
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
            return string.Equals(methodName, "ServiceStation", StringComparison.Ordinal);
        }
    }
}
