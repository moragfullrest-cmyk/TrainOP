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
        /// Builds a <see cref="RouteChain"/> starting at an already-identified Station invocation
        /// (inclusive) and continuing through further fluent stations.
        /// </summary>
        internal static bool TryBuildChainFromStationInvocation(
            InvocationExpressionSyntax startStation,
            SemanticModel semanticModel,
            out RouteChain chain)
        {
            chain = null;

            if (startStation == null || semanticModel == null)
            {
                return false;
            }

            var stations = ImmutableArray.CreateBuilder<StationChainLink>();

            if (StationSyntaxHelper.IsCandidateServiceStationInvocation(startStation)
                && StationSyntaxHelper.TryGetDataServiceStationInvocation(
                    startStation,
                    semanticModel,
                    out var serviceStationName,
                    out var serviceHandlerLocation,
                    out var serviceHandlerBinding))
            {
                stations.Add(new StationChainLink(
                    serviceStationName,
                    serviceHandlerLocation,
                    serviceHandlerLocation,
                    serviceHandlerBinding,
                    startStation));
            }
            else if (StationSyntaxHelper.TryGetDataStationInvocation(
                startStation,
                semanticModel,
                out var stationName,
                out var handlerLocation,
                out var handlerBinding))
            {
                stations.Add(new StationChainLink(
                    stationName,
                    startStation.ArgumentList.Arguments[0].GetLocation(),
                    handlerLocation,
                    handlerBinding,
                    startStation));
            }
            else
            {
                return false;
            }

            var anchor = new RouteChainAnchor(
                RouteChainAnchorKind.BranchJoin,
                startStation,
                startStation.GetLocation(),
                GetContainingMethod(startStation, semanticModel));

            var current = (ExpressionSyntax)startStation;
            while (TryAdvanceChain(current, semanticModel, stations, out current, null)) ;

            chain = new RouteChain(anchor, stations.ToImmutable());
            return true;
        }

        /// <summary>
        /// Builds a <see cref="RouteChain"/> from a known chain root forward until
        /// <paramref name="endpoint"/> (inclusive), without continuing past a fork into an outer join.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="DetectChains"/>, this accepts bare <c>new TrainRoute()</c> / local endpoints
        /// with zero stations, and does not require a local identifier to already be a Station receiver.
        /// </remarks>
        internal static bool TryBuildChainEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out RouteChain chain)
        {
            chain = null;

            var target = ReceiverExpressionPeel.UnwrapTransparent(endpoint);
            if (target == null)
            {
                return false;
            }

            if (!TryFindChainRootEndingAt(target, semanticModel, out var root, out var anchorKind))
            {
                return false;
            }

            var anchor = new RouteChainAnchor(
                anchorKind,
                root,
                root.GetLocation(),
                GetContainingMethod(root, semanticModel));

            var stations = ImmutableArray.CreateBuilder<StationChainLink>();
            var current = root;

            while (!MatchesChainEndpoint(current, endpoint, target))
            {
                if (!TryAdvanceChain(current, semanticModel, stations, out var next, null))
                {
                    return false;
                }

                current = next;
            }

            chain = new RouteChain(anchor, stations.ToImmutable());
            return true;
        }

        /// <summary>
        /// Walks backward from <paramref name="endpoint"/> through Station / StationAsync / ServiceStation
        /// receivers until a <c>new TrainRoute()</c> or resolvable local root is found.
        /// </summary>
        private static bool TryFindChainRootEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind)
        {
            root = null;
            anchorKind = default;

            var current = endpoint;

            while (current != null)
            {
                current = ReceiverExpressionPeel.UnwrapTransparent(current);
                if (current == null)
                {
                    return false;
                }

                if (current is ObjectCreationExpressionSyntax objectCreation
                    && StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel))
                {
                    root = objectCreation;
                    anchorKind = RouteChainAnchorKind.ObjectCreation;
                    return true;
                }

                if (current is IdentifierNameSyntax identifier
                    && TryGetPrecedingTrainRouteCreationAssignment(identifier, semanticModel, out _))
                {
                    root = identifier;
                    anchorKind = RouteChainAnchorKind.LocalVariable;
                    return true;
                }

                if (!TryGetChainMethodReceiver(current, out var receiver))
                {
                    return false;
                }

                current = receiver;
            }

            return false;
        }

        /// <summary>
        /// If <paramref name="expression"/> is a Station / StationAsync / ServiceStation invocation,
        /// returns its receiver expression.
        /// </summary>
        private static bool TryGetChainMethodReceiver(
            ExpressionSyntax expression,
            out ExpressionSyntax receiver)
        {
            receiver = null;

            if (expression is not InvocationExpressionSyntax invocation
                || invocation.Expression is not MemberAccessExpressionSyntax memberAccess
                || !ReferenceEquals(invocation.Expression, memberAccess))
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (!string.Equals(methodName, "Station", StringComparison.Ordinal)
                && !string.Equals(methodName, "StationAsync", StringComparison.Ordinal)
                && !string.Equals(methodName, "ServiceStation", StringComparison.Ordinal))
            {
                return false;
            }

            if (invocation.ArgumentList.Arguments.Count != 2)
            {
                return false;
            }

            receiver = memberAccess.Expression;
            return receiver != null;
        }

        /// <summary>
        /// Determines whether <paramref name="current"/> matches the chain endpoint (raw or unwrapped forms).
        /// </summary>
        private static bool MatchesChainEndpoint(
            ExpressionSyntax current,
            ExpressionSyntax endpoint,
            ExpressionSyntax unwrappedEndpoint)
        {
            if (ReferenceEquals(current, endpoint)
                || ReferenceEquals(current, unwrappedEndpoint))
            {
                return true;
            }

            var unwrappedCurrent = ReceiverExpressionPeel.UnwrapTransparent(current);
            if (ReferenceEquals(unwrappedCurrent, endpoint)
                || ReferenceEquals(unwrappedCurrent, unwrappedEndpoint))
            {
                return true;
            }

            var outermostCurrent = ReceiverExpressionPeel.WrapTransparentOutermost(current);
            var outermostEndpoint = ReceiverExpressionPeel.WrapTransparentOutermost(endpoint);
            return ReferenceEquals(outermostCurrent, outermostEndpoint);
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
            var receiver = ReceiverExpressionPeel.WrapTransparentOutermost(identifier);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
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
        internal static bool TryGetPrecedingTrainRouteCreationAssignment(
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
                    && declarator.Initializer != null
                    && ReceiverExpressionPeel.UnwrapTransparent(declarator.Initializer.Value)
                        is ObjectCreationExpressionSyntax declaratorCreation)
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
                    && ReceiverExpressionPeel.UnwrapTransparent(assignment.Right)
                        is ObjectCreationExpressionSyntax assignmentCreation
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
        internal static bool TryAdvanceChain(
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

            var receiver = ReceiverExpressionPeel.WrapTransparentOutermost(current);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "ServiceStation", StringComparison.Ordinal))
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
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

            var receiver = ReceiverExpressionPeel.WrapTransparentOutermost(current);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!IsTransparentRouteMethod(memberAccess.Name.Identifier.ValueText))
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
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

            var receiver = ReceiverExpressionPeel.WrapTransparentOutermost(current);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!string.Equals(memberAccess.Name.Identifier.ValueText, "Station", StringComparison.Ordinal)
                && !string.Equals(memberAccess.Name.Identifier.ValueText, "StationAsync", StringComparison.Ordinal))
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
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
