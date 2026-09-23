using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using TrainOP.Generators.Parts;

namespace TrainOP.Generators
{
    /// <summary>
    /// Classifies assignment RHS / pattern / deconstruct / out origins for the local TrainRoute window.
    /// </summary>
    internal static class RouteOriginClassifier
    {
        internal static bool TryClassifyRhsOrigin(
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

        internal static bool TryGetJoinedForkOrigin(
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
        internal static bool TryGetPatternOriginAssignment(
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

        internal static bool TryGetTrainRoutePatternLocal(
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
        internal static bool TryGetTupleDeconstructOriginAssignment(
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

        internal static bool TryFindDeconstructLocalIndex(
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

        internal static bool TryMatchDeconstructSlot(
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

        internal static bool TryGetOutArgumentLocal(
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

        internal static bool TryClassifyOutFactoryInvocation(
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
        internal static bool TryClassifyTrainRouteOriginExpression(
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

    }
}
