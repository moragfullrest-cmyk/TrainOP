using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Route;
using TrainOP.Generators.Wagons;
namespace TrainOP.Generators
{
    /// <summary>
    /// Collects and simulates all return paths of a TrainRoute factory method.
    /// </summary>
    internal static class RouteFactoryPathSimulator
    {
        /// <summary>
        /// Simulates all statically discoverable return paths for a factory method.
        /// </summary>
        public static ImmutableArray<FactoryPathSimulation> SimulateAllReturnPaths(
            IMethodSymbol factoryMethod,
            Compilation compilation)
        {
            if (factoryMethod == null || !StationSyntaxHelper.IsTrainRouteFactoryReturnType(factoryMethod.ReturnType))
            {
                return ImmutableArray<FactoryPathSimulation>.Empty;
            }

            var paths = ImmutableArray.CreateBuilder<FactoryPathSimulation>();
            foreach (var reference in factoryMethod.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                if (syntax is not MethodDeclarationSyntax and not LocalFunctionStatementSyntax)
                {
                    continue;
                }

                if (!compilation.ContainsSyntaxTree(syntax.SyntaxTree))
                {
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var expression in CollectReturnPathExpressions(syntax))
                {
                    foreach (var simulation in ExpandAndSimulateReturnPaths(
                        expression,
                        semanticModel,
                        compilation))
                    {
                        paths.Add(simulation);
                    }
                }
            }

            return paths.ToImmutable();
        }

        /// <summary>
        /// Simulates all statically discoverable assignments to an <c>out TrainRoute</c> factory parameter.
        /// </summary>
        public static ImmutableArray<FactoryPathSimulation> SimulateAllOutParameterPaths(
            IMethodSymbol factoryMethod,
            IParameterSymbol outParameter,
            Compilation compilation)
        {
            if (factoryMethod == null
                || outParameter == null
                || outParameter.RefKind != RefKind.Out
                || !StationSyntaxHelper.IsTrainRoute(outParameter.Type))
            {
                return ImmutableArray<FactoryPathSimulation>.Empty;
            }

            var paths = ImmutableArray.CreateBuilder<FactoryPathSimulation>();
            foreach (var reference in factoryMethod.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                if (syntax is not MethodDeclarationSyntax and not LocalFunctionStatementSyntax)
                {
                    continue;
                }

                if (!compilation.ContainsSyntaxTree(syntax.SyntaxTree))
                {
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                foreach (var expression in CollectOutParameterAssignments(syntax, outParameter, semanticModel))
                {
                    foreach (var simulation in ExpandAndSimulateReturnPaths(
                        expression,
                        semanticModel,
                        compilation))
                    {
                        paths.Add(simulation);
                    }
                }
            }

            return paths.ToImmutable();
        }

        /// <summary>
        /// Collects RHS expressions assigned to an <c>out TrainRoute</c> parameter.
        /// </summary>
        internal static IEnumerable<ExpressionSyntax> CollectOutParameterAssignments(
            SyntaxNode declaration,
            IParameterSymbol outParameter,
            SemanticModel semanticModel)
        {
            if (!TryGetFactoryBody(declaration, out var expressionBody, out var body))
            {
                yield break;
            }

            if (expressionBody?.Expression != null)
            {
                if (TryGetOutParameterAssignmentRhs(
                    expressionBody.Expression,
                    outParameter,
                    semanticModel,
                    out var expressionRhs))
                {
                    yield return expressionRhs;
                }

                yield break;
            }

            if (body == null)
            {
                yield break;
            }

            foreach (var node in body.DescendantNodes())
            {
                if (node is not AssignmentExpressionSyntax assignment
                    || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    continue;
                }

                if (TryGetOutParameterAssignmentRhs(
                    assignment,
                    outParameter,
                    semanticModel,
                    out var assignmentRhs))
                {
                    yield return assignmentRhs;
                }
            }
        }

        private static bool TryGetOutParameterAssignmentRhs(
            ExpressionSyntax expression,
            IParameterSymbol outParameter,
            SemanticModel semanticModel,
            out ExpressionSyntax rhs)
        {
            rhs = null;
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression is not AssignmentExpressionSyntax assignment
                || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return false;
            }

            var left = ReceiverExpressionSyntaxPeel.UnwrapTransparent(assignment.Left);
            if (left is not IdentifierNameSyntax identifier)
            {
                return false;
            }

            if (semanticModel.GetSymbolInfo(identifier).Symbol is not IParameterSymbol parameter
                || !SymbolEqualityComparer.Default.Equals(parameter, outParameter))
            {
                return false;
            }

            rhs = assignment.Right;
            return rhs != null;
        }

        /// <summary>
        /// Collects return expressions from a method or local-function factory body.
        /// </summary>
        internal static IEnumerable<ExpressionSyntax> CollectReturnPathExpressions(SyntaxNode declaration)
        {
            if (!TryGetFactoryBody(declaration, out var expressionBody, out var body))
            {
                yield break;
            }

            if (expressionBody?.Expression != null)
            {
                yield return expressionBody.Expression;
                yield break;
            }

            if (body == null)
            {
                yield break;
            }

            foreach (var node in body.DescendantNodes())
            {
                if (node is ReturnStatementSyntax returnStatement
                    && returnStatement.Expression != null)
                {
                    yield return returnStatement.Expression;
                }
            }
        }

        private static bool TryGetFactoryBody(
            SyntaxNode declaration,
            out ArrowExpressionClauseSyntax expressionBody,
            out BlockSyntax body)
        {
            switch (declaration)
            {
                case MethodDeclarationSyntax method:
                    expressionBody = method.ExpressionBody;
                    body = method.Body;
                    return true;
                case LocalFunctionStatementSyntax localFunction:
                    expressionBody = localFunction.ExpressionBody;
                    body = localFunction.Body;
                    return true;
                default:
                    expressionBody = null;
                    body = null;
                    return false;
            }
        }

        private static IEnumerable<FactoryPathSimulation> ExpandAndSimulateReturnPaths(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            Compilation compilation)
        {
            foreach (var leaf in ReturnPathExpressionExpander.Expand(expression))
            {
                if (JoinChainsStage.TrySimulateFactoryForkJoin(
                    leaf,
                    semanticModel,
                    out var forkJoinPaths))
                {
                    foreach (var path in forkJoinPaths)
                    {
                        yield return path;
                    }

                    continue;
                }

                yield return SimulateReturnExpression(
                    leaf,
                    semanticModel,
                    compilation,
                    leaf.GetLocation());
            }
        }

        private static FactoryPathSimulation SimulateReturnExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            Compilation compilation,
            Location location)
        {
            if (BuildChainsStage.EndingAt(expression, semanticModel, out var chain))
            {
                var seed = TerminalSetAdapters.FromAnchorSeed(chain.InitialWagons);
                var simulation = ChainGraphSimulator.Simulate(
                    chain,
                    TerminalSetAdapters.ToWagons(seed));
                var terminals = TerminalSetAdapters.FromSimulation(
                    simulation,
                    TerminalSet.Origin.FactoryPath);
                return new FactoryPathSimulation(
                    TerminalSetAdapters.ToWagons(terminals),
                    terminals.HasUnknownReturn,
                    location);
            }

            if (BuildChainsStage.FactoryExtension(
                expression,
                semanticModel,
                compilation,
                out var extensionChain,
                out var resolverDiagnostics))
            {
                if (!resolverDiagnostics.IsDefaultOrEmpty)
                {
                    return new FactoryPathSimulation(
                        ImmutableArray<WagonBinding>.Empty,
                        hasUnknownReturn: true,
                        location);
                }

                var seed = TerminalSetAdapters.FromAnchorSeed(extensionChain.InitialWagons);
                var simulation = ChainGraphSimulator.Simulate(
                    extensionChain,
                    TerminalSetAdapters.ToWagons(seed));
                var terminals = TerminalSetAdapters.FromSimulation(
                    simulation,
                    TerminalSet.Origin.FactoryPath);
                return new FactoryPathSimulation(
                    TerminalSetAdapters.ToWagons(terminals),
                    terminals.HasUnknownReturn,
                    location);
            }

            if (TrySimulateBareFactoryInvocation(expression, semanticModel, compilation, location, out var bareSimulation))
            {
                return bareSimulation;
            }

            return new FactoryPathSimulation(
                ImmutableArray<WagonBinding>.Empty,
                hasUnknownReturn: true,
                location);
        }

        private static bool TrySimulateBareFactoryInvocation(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            Compilation compilation,
            Location location,
            out FactoryPathSimulation simulation)
        {
            simulation = null;
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol factoryMethod
                || !StationSyntaxHelper.IsTrainRouteFactoryReturnType(factoryMethod.ReturnType))
            {
                return false;
            }

            if (!RouteFactoryResolver.TryResolveInline(
                factoryMethod,
                compilation,
                location,
                out var terminalWagons,
                out var diagnostics))
            {
                simulation = new FactoryPathSimulation(
                    ImmutableArray<WagonBinding>.Empty,
                    hasUnknownReturn: true,
                    location);
                return true;
            }

            if (!diagnostics.IsDefaultOrEmpty)
            {
                simulation = new FactoryPathSimulation(
                    ImmutableArray<WagonBinding>.Empty,
                    hasUnknownReturn: true,
                    location);
                return true;
            }

            var terminals = new TerminalSet(
                terminalWagons,
                TerminalSet.Origin.FactoryPath,
                hasUnknownReturn: false);
            simulation = new FactoryPathSimulation(
                TerminalSetAdapters.ToWagons(terminals),
                terminals.HasUnknownReturn,
                location);
            return true;
        }
    }
}
