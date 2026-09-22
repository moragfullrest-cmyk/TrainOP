using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Local TrainRoute origin window: enumerate assignments, preceding origin, statement-local Collect.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="RouteChainWalker"/> (post-Z0 nesting extract). Fluent root without
    /// local origins and endpoint matching live on <see cref="RouteChainRootResolver"/>.
    /// </remarks>
    internal static class RouteOriginWindow
    {
        /// <summary>
        /// Finds the latest forking assignment RHS (<c>?:</c> / <c>??</c> / <c>switch</c>) to the local
        /// before its use site (used by join-set discovery for statement-local tails).
        /// </summary>
        public static bool TryGetPrecedingForkingAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ExpressionSyntax forkExpression)
        {
            forkExpression = null;
            if (!TryGetLocalTrainRouteInMethod(
                    identifier,
                    semanticModel,
                    out var localSymbol,
                    out var methodDeclaration))
            {
                // Error-typed locals still participate in join discovery.
                if (semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol resolvedLocal)
                {
                    return false;
                }

                methodDeclaration = identifier.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (methodDeclaration == null)
                {
                    return false;
                }

                localSymbol = resolvedLocal;
            }

            ExpressionSyntax latestFork = null;
            var latestSpan = -1;
            foreach (var node in methodDeclaration.DescendantNodes())
            {
                ExpressionSyntax rhs = null;
                var spanStart = -1;

                if (node is VariableDeclaratorSyntax declarator
                    && declarator.Initializer != null
                    && semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol declaredLocal
                    && SymbolEqualityComparer.Default.Equals(declaredLocal, localSymbol))
                {
                    rhs = declarator.Initializer.Value;
                    spanStart = declarator.SpanStart;
                }
                else if (node is AssignmentExpressionSyntax assignment
                    && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && semanticModel.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol assignedLocal
                    && SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol))
                {
                    rhs = assignment.Right;
                    spanStart = assignment.SpanStart;
                }

                if (rhs == null || spanStart < 0 || spanStart >= identifier.SpanStart)
                {
                    continue;
                }

                var unwrapped = ReceiverExpressionSyntaxPeel.UnwrapTransparent(rhs);
                if (!JoinChainsStage.IsForkingExpression(unwrapped)
                    || spanStart <= latestSpan)
                {
                    continue;
                }

                latestSpan = spanStart;
                latestFork = unwrapped;
            }

            if (latestFork == null)
            {
                return false;
            }

            forkExpression = latestFork;
            return true;
        }

        /// <summary>
        /// Finds the latest known-origin assignment to the local before its use site.
        /// </summary>
        /// <remarks>
        /// Recognizes bare <c>new</c>, bare factory, and fluent-RHS whose root is <c>new</c> or factory (SL-3a/3b).
        /// </remarks>
        public static bool TryGetPrecedingTrainRouteOriginAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart)
        {
            return TryGetPrecedingTrainRouteOriginAssignment(
                identifier,
                semanticModel,
                out originExpression,
                out assignmentSpanStart,
                out _);
        }

        /// <summary>
        /// Finds the latest known-origin assignment to the local before its use site,
        /// including the full assignment RHS (for fluent-RHS station collection).
        /// </summary>
        public static bool TryGetPrecedingTrainRouteOriginAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            if (!TryGetLocalTrainRouteInMethod(
                    identifier,
                    semanticModel,
                    out var localSymbol,
                    out var methodDeclaration))
            {
                return false;
            }

            return TryFindLatestOriginAssignmentBefore(
                localSymbol,
                methodDeclaration,
                semanticModel,
                identifier.SpanStart,
                out originExpression,
                out assignmentSpanStart,
                out assignmentRhs);
        }

        /// <summary>
        /// Finds the latest direct <c>new TrainRoute()</c> assignment to the local before its use site.
        /// </summary>
        public static bool TryGetPrecedingTrainRouteCreationAssignment(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ObjectCreationExpressionSyntax creation)
        {
            creation = null;

            if (!TryGetPrecedingTrainRouteOriginAssignment(
                    identifier,
                    semanticModel,
                    out var originExpression,
                    out _))
            {
                return false;
            }

            if (originExpression is not ObjectCreationExpressionSyntax objectCreation)
            {
                return false;
            }

            creation = objectCreation;
            return true;
        }

        /// <summary>
        /// Collects Station / ServiceStation links for a local origin window:
        /// fluent stations on the assignment RHS (if any), then statement roots on the local
        /// until the next origin assignment. Fluent tails on each statement via peel.
        /// </summary>
        public static ImmutableArray<StationChainLink> CollectLocalStatementStationLinks(
            IdentifierNameSyntax localIdentifier,
            SemanticModel semanticModel,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey = null)
        {
            if (localIdentifier == null || semanticModel == null)
            {
                return ImmutableArray<StationChainLink>.Empty;
            }

            if (!TryGetLocalTrainRouteInMethod(
                    localIdentifier,
                    semanticModel,
                    out var localSymbol,
                    out var methodDeclaration))
            {
                return ImmutableArray<StationChainLink>.Empty;
            }

            if (!TryFindLatestOriginAssignmentBefore(
                    localSymbol,
                    methodDeclaration,
                    semanticModel,
                    localIdentifier.SpanStart,
                    out _,
                    out var originAssignmentSpanStart,
                    out var assignmentRhs))
            {
                return ImmutableArray<StationChainLink>.Empty;
            }

            var windowEnd = int.MaxValue;
            if (TryFindNextOriginAssignmentAfter(
                    localSymbol,
                    methodDeclaration,
                    semanticModel,
                    originAssignmentSpanStart,
                    out var nextOriginSpanStart))
            {
                windowEnd = nextOriginSpanStart;
            }

            var stations = ImmutableArray.CreateBuilder<StationChainLink>();

            // SL-3a: stations on fluent assignment RHS come first (do NOT call EndingAt —
            // that re-enters Collect for locals and can fail factory-path simulation).
            if (assignmentRhs != null)
            {
                AppendFluentStationsFromRootToEndpoint(
                    assignmentRhs,
                    semanticModel,
                    stations,
                    stationSitesByKey);
            }

            var statementRoots = new List<InvocationExpressionSyntax>();
            foreach (var node in methodDeclaration.DescendantNodes())
            {
                if (node is not InvocationExpressionSyntax invocation
                    || !StationSyntaxHelper.MatchesStationOrServiceStationShape(invocation, out var memberAccess))
                {
                    continue;
                }

                var spanStart = invocation.SpanStart;
                if (spanStart <= originAssignmentSpanStart || spanStart >= windowEnd)
                {
                    continue;
                }

                var receiver = ReceiverExpressionSyntaxPeel.UnwrapTransparent(memberAccess.Expression);
                if (receiver is not IdentifierNameSyntax receiverIdentifier
                    || semanticModel.GetSymbolInfo(receiverIdentifier).Symbol is not ILocalSymbol receiverLocal
                    || !SymbolEqualityComparer.Default.Equals(receiverLocal, localSymbol))
                {
                    continue;
                }

                statementRoots.Add(invocation);
            }

            statementRoots.Sort((left, right) => left.SpanStart.CompareTo(right.SpanStart));

            for (var i = 0; i < statementRoots.Count; i++)
            {
                AppendStatementRootAndFluentTail(
                    statementRoots[i],
                    semanticModel,
                    stations,
                    stationSitesByKey);
            }

            return stations.ToImmutable();
        }

        /// <summary>
        /// Finds a syntax identifier for a local that was initialized/assigned from a fluent
        /// chain rooted at <paramref name="chainRoot"/> (<c>new</c> or factory + <c>.Station</c>).
        /// </summary>
        public static bool TryFindLocalIdentifierAssignedFromFluentCreation(
            ExpressionSyntax chainRoot,
            SemanticModel semanticModel,
            out IdentifierNameSyntax localIdentifier)
        {
            localIdentifier = null;
            if (chainRoot == null || semanticModel == null)
            {
                return false;
            }

            if (!TryGetLocalSymbolAssignedFromFluentCreation(
                    chainRoot,
                    semanticModel,
                    out var localSymbol,
                    out var methodDeclaration))
            {
                return false;
            }

            foreach (var node in methodDeclaration.DescendantNodes())
            {
                if (node is not IdentifierNameSyntax identifier)
                {
                    continue;
                }

                if (semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol candidate
                    || !SymbolEqualityComparer.Default.Equals(candidate, localSymbol))
                {
                    continue;
                }

                localIdentifier = identifier;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves anchor location for a chain root (origin location for locals).
        /// </summary>
        public static Location ResolveAnchorLocation(
            ExpressionSyntax root,
            SemanticModel semanticModel)
        {
            if (root is IdentifierNameSyntax identifier
                && TryGetPrecedingTrainRouteOriginAssignment(
                    identifier,
                    semanticModel,
                    out var originExpression,
                    out _))
            {
                return originExpression.GetLocation();
            }

            return root.GetLocation();
        }

        private static void AppendFluentStationsFromRootToEndpoint(
            ExpressionSyntax endpoint,
            SemanticModel semanticModel,
            ImmutableArray<StationChainLink>.Builder stations,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey)
        {
            var target = ReceiverExpressionSyntaxPeel.UnwrapTransparent(endpoint);
            if (target == null || stations == null)
            {
                return;
            }

            if (!RouteChainRootResolver.TryFindFluentChainRootWithoutLocalOrigins(
                    target,
                    semanticModel,
                    out var root))
            {
                return;
            }

            var current = root;
            while (!RouteChainRootResolver.MatchesChainEndpoint(current, endpoint, target))
            {
                if (!RouteChainPeel.TryAdvanceChain(
                    current,
                    semanticModel,
                    stations,
                    out current,
                    null,
                    stationSitesByKey))
                {
                    return;
                }
            }
        }

        private static void AppendStatementRootAndFluentTail(
            InvocationExpressionSyntax rootInvocation,
            SemanticModel semanticModel,
            ImmutableArray<StationChainLink>.Builder stations,
            IReadOnlyDictionary<string, RouteSite> stationSitesByKey)
        {
            if (StationLinkMaterializer.TryMaterialize(
                    rootInvocation,
                    semanticModel,
                    stationSitesByKey,
                    out var link))
            {
                stations.Add(link.ToStationChainLink());
            }

            var current = (ExpressionSyntax)rootInvocation;
            while (RouteChainPeel.TryAdvanceChain(
                current,
                semanticModel,
                stations,
                out current,
                null,
                stationSitesByKey))
            {
            }
        }

        private static bool TryGetLocalTrainRouteInMethod(
            IdentifierNameSyntax identifier,
            SemanticModel semanticModel,
            out ILocalSymbol localSymbol,
            out MethodDeclarationSyntax methodDeclaration)
        {
            localSymbol = null;
            methodDeclaration = null;

            if (semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol resolvedLocal)
            {
                return false;
            }

            methodDeclaration = identifier.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                return false;
            }

            var containingMethod = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
            if (containingMethod == null
                || !SymbolEqualityComparer.Default.Equals(resolvedLocal.ContainingSymbol, containingMethod))
            {
                methodDeclaration = null;
                return false;
            }

            if (!StationSyntaxHelper.IsTrainRoute(resolvedLocal.Type)
                && !IsErrorTypedLocalWithKnownTrainRouteOrigin(
                    resolvedLocal,
                    methodDeclaration,
                    semanticModel))
            {
                return false;
            }

            localSymbol = resolvedLocal;
            return true;
        }

        /// <summary>
        /// Analyzer-only compilations often lack generated <c>Station</c> stubs, so
        /// <c>var route = new TrainRoute().Station(...)</c> types as error. Still accept the local
        /// when an assignment RHS classifies as a known TrainRoute origin.
        /// </summary>
        private static bool IsErrorTypedLocalWithKnownTrainRouteOrigin(
            ILocalSymbol localSymbol,
            MethodDeclarationSyntax methodDeclaration,
            SemanticModel semanticModel)
        {
            if (localSymbol?.Type?.TypeKind != TypeKind.Error)
            {
                return false;
            }

            foreach (var _ in EnumerateOriginAssignments(localSymbol, methodDeclaration, semanticModel))
            {
                return true;
            }

            return false;
        }

        private static bool TryFindLatestOriginAssignmentBefore(
            ILocalSymbol localSymbol,
            MethodDeclarationSyntax methodDeclaration,
            SemanticModel semanticModel,
            int usePosition,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            foreach (var candidate in EnumerateOriginAssignments(localSymbol, methodDeclaration, semanticModel))
            {
                if (candidate.AssignmentSpanStart >= usePosition
                    || candidate.AssignmentSpanStart <= assignmentSpanStart)
                {
                    continue;
                }

                assignmentSpanStart = candidate.AssignmentSpanStart;
                originExpression = candidate.OriginExpression;
                assignmentRhs = candidate.AssignmentRhs;
            }

            return originExpression != null;
        }

        private static bool TryFindNextOriginAssignmentAfter(
            ILocalSymbol localSymbol,
            MethodDeclarationSyntax methodDeclaration,
            SemanticModel semanticModel,
            int afterPosition,
            out int nextAssignmentSpanStart)
        {
            nextAssignmentSpanStart = -1;

            foreach (var candidate in EnumerateOriginAssignments(localSymbol, methodDeclaration, semanticModel))
            {
                if (candidate.AssignmentSpanStart <= afterPosition)
                {
                    continue;
                }

                if (nextAssignmentSpanStart < 0
                    || candidate.AssignmentSpanStart < nextAssignmentSpanStart)
                {
                    nextAssignmentSpanStart = candidate.AssignmentSpanStart;
                }
            }

            return nextAssignmentSpanStart >= 0;
        }

        private static IEnumerable<(ExpressionSyntax OriginExpression, int AssignmentSpanStart, ExpressionSyntax AssignmentRhs)> EnumerateOriginAssignments(
            ILocalSymbol localSymbol,
            MethodDeclarationSyntax methodDeclaration,
            SemanticModel semanticModel)
        {
            foreach (var node in methodDeclaration.DescendantNodes())
            {
                if (TryGetOriginCandidate(
                        node,
                        localSymbol,
                        semanticModel,
                        out var originExpression,
                        out var assignmentSpanStart,
                        out var assignmentRhs))
                {
                    yield return (originExpression, assignmentSpanStart, assignmentRhs);
                }
            }
        }

        /// <summary>
        /// Dispatches one syntax node to a known origin candidate shape (declarator / assign /
        /// out / deconstruct / pattern / join fork).
        /// </summary>
        private static bool TryGetOriginCandidate(
            SyntaxNode node,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            if (TryGetDeclaratorOriginCandidate(
                    node,
                    localSymbol,
                    semanticModel,
                    out originExpression,
                    out assignmentSpanStart,
                    out assignmentRhs))
            {
                return true;
            }

            if (TryGetSimpleAssignmentOriginCandidate(
                    node,
                    localSymbol,
                    semanticModel,
                    out originExpression,
                    out assignmentSpanStart,
                    out assignmentRhs))
            {
                return true;
            }

            if (node is ArgumentSyntax argument
                && TryGetOutArgumentLocal(argument, semanticModel, out var outLocal)
                && SymbolEqualityComparer.Default.Equals(outLocal, localSymbol)
                && argument.Parent is ArgumentListSyntax argumentList
                && argumentList.Parent is InvocationExpressionSyntax outInvocation
                && TryClassifyOutFactoryInvocation(outInvocation, semanticModel, out var outOrigin))
            {
                originExpression = outOrigin;
                assignmentSpanStart = outInvocation.SpanStart;
                assignmentRhs = null;
                return true;
            }

            if (node is AssignmentExpressionSyntax deconstructAssignment
                && TryGetTupleDeconstructOriginAssignment(
                    deconstructAssignment,
                    localSymbol,
                    semanticModel,
                    out originExpression,
                    out assignmentSpanStart,
                    out assignmentRhs))
            {
                return true;
            }

            return TryGetPatternOriginAssignment(
                node,
                localSymbol,
                semanticModel,
                out originExpression,
                out assignmentSpanStart,
                out assignmentRhs);
        }

        private static bool TryGetDeclaratorOriginCandidate(
            SyntaxNode node,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            if (node is not VariableDeclaratorSyntax declarator
                || declarator.Initializer == null
                || semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol declaredLocal
                || !SymbolEqualityComparer.Default.Equals(declaredLocal, localSymbol))
            {
                return false;
            }

            return TryClassifyRhsOrigin(
                declarator.Initializer.Value,
                declarator.SpanStart,
                semanticModel,
                out originExpression,
                out assignmentSpanStart,
                out assignmentRhs);
        }

        private static bool TryGetSimpleAssignmentOriginCandidate(
            SyntaxNode node,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            if (node is not AssignmentExpressionSyntax assignment
                || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || semanticModel.GetSymbolInfo(assignment.Left).Symbol is not ILocalSymbol assignedLocal
                || !SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol))
            {
                return false;
            }

            // Tuple deconstruct is handled separately (element-level RHS).
            if (ReceiverExpressionSyntaxPeel.UnwrapTransparent(assignment.Right) is TupleExpressionSyntax)
            {
                return false;
            }

            return TryClassifyRhsOrigin(
                assignment.Right,
                assignment.SpanStart,
                semanticModel,
                out originExpression,
                out assignmentSpanStart,
                out assignmentRhs);
        }

        private static bool TryClassifyRhsOrigin(
            ExpressionSyntax rhs,
            int spanStart,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            if (rhs == null || spanStart < 0)
            {
                return false;
            }

            if (TryClassifyTrainRouteOriginExpression(rhs, semanticModel, out originExpression))
            {
                assignmentSpanStart = spanStart;
                assignmentRhs = rhs;
                return true;
            }

            if (TryGetJoinedForkOrigin(rhs, semanticModel, out var forkOrigin))
            {
                originExpression = forkOrigin;
                assignmentSpanStart = spanStart;
                assignmentRhs = null;
                return true;
            }

            return false;
        }

        private static bool TryGetJoinedForkOrigin(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out ExpressionSyntax forkOrigin)
        {
            forkOrigin = null;
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (!JoinChainsStage.IsForkingExpression(expression))
            {
                return false;
            }

            if (!RouteAnchorDetector.TryValidateLocalAssignJoin(
                    expression,
                    semanticModel,
                    out var validation)
                || !validation.CanMerge)
            {
                return false;
            }

            forkOrigin = expression;
            return true;
        }

        /// <summary>
        /// Pattern-declared local (<c>expr is TrainRoute r</c> / <c>case TrainRoute r</c> /
        /// switch-expression arm) when the matched expression is a known origin.
        /// </summary>
        private static bool TryGetPatternOriginAssignment(
            SyntaxNode node,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            if (node == null || localSymbol == null || semanticModel == null)
            {
                return false;
            }

            if (node is IsPatternExpressionSyntax isPattern
                && TryGetTrainRoutePatternLocal(isPattern.Pattern, semanticModel, out var isLocal)
                && SymbolEqualityComparer.Default.Equals(isLocal, localSymbol)
                && TryClassifyTrainRouteOriginExpression(
                    isPattern.Expression,
                    semanticModel,
                    out originExpression))
            {
                assignmentSpanStart = isPattern.SpanStart;
                assignmentRhs = isPattern.Expression;
                return true;
            }

            if (node is CasePatternSwitchLabelSyntax caseLabel
                && TryGetTrainRoutePatternLocal(caseLabel.Pattern, semanticModel, out var caseLocal)
                && SymbolEqualityComparer.Default.Equals(caseLocal, localSymbol)
                && caseLabel.Parent?.Parent is SwitchStatementSyntax switchStatement
                && TryClassifyTrainRouteOriginExpression(
                    switchStatement.Expression,
                    semanticModel,
                    out originExpression))
            {
                assignmentSpanStart = caseLabel.SpanStart;
                assignmentRhs = switchStatement.Expression;
                return true;
            }

            if (node is SwitchExpressionArmSyntax arm
                && TryGetTrainRoutePatternLocal(arm.Pattern, semanticModel, out var armLocal)
                && SymbolEqualityComparer.Default.Equals(armLocal, localSymbol)
                && arm.Parent is SwitchExpressionSyntax switchExpression
                && TryClassifyTrainRouteOriginExpression(
                    switchExpression.GoverningExpression,
                    semanticModel,
                    out originExpression))
            {
                assignmentSpanStart = arm.SpanStart;
                assignmentRhs = switchExpression.GoverningExpression;
                return true;
            }

            return false;
        }

        private static bool TryGetTrainRoutePatternLocal(
            PatternSyntax pattern,
            SemanticModel semanticModel,
            out ILocalSymbol localSymbol)
        {
            localSymbol = null;
            if (pattern == null || semanticModel == null)
            {
                return false;
            }

            if (pattern is DeclarationPatternSyntax declaration
                && declaration.Designation is SingleVariableDesignationSyntax designation
                && semanticModel.GetDeclaredSymbol(designation) is ILocalSymbol declaredLocal
                && (StationSyntaxHelper.IsTrainRoute(declaredLocal.Type)
                    || declaredLocal.Type?.TypeKind == TypeKind.Error))
            {
                localSymbol = declaredLocal;
                return true;
            }

            if (pattern is VarPatternSyntax varPattern
                && varPattern.Designation is SingleVariableDesignationSyntax varDesignation
                && semanticModel.GetDeclaredSymbol(varDesignation) is ILocalSymbol varLocal
                && (StationSyntaxHelper.IsTrainRoute(varLocal.Type)
                    || varLocal.Type?.TypeKind == TypeKind.Error))
            {
                localSymbol = varLocal;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Narrow deconstruct: <c>(var r, _) = (knownOrigin, ...)</c> / <c>var (r, _) = (...)</c>
        /// where the matching RHS element classifies as a TrainRoute origin.
        /// </summary>
        private static bool TryGetTupleDeconstructOriginAssignment(
            AssignmentExpressionSyntax assignment,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression,
            out int assignmentSpanStart,
            out ExpressionSyntax assignmentRhs)
        {
            originExpression = null;
            assignmentSpanStart = -1;
            assignmentRhs = null;

            if (assignment == null
                || localSymbol == null
                || semanticModel == null
                || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return false;
            }

            var right = ReceiverExpressionSyntaxPeel.UnwrapTransparent(assignment.Right);
            if (right is not TupleExpressionSyntax tupleRhs)
            {
                return false;
            }

            if (!TryFindDeconstructLocalIndex(
                    assignment.Left,
                    localSymbol,
                    semanticModel,
                    out var index)
                || index < 0
                || index >= tupleRhs.Arguments.Count)
            {
                return false;
            }

            var element = tupleRhs.Arguments[index].Expression;
            if (!TryClassifyTrainRouteOriginExpression(element, semanticModel, out originExpression))
            {
                return false;
            }

            assignmentSpanStart = assignment.SpanStart;
            assignmentRhs = element;
            return true;
        }

        private static bool TryFindDeconstructLocalIndex(
            ExpressionSyntax left,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            out int index)
        {
            index = -1;
            left = ReceiverExpressionSyntaxPeel.UnwrapTransparent(left);
            if (left == null)
            {
                return false;
            }

            if (left is TupleExpressionSyntax tupleLeft)
            {
                for (var i = 0; i < tupleLeft.Arguments.Count; i++)
                {
                    if (TryMatchDeconstructSlot(
                            tupleLeft.Arguments[i].Expression,
                            localSymbol,
                            semanticModel))
                    {
                        index = i;
                        return true;
                    }
                }

                return false;
            }

            if (left is DeclarationExpressionSyntax declaration
                && declaration.Designation is ParenthesizedVariableDesignationSyntax parenthesized)
            {
                for (var i = 0; i < parenthesized.Variables.Count; i++)
                {
                    if (parenthesized.Variables[i] is not SingleVariableDesignationSyntax single)
                    {
                        continue;
                    }

                    if (semanticModel.GetDeclaredSymbol(single) is ILocalSymbol declared
                        && SymbolEqualityComparer.Default.Equals(declared, localSymbol))
                    {
                        index = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryMatchDeconstructSlot(
            ExpressionSyntax slot,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel)
        {
            slot = ReceiverExpressionSyntaxPeel.UnwrapTransparent(slot);
            if (slot == null)
            {
                return false;
            }

            if (slot is DeclarationExpressionSyntax declaration
                && declaration.Designation is SingleVariableDesignationSyntax designation
                && semanticModel.GetDeclaredSymbol(designation) is ILocalSymbol declaredLocal
                && SymbolEqualityComparer.Default.Equals(declaredLocal, localSymbol))
            {
                return StationSyntaxHelper.IsTrainRoute(declaredLocal.Type)
                    || declaredLocal.Type?.TypeKind == TypeKind.Error;
            }

            if (slot is IdentifierNameSyntax identifier
                && semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol existingLocal
                && SymbolEqualityComparer.Default.Equals(existingLocal, localSymbol))
            {
                return StationSyntaxHelper.IsTrainRoute(existingLocal.Type)
                    || existingLocal.Type?.TypeKind == TypeKind.Error;
            }

            return false;
        }

        private static bool TryGetOutArgumentLocal(
            ArgumentSyntax argument,
            SemanticModel semanticModel,
            out ILocalSymbol localSymbol)
        {
            localSymbol = null;
            if (argument == null
                || semanticModel == null
                || !argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                return false;
            }

            if (argument.Expression is DeclarationExpressionSyntax declaration
                && declaration.Designation is SingleVariableDesignationSyntax designation
                && semanticModel.GetDeclaredSymbol(designation) is ILocalSymbol declaredLocal)
            {
                localSymbol = declaredLocal;
                return StationSyntaxHelper.IsTrainRoute(declaredLocal.Type)
                    || declaredLocal.Type?.TypeKind == TypeKind.Error;
            }

            if (argument.Expression is IdentifierNameSyntax identifier
                && semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol existingLocal)
            {
                localSymbol = existingLocal;
                return StationSyntaxHelper.IsTrainRoute(existingLocal.Type)
                    || existingLocal.Type?.TypeKind == TypeKind.Error;
            }

            return false;
        }

        private static bool TryClassifyOutFactoryInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression)
        {
            originExpression = null;
            if (!RouteChainRootResolver.TryResolveFactoryRoot(
                    invocation,
                    semanticModel,
                    out var factoryRoot,
                    out _,
                    out _,
                    out _))
            {
                return false;
            }

            originExpression = factoryRoot;
            return true;
        }

        /// <summary>
        /// Classifies an assignment RHS as a known TrainRoute origin
        /// (bare <c>new</c>; bare factory; fluent-RHS with root <c>new</c> or factory — SL-3a/3b).
        /// </summary>
        private static bool TryClassifyTrainRouteOriginExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            out ExpressionSyntax originExpression)
        {
            originExpression = null;
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                return false;
            }

            if (expression is ObjectCreationExpressionSyntax objectCreation
                && StationSyntaxHelper.IsTrainRouteCreation(objectCreation, semanticModel))
            {
                originExpression = objectCreation;
                return true;
            }

            // SL-2a/2b: bare factory.
            if (RouteChainRootResolver.TryResolveFactoryRoot(
                    expression,
                    semanticModel,
                    out var factoryRoot,
                    out _,
                    out _,
                    out _))
            {
                originExpression = factoryRoot;
                return true;
            }

            // SL-3a/3b: fluent-RHS Station chain whose root is new or factory.
            if (expression is InvocationExpressionSyntax invocation
                && StationSyntaxHelper.MatchesStationOrServiceStationShape(invocation, out _)
                && RouteChainRootResolver.TryFindFluentChainRootWithoutLocalOrigins(
                    expression,
                    semanticModel,
                    out var fluentRoot))
            {
                originExpression = fluentRoot;
                return true;
            }

            return false;
        }

        private static bool TryGetLocalSymbolAssignedFromFluentCreation(
            ExpressionSyntax chainRoot,
            SemanticModel semanticModel,
            out ILocalSymbol localSymbol,
            out MethodDeclarationSyntax methodDeclaration)
        {
            localSymbol = null;
            methodDeclaration = null;

            var outermost = chainRoot;
            while (true)
            {
                var wrapped = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(outermost);
                if (wrapped.Parent is MemberAccessExpressionSyntax memberAccess
                    && ReferenceEquals(memberAccess.Expression, wrapped)
                    && StationSyntaxHelper.IsStationOrServiceStationMethodName(
                        memberAccess.Name.Identifier.ValueText)
                    && memberAccess.Parent is InvocationExpressionSyntax invocation
                    && ReferenceEquals(invocation.Expression, memberAccess))
                {
                    outermost = invocation;
                    continue;
                }

                break;
            }

            if (ReferenceEquals(outermost, chainRoot))
            {
                return false;
            }

            var rhs = ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(outermost);
            methodDeclaration = chainRoot.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDeclaration == null)
            {
                return false;
            }

            if (rhs.Parent is EqualsValueClauseSyntax equals
                && equals.Parent is VariableDeclaratorSyntax declarator
                && semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol declaredLocal
                && (StationSyntaxHelper.IsTrainRoute(declaredLocal.Type)
                    || declaredLocal.Type?.TypeKind == TypeKind.Error))
            {
                localSymbol = declaredLocal;
                return true;
            }

            if (rhs.Parent is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && ReferenceEquals(
                    ReceiverExpressionSyntaxPeel.WrapTransparentOutermost(assignment.Right),
                    rhs)
                && semanticModel.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol assignedLocal
                && (StationSyntaxHelper.IsTrainRoute(assignedLocal.Type)
                    || assignedLocal.Type?.TypeKind == TypeKind.Error))
            {
                localSymbol = assignedLocal;
                return true;
            }

            return false;
        }
    }
}
