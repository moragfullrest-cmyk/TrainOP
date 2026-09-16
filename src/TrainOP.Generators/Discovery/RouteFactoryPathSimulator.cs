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
            if (factoryMethod == null || !StationSyntaxHelper.IsTrainRoute(factoryMethod.ReturnType))
            {
                return ImmutableArray<FactoryPathSimulation>.Empty;
            }

            var paths = ImmutableArray.CreateBuilder<FactoryPathSimulation>();
            foreach (var reference in factoryMethod.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not MethodDeclarationSyntax methodDeclaration)
                {
                    continue;
                }

                if (!compilation.ContainsSyntaxTree(methodDeclaration.SyntaxTree))
                {
                    continue;
                }

                var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
                foreach (var expression in CollectReturnPathExpressions(methodDeclaration))
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

        private static IEnumerable<ExpressionSyntax> CollectReturnPathExpressions(MethodDeclarationSyntax methodDeclaration)
        {
            if (methodDeclaration.ExpressionBody?.Expression != null)
            {
                yield return methodDeclaration.ExpressionBody.Expression;
                yield break;
            }

            if (methodDeclaration.Body == null)
            {
                yield break;
            }

            foreach (var node in methodDeclaration.Body.DescendantNodes())
            {
                if (node is ReturnStatementSyntax returnStatement
                    && returnStatement.Expression != null)
                {
                    yield return returnStatement.Expression;
                }
            }
        }

        private static IEnumerable<FactoryPathSimulation> ExpandAndSimulateReturnPaths(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            Compilation compilation)
        {
            expression = ReceiverExpressionSyntaxPeel.UnwrapTransparent(expression);
            if (expression == null)
            {
                yield break;
            }

            if (expression is ConditionalExpressionSyntax conditional)
            {
                foreach (var path in ExpandAndSimulateReturnPaths(conditional.WhenTrue, semanticModel, compilation))
                {
                    yield return path;
                }

                foreach (var path in ExpandAndSimulateReturnPaths(conditional.WhenFalse, semanticModel, compilation))
                {
                    yield return path;
                }

                yield break;
            }

            if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                foreach (var path in ExpandAndSimulateReturnPaths(binary.Left, semanticModel, compilation))
                {
                    yield return path;
                }

                foreach (var path in ExpandAndSimulateReturnPaths(binary.Right, semanticModel, compilation))
                {
                    yield return path;
                }

                yield break;
            }

            if (expression is SwitchExpressionSyntax switchExpression)
            {
                foreach (var arm in switchExpression.Arms)
                {
                    foreach (var path in ExpandAndSimulateReturnPaths(arm.Expression, semanticModel, compilation))
                    {
                        yield return path;
                    }
                }

                yield break;
            }

            if (JoinChainsStage.TrySimulateFactoryForkJoin(
                expression,
                semanticModel,
                out var forkJoinPaths))
            {
                foreach (var path in forkJoinPaths)
                {
                    yield return path;
                }

                yield break;
            }

            yield return SimulateReturnExpression(
                expression,
                semanticModel,
                compilation,
                expression.GetLocation());
        }

        private static FactoryPathSimulation SimulateReturnExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            Compilation compilation,
            Location location)
        {
            if (BuildChainsStage.EndingAt(expression, semanticModel, out var chain))
            {
                var seed = TerminalSetAdapters.FromAnchorSeed(chain.Anchor.InitialWagons);
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

                var seed = TerminalSetAdapters.FromAnchorSeed(extensionChain.Anchor.InitialWagons);
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
                || !StationSyntaxHelper.IsTrainRoute(factoryMethod.ReturnType))
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
