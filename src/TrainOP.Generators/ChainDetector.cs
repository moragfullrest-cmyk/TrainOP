using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Discovers TrainRoute station chains and related invocations from syntax trees.
    /// </summary>
    internal static class ChainDetector
    {
        /// <summary>
        /// Finds all route chain anchors in the given syntax tree.
        /// </summary>
        public static ImmutableArray<RouteChainAnchor> DetectChainAnchors(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel)
        {
            var anchors = ImmutableArray.CreateBuilder<RouteChainAnchor>();

            foreach (var node in syntaxTree.GetRoot().DescendantNodes())
            {
                if (TryDetectAnchor(node, semanticModel, out var anchor))
                {
                    anchors.Add(anchor);
                }
            }

            return anchors.ToImmutable();
        }

        /// <summary>
        /// Finds all route chains starting from detected anchors in the given syntax tree.
        /// </summary>
        public static ImmutableArray<RouteChain> DetectChains(SyntaxTree syntaxTree, SemanticModel semanticModel)
        {
            var chains = new List<RouteChain>();

            foreach (var anchor in DetectChainAnchors(syntaxTree, semanticModel))
            {
                var stations = ImmutableArray.CreateBuilder<StationChainLink>();
                var current = anchor.Root;

                while (TryAdvanceChain(current, semanticModel, stations, out current, null)) ;

                if (stations.Count > 0)
                {
                    chains.Add(new RouteChain(anchor, stations.ToImmutable()));
                }
            }

            return [.. chains];
        }

        /// <summary>
        /// Collects every station or service-station invocation that belongs to a TrainRoute chain.
        /// </summary>
        public static ImmutableArray<InvocationExpressionSyntax> CollectChainedStationInvocations(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel)
        {
            var chained = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

            foreach (var anchor in DetectChainAnchors(syntaxTree, semanticModel))
            {
                var current = anchor.Root;

                while (TryAdvanceChain(current, semanticModel, null, out current, chained)) ;
            }

            return chained.ToImmutable();
        }

        /// <summary>
        /// Finds data-oriented station handlers that are not part of any TrainRoute chain.
        /// </summary>
        public static ImmutableArray<InvocationExpressionSyntax> DetectOrphanStationInvocations(
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            ISet<InvocationExpressionSyntax> chainedInvocations)
        {
            var orphans = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

            foreach (var node in syntaxTree.GetRoot().DescendantNodes())
            {
                if (node is not InvocationExpressionSyntax invocation)
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

        /// <summary>
        /// Attempts to detect a route chain anchor at the given syntax node.
        /// </summary>
        private static bool TryDetectAnchor(
            SyntaxNode node,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (node is ObjectCreationExpressionSyntax objectCreation
                && TryDetectObjectCreationAnchor(objectCreation, semanticModel, out anchor))
            {
                return true;
            }

            if (node is IdentifierNameSyntax identifier
                && TryDetectLocalVariableAnchor(identifier, semanticModel, out anchor))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Detects an anchor at <c>new TrainRoute()</c>.
        /// </summary>
        private static bool TryDetectObjectCreationAnchor(
            ObjectCreationExpressionSyntax objectCreation,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (!StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel))
            {
                return false;
            }

            anchor = new RouteChainAnchor(
                RouteChainAnchorKind.ObjectCreation,
                objectCreation,
                objectCreation.GetLocation(),
                GetContainingMethod(objectCreation, semanticModel));
            return true;
        }

        /// <summary>
        /// Detects an anchor at a local variable use preceded by a <c>new TrainRoute()</c> assignment.
        /// </summary>
        private static bool TryDetectLocalVariableAnchor(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (!IsLocalVariableChainReceiver(identifier))
            {
                return false;
            }

            if (!TryGetPrecedingTrainRouteCreationAssignment(identifier, semanticModel, out _))
            {
                return false;
            }

            anchor = new RouteChainAnchor(
                RouteChainAnchorKind.LocalVariable,
                identifier,
                identifier.GetLocation(),
                GetContainingMethod(identifier, semanticModel));
            return true;
        }

        /// <summary>
        /// Determines whether the identifier is the receiver of a route handler member access.
        /// </summary>
        private static bool IsLocalVariableChainReceiver(IdentifierNameSyntax identifier)
        {
            if (identifier.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, identifier))
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            return string.Equals(methodName, "Station", StringComparison.Ordinal)
                || string.Equals(methodName, "StationAsync", StringComparison.Ordinal)
                || string.Equals(methodName, "ServiceStation", StringComparison.Ordinal);
        }

        /// <summary>
        /// Finds the latest direct <c>new TrainRoute()</c> assignment to the local before its use site.
        /// </summary>
        private static bool TryGetPrecedingTrainRouteCreationAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ObjectCreationExpressionSyntax creation)
        {
            creation = null;

            if (semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol localSymbol)
            {
                return false;
            }

            if (!StationSyntaxHelper.IsTrainRoute(localSymbol.Type))
            {
                return false;
            }

            var methodDeclaration = identifier.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                return false;
            }

            var containingMethod = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
            if (containingMethod == null
                || !SymbolEqualityComparer.Default.Equals(localSymbol.ContainingSymbol, containingMethod))
            {
                return false;
            }

            var usePosition = identifier.SpanStart;
            ObjectCreationExpressionSyntax bestCreation = null;
            var bestAssignmentPosition = -1;

            foreach (var node in methodDeclaration.DescendantNodes())
            {
                var assignmentPosition = -1;
                ObjectCreationExpressionSyntax objectCreation = null;

                if (node is VariableDeclaratorSyntax declarator
                    && declarator.Initializer?.Value is ObjectCreationExpressionSyntax declaratorCreation)
                {
                    if (semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol declaredLocal
                        && SymbolEqualityComparer.Default.Equals(declaredLocal, localSymbol))
                    {
                        assignmentPosition = declarator.SpanStart;
                        objectCreation = declaratorCreation;
                    }
                }
                else if (node is AssignmentExpressionSyntax assignment
                    && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && assignment.Right is ObjectCreationExpressionSyntax assignmentCreation
                    && semanticModel.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol assignedLocal
                    && SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol))
                {
                    assignmentPosition = assignment.SpanStart;
                    objectCreation = assignmentCreation;
                }

                if (objectCreation == null
                    || !StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel)
                    || assignmentPosition >= usePosition
                    || assignmentPosition <= bestAssignmentPosition)
                {
                    continue;
                }

                bestAssignmentPosition = assignmentPosition;
                bestCreation = objectCreation;
            }

            if (bestCreation == null)
            {
                return false;
            }

            creation = bestCreation;
            return true;
        }

        /// <summary>
        /// Resolves the method that contains the given syntax node.
        /// </summary>
        private static IMethodSymbol GetContainingMethod(SyntaxNode node, SemanticModel semanticModel)
        {
            var methodDeclaration = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                return null;
            }

            return semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
        }

        /// <summary>
        /// Advances along a route chain by resolving the next station or service-station invocation.
        /// </summary>
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
                    serviceHandlerLocation,
                    serviceHandlerLocation,
                    serviceHandlerBinding,
                    serviceInvocation));
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
                    handlerBinding,
                    stationInvocation));
            }

            chainedInvocations?.Add(stationInvocation);
            next = stationInvocation;
            return true;
        }

        /// <summary>
        /// Detects a direct ServiceStation invocation immediately following the current expression.
        /// </summary>
        private static bool TryGetDirectServiceStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax serviceStationInvocation)
        {
            serviceStationInvocation = null;

            if (current.Parent is not MemberAccessExpressionSyntax memberAccess)
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

            if (memberAccess.Parent is not InvocationExpressionSyntax invocation)
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

        /// <summary>
        /// Resolves the next Station invocation, skipping through transparent route methods.
        /// </summary>
        private static bool TryGetNextStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            return TryGetNextStationInvocationCore(current, out stationInvocation);
        }

        private static bool TryGetNextStationInvocationCore(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            stationInvocation = null;

            if (TryGetDirectStationInvocation(current, out stationInvocation))
            {
                return true;
            }

            if (current.Parent is not MemberAccessExpressionSyntax memberAccess)
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

            if (memberAccess.Parent is not InvocationExpressionSyntax transparentInvocation)
            {
                return false;
            }

            if (!ReferenceEquals(transparentInvocation.Expression, memberAccess))
            {
                return false;
            }

            return TryGetNextStationInvocationCore(transparentInvocation, out stationInvocation);
        }

        /// <summary>
        /// Detects a direct Station invocation immediately following the current expression.
        /// </summary>
        private static bool TryGetDirectStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax stationInvocation)
        {
            stationInvocation = null;

            if (current.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "Station", StringComparison.Ordinal)
                && !string.Equals(memberAccess.Name.Identifier.ValueText, "StationAsync", StringComparison.Ordinal))
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, current))
            {
                return false;
            }

            if (memberAccess.Parent is not InvocationExpressionSyntax invocation)
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

        /// <summary>
        /// Determines whether a route method name is transparent for chain traversal.
        /// </summary>
        private static bool IsTransparentRouteMethod(string methodName)
        {
            return string.Equals(methodName, "ServiceStation", StringComparison.Ordinal);
        }
    }
}
