using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Walk-primitives for fluent route chain traversal (used by RouteGraphAssembler).
    /// </summary>
    internal static class RouteChainDetector
    {
        /// <summary>
        /// Attempts to detect a route chain anchor at the given syntax node.
        /// </summary>
        internal static bool TryDetectAnchorSite(
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

            if (node is InvocationExpressionSyntax factoryInvocation
                && TryDetectFactoryInvocationAnchor(factoryInvocation, semanticModel, out anchor))
            {
                return true;
            }

            return false;
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
        /// Determines whether <paramref name="expression"/> is a bare user-defined factory invocation
        /// (e.g. <c>GetRoute()</c>) with no inline fluent stations at the call site.
        /// </summary>
        internal static bool IsBareUserDefinedFactoryInvocation(
            ExpressionSyntax expression,
            SemanticModel semanticModel)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                return false;
            }

            return TryResolveFactoryRoot(
                expression,
                semanticModel,
                out _,
                out _,
                out _,
                out _);
        }

        /// <summary>
        /// Builds a <see cref="RouteChain"/> from a known chain root forward until
        /// <paramref name="endpoint"/> (inclusive), without continuing past a fork into an outer join.
        /// </summary>
        /// <remarks>
        /// Unlike endpoint-based chain building, linear assembly accepts bare <c>new TrainRoute()</c> / local endpoints
        /// with zero stations, and does not require a local identifier to already be a Station receiver.
        /// </remarks>
        internal static bool TryBuildChainEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out RouteChain chain)
        {
            chain = null;

            var target = ReceiverExpressionSyntaxPeel.UnwrapTransparent(endpoint);
            if (target == null)
            {
                return false;
            }

            if (!TryFindChainRootEndingAt(target, semanticModel, out var root, out var anchorKind, out var factoryMethod, out var initialWagons))
            {
                return false;
            }

            var anchor = new RouteChainAnchor(
                anchorKind,
                root,
                root.GetLocation(),
                GetContainingMethod(root, semanticModel),
                factoryMethod,
                initialWagons);

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
        /// Builds a factory extension chain ending at <paramref name="endpoint"/>.
        /// </summary>
        internal static bool TryBuildFactoryExtensionChain(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            Compilation compilation,
            out RouteChain chain,
            out ImmutableArray<Diagnostic> diagnostics)
        {
            chain = null;
            diagnostics = ImmutableArray<Diagnostic>.Empty;

            if (!TryBuildChainEndingAt(endpoint, semanticModel, out chain))
            {
                return false;
            }

            if (chain.Anchor.Kind != RouteChainAnchorKind.MethodInvocation
                && chain.Anchor.Kind != RouteChainAnchorKind.FactorySchema)
            {
                chain = null;
                return false;
            }

            if (chain.Anchor.FactoryMethod != null)
            {
                RouteFactoryResolver.TryResolve(
                    chain.Anchor.FactoryMethod,
                    compilation,
                    chain.Anchor.Location,
                    out _,
                    out diagnostics);
            }

            return true;
        }

        /// <summary>
        /// Walks backward from <paramref name="endpoint"/> through Station / ServiceStation
        /// receivers until a resolvable chain root is found.
        /// </summary>
        internal static bool TryFindChainRootEndingAt(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind,
            out IMethodSymbol factoryMethod,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            root = null;
            anchorKind = default;
            factoryMethod = null;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            var current = endpoint;

            while (current != null)
            {
                current = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
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

                if (TryResolveFactoryRoot(current, semanticModel, out root, out anchorKind, out factoryMethod, out initialWagons))
                {
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

        private static bool TryResolveFactoryRoot(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            out ExpressionSyntax root,
            out RouteChainAnchorKind anchorKind,
            out IMethodSymbol factoryMethod,
            out ImmutableArray<WagonBinding> initialWagons)
        {
            root = null;
            anchorKind = default;
            factoryMethod = null;
            initialWagons = ImmutableArray<WagonBinding>.Empty;

            if (current is not InvocationExpressionSyntax factoryInvocation)
            {
                return false;
            }

            if (StationSyntaxHelper.IsCandidateStationInvocation(factoryInvocation)
                || StationSyntaxHelper.IsCandidateServiceStationInvocation(factoryInvocation))
            {
                return false;
            }

            if (semanticModel.GetSymbolInfo(factoryInvocation).Symbol is not IMethodSymbol methodSymbol
                || !StationSyntaxHelper.IsTrainRoute(methodSymbol.ReturnType)
                || !IsUserDefinedRouteFactory(methodSymbol))
            {
                return false;
            }

            factoryMethod = methodSymbol;
            anchorKind = FactoryAccessibilityHelper.RequiresSchemaLookup(methodSymbol, semanticModel.Compilation)
                ? RouteChainAnchorKind.FactorySchema
                : RouteChainAnchorKind.MethodInvocation;

            RouteFactoryResolver.TryResolve(
                methodSymbol,
                semanticModel.Compilation,
                factoryInvocation.GetLocation(),
                out initialWagons,
                out _);

            root = factoryInvocation;
            return true;
        }

        /// <summary>
        /// If <paramref name="expression"/> is a Station / ServiceStation invocation,
        /// returns its receiver expression.
        /// </summary>
        private static bool TryGetChainMethodReceiver(
            ExpressionSyntax expression,
            out ExpressionSyntax receiver)
        {
            receiver = null;

            if (expression is not InvocationExpressionSyntax invocation
                || !StationSyntaxHelper.MatchesStationOrServiceStationShape(invocation, out var memberAccess))
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

            var unwrappedCurrent = ReceiverExpressionSyntaxPeel.UnwrapTransparent(current);
            if (ReferenceEquals(unwrappedCurrent, endpoint)
                || ReferenceEquals(unwrappedCurrent, unwrappedEndpoint))
            {
                return true;
            }

            var outermostCurrent = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
            var outermostEndpoint = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(endpoint);
            return ReferenceEquals(outermostCurrent, outermostEndpoint);
        }

        /// <summary>
        /// Advances along a route chain by resolving the next station or service-station invocation.
        /// </summary>
        internal static bool TryAdvanceChain(
            ExpressionSyntax current,
            SemanticModel semanticModel,
            ImmutableArray<StationChainLink>.Builder stations,
            out ExpressionSyntax next,
            ImmutableArray<InvocationExpressionSyntax>.Builder chainedInvocations,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey = null)
        {
            next = current;

            if (TryGetDirectServiceStationInvocation(current, out var serviceInvocation)
                && TryCreateServiceStationLink(serviceInvocation, semanticModel, stationSitesByKey, out var serviceLink))
            {
                stations?.Add(serviceLink);
                chainedInvocations?.Add(serviceInvocation);
                next = serviceInvocation;
                return true;
            }

            if (!TryGetNextStationInvocation(current, out var stationInvocation))
            {
                return false;
            }

            if (TryCreateStationLink(stationInvocation, semanticModel, stationSitesByKey, out var stationLink))
            {
                stations?.Add(stationLink);
            }

            chainedInvocations?.Add(stationInvocation);
            next = stationInvocation;
            return true;
        }

        private static bool TryCreateServiceStationLink(
            InvocationExpressionSyntax serviceInvocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            out StationChainLink link)
        {
            link = null;
            if (TryGetPrebuiltStationSite(serviceInvocation, stationSitesByKey, RouteSiteKind.ServiceStation, out var site))
            {
                link = new StationChainLink(
                    site.StationName,
                    site.HandlerLocation,
                    site.HandlerLocation,
                    site.HandlerBinding,
                    serviceInvocation);
                return true;
            }

            if (StationSyntaxHelper.TryGetDataServiceStationInvocation(
                    serviceInvocation,
                    semanticModel,
                    out var serviceStationName,
                    out var serviceHandlerLocation,
                    out var serviceHandlerBinding))
            {
                link = new StationChainLink(
                    serviceStationName,
                    serviceHandlerLocation,
                    serviceHandlerLocation,
                    serviceHandlerBinding,
                    serviceInvocation);
                return true;
            }

            return false;
        }

        private static bool TryCreateStationLink(
            InvocationExpressionSyntax stationInvocation,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            out StationChainLink link)
        {
            link = null;
            if (TryGetPrebuiltStationSite(stationInvocation, stationSitesByKey, RouteSiteKind.Station, out var site))
            {
                link = new StationChainLink(
                    site.StationName,
                    stationInvocation.ArgumentList.Arguments[0].GetLocation(),
                    site.HandlerLocation,
                    site.HandlerBinding,
                    stationInvocation);
                return true;
            }

            if (StationSyntaxHelper.TryGetDataStationInvocation(
                    stationInvocation,
                    semanticModel,
                    out var stationName,
                    out var handlerLocation,
                    out var handlerBinding))
            {
                link = new StationChainLink(
                    stationName,
                    stationInvocation.ArgumentList.Arguments[0].GetLocation(),
                    handlerLocation,
                    handlerBinding,
                    stationInvocation);
                return true;
            }

            return false;
        }

        private static bool TryGetPrebuiltStationSite(
            InvocationExpressionSyntax invocation,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey,
            RouteSiteKind expectedKind,
            out RouteSite site)
        {
            site = null;
            if (stationSitesByKey == null || invocation == null)
            {
                return false;
            }

            var key = ChainStationCallIndex.BuildLocationKey(invocation.GetLocation());
            if (key.Length == 0
                || !stationSitesByKey.TryGetValue(key, out site)
                || site.Kind != expectedKind
                || site.HandlerBinding == null)
            {
                site = null;
                return false;
            }

            return true;
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

            // Preserve the ctor call-site location so runtime caller line stamping matches generator.
            // For local-variable chains the anchor receiver is an identifier, but ctor stamp is on the preceding `new TrainRoute()`.
            if (!TryGetPrecedingTrainRouteCreationAssignment(identifier, semanticModel, out var creation))
            {
                return false;
            }

            anchor = new RouteChainAnchor(
                RouteChainAnchorKind.LocalVariable,
                identifier,
                creation.GetLocation(),
                GetContainingMethod(identifier, semanticModel));
            return true;
        }

        /// <summary>
        /// Detects an anchor at a factory invocation that begins an extension chain.
        /// </summary>
        private static bool TryDetectFactoryInvocationAnchor(
            InvocationExpressionSyntax factoryInvocation,
            SemanticModel semanticModel,
            out RouteChainAnchor anchor)
        {
            anchor = null;

            if (!IsFactoryChainReceiver(factoryInvocation))
            {
                return false;
            }

            if (!TryResolveFactoryRoot(
                factoryInvocation,
                semanticModel,
                out var root,
                out var anchorKind,
                out var factoryMethod,
                out var initialWagons))
            {
                return false;
            }

            anchor = new RouteChainAnchor(
                anchorKind,
                root,
                root.GetLocation(),
                GetContainingMethod(root, semanticModel),
                factoryMethod,
                initialWagons);
            return true;
        }

        /// <summary>
        /// Determines whether the invocation is the receiver of a route handler member access.
        /// </summary>
        private static bool IsFactoryChainReceiver(InvocationExpressionSyntax factoryInvocation)
        {
            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(factoryInvocation);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            return StationSyntaxHelper.IsStationOrServiceStationMethodName(methodName);
        }

        /// <summary>
        /// Determines whether the identifier is the receiver of a route handler member access.
        /// </summary>
        private static bool IsLocalVariableChainReceiver(IdentifierNameSyntax identifier)
        {
            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(identifier);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!ReferenceEquals(memberAccess.Expression, receiver))
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            return StationSyntaxHelper.IsStationOrServiceStationMethodName(methodName);
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
                    && ReceiverExpressionSyntaxPeel.UnwrapTransparent(declarator.Initializer.Value)
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
                    && ReceiverExpressionSyntaxPeel.UnwrapTransparent(assignment.Right)
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
        /// Detects a direct ServiceStation invocation immediately following the current expression.
        /// </summary>
        private static bool TryGetDirectServiceStationInvocation(
            ExpressionSyntax current,
            out InvocationExpressionSyntax serviceStationInvocation)
        {
            return TryGetDirectRouteHandlerInvocation(
                current,
                HandlerStationKind.ServiceStation,
                out serviceStationInvocation);
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

            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
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
            return TryGetDirectRouteHandlerInvocation(current, HandlerStationKind.Station, out stationInvocation);
        }

        /// <summary>
        /// Detects a direct route handler invocation immediately following the current expression.
        /// </summary>
        private static bool TryGetDirectRouteHandlerInvocation(
            ExpressionSyntax current,
            HandlerStationKind stationKind,
            out InvocationExpressionSyntax routeHandlerInvocation)
        {
            routeHandlerInvocation = null;

            var receiver = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(current);
            if (receiver.Parent is not MemberAccessExpressionSyntax memberAccess)
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

            if (!StationSyntaxHelper.TryParseRouteHandlerInvocation(invocation, out var parsedKind, out _)
                || parsedKind != stationKind)
            {
                return false;
            }

            routeHandlerInvocation = invocation;
            return true;
        }

        /// <summary>
        /// Determines whether a route method name is transparent for chain traversal.
        /// </summary>
        private static bool IsTransparentRouteMethod(string methodName)
        {
            return StationSyntaxHelper.IsServiceStationMethodName(methodName);
        }

        /// <summary>
        /// Returns true for user-authored factory methods, excluding fluent route API, generated extensions, and delegate Invoke.
        /// </summary>
        private static bool IsUserDefinedRouteFactory(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null)
            {
                return false;
            }

            var containingType = methodSymbol.ContainingType;
            if (containingType == null)
            {
                return true;
            }

            if (containingType.TypeKind == TypeKind.Delegate)
            {
                return false;
            }

            if (StationSyntaxHelper.IsTrainRoute(containingType))
            {
                return false;
            }

            return !string.Equals(containingType.Name, "TrainRouteStationExtensions", StringComparison.Ordinal);
        }
    }
}
