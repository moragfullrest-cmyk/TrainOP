using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Route;
namespace TrainOP.Generators
{
    /// <summary>
    /// Roslyn analyzer that reports route-chain validation diagnostics at compile time.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ChainValidationAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// Diagnostics produced by route-chain simulation and orphan handler detection.
        /// </summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [
                TrainRouteDiagnostics.MissingWagon,
                TrainRouteDiagnostics.WagonTypeConflict,
                TrainRouteDiagnostics.WagonRemovedButRequired,
                TrainRouteDiagnostics.CargoManifestReplacement,
                TrainRouteDiagnostics.DefaultItemNTupleReturn,
                TrainRouteDiagnostics.RuntimeSignalReturn,
                TrainRouteDiagnostics.OrphanDataHandler,
                TrainRouteDiagnostics.ExternalFactorySchemaMissing,
                TrainRouteDiagnostics.FactoryReturnPathsDiverge,
                TrainRouteDiagnostics.FactoryReturnPathUnknown,
                TrainRouteDiagnostics.RouteBranchJoinFailed,
                TrainRouteDiagnostics.UnsupportedStationHandler,
                TrainRouteDiagnostics.MultipleTrainRouteNewSameLine,
            ];

        /// <summary>
        /// Registers semantic-model analysis for route chains in each compilation.
        /// </summary>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(AnalyzeCompilation);
        }

        /// <summary>
        /// Analyzes each syntax tree for route chains, wagon-flow issues, and orphan handlers.
        /// </summary>
        private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
        {
            var graph = RouteGraphAssembler.Build(
                RouteSiteDiscoverer.CollectAll(context.Compilation),
                context.Compilation);

            context.RegisterSemanticModelAction(modelContext =>
            {
                if (modelContext.SemanticModel.SyntaxTree.FilePath.EndsWith(
                    ".g.cs",
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var tree = modelContext.SemanticModel.SyntaxTree;
                var semanticModel = modelContext.SemanticModel;
                var compilation = semanticModel.Compilation;

                ReportMultipleTrainRouteCreationsOnSameLine(modelContext, tree, semanticModel);
                ReportChainValidationDiagnostics(modelContext, graph, tree, compilation);
                ReportFactoryValidationDiagnostics(modelContext, compilation);
                ReportBranchJoinDiagnostics(modelContext, graph, tree, semanticModel);
                ReportOrphanHandlers(modelContext, graph, tree, semanticModel);
                ReportUnsupportedHandlers(modelContext, tree, semanticModel);
            });
        }

        private static void ReportMultipleTrainRouteCreationsOnSameLine(
            SemanticModelAnalysisContext modelContext,
            SyntaxTree tree,
            SemanticModel semanticModel)
        {
            foreach (var methodDeclaration in tree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>())
            {
                var trainRouteCreations = methodDeclaration
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Where(oc => StationSyntaxHelper.IsTrainRouteCreation(oc, semanticModel))
                    .ToList();

                if (trainRouteCreations.Count <= 1)
                {
                    continue;
                }

                var byLine = trainRouteCreations
                    .GroupBy(oc =>
                        oc.GetLocation().GetLineSpan().StartLinePosition.Line);

                foreach (var lineGroup in byLine)
                {
                    if (lineGroup.Count() <= 1)
                    {
                        continue;
                    }

                    var first = lineGroup.OrderBy(oc => oc.GetLocation().SourceSpan.Start).First();
                    modelContext.ReportDiagnostic(Diagnostic.Create(
                        TrainRouteDiagnostics.MultipleTrainRouteNewSameLine,
                        first.GetLocation(),
                        methodDeclaration.Identifier.ValueText,
                        lineGroup.Key + 1));
                }
            }
        }

        private static void ReportChainValidationDiagnostics(
            SemanticModelAnalysisContext modelContext,
            RouteGraph graph,
            SyntaxTree tree,
            Compilation compilation)
        {
            foreach (var chain in graph.GetChainsInTree(tree))
            {
                if (chain.Anchor.FactoryMethod != null)
                {
                    if (!RouteFactoryResolver.TryResolve(
                        chain.Anchor.FactoryMethod,
                        compilation,
                        chain.Anchor.Location,
                        out _,
                        out var factoryDiagnostics))
                    {
                        foreach (var diagnostic in factoryDiagnostics)
                        {
                            modelContext.ReportDiagnostic(diagnostic);
                        }
                    }
                }

                foreach (var diagnostic in ChainGraphSimulator
                    .Simulate(chain, chain.Anchor.InitialWagons)
                    .Diagnostics)
                {
                    modelContext.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static void ReportBranchJoinDiagnostics(
            SemanticModelAnalysisContext modelContext,
            RouteGraph graph,
            SyntaxTree tree,
            SemanticModel semanticModel)
        {
            var joinSets = BranchRouteJoinSetFinder.Find(tree, semanticModel);
            foreach (var joinSet in joinSets)
            {
                var validation = BranchRouteJoinValidator.Validate(joinSet, semanticModel);
                foreach (var diagnostic in validation.Diagnostics)
                {
                    modelContext.ReportDiagnostic(diagnostic);
                }

                if (!validation.CanMerge || joinSet.DownstreamStation == null)
                {
                    continue;
                }

                if (!graph.TryGetChainForInvocation(joinSet.DownstreamStation, out var downstreamChain))
                {
                    continue;
                }

                foreach (var diagnostic in ChainGraphSimulator
                    .Simulate(downstreamChain, validation.MergedTerminalWagons)
                    .Diagnostics)
                {
                    modelContext.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static void ReportOrphanHandlers(
            SemanticModelAnalysisContext modelContext,
            RouteGraph graph,
            SyntaxTree tree,
            SemanticModel semanticModel)
        {
            var joinDownstream = new HashSet<InvocationExpressionSyntax>();
            foreach (var joinSet in BranchRouteJoinSetFinder.Find(tree, semanticModel))
            {
                if (joinSet.DownstreamStation != null)
                {
                    joinDownstream.Add(joinSet.DownstreamStation);

                    if (graph.TryGetChainForInvocation(joinSet.DownstreamStation, out var downstreamChain))
                    {
                        for (var i = 0; i < downstreamChain.Stations.Length; i++)
                        {
                            joinDownstream.Add(downstreamChain.Stations[i].Invocation);
                        }
                    }
                }
            }

            foreach (var invocation in tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                if (!StationSyntaxHelper.IsCandidateRouteHandlerInvocation(invocation))
                {
                    continue;
                }

                if (graph.IsChainedInvocation(invocation.GetLocation())
                    || joinDownstream.Contains(invocation))
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
                    modelContext.ReportDiagnostic(Diagnostic.Create(
                        TrainRouteDiagnostics.OrphanDataHandler,
                        invocation.ArgumentList.Arguments[1].GetLocation()));
                }
            }
        }

        private static void ReportUnsupportedHandlers(
            SemanticModelAnalysisContext modelContext,
            SyntaxTree tree,
            SemanticModel semanticModel)
        {
            foreach (var invocation in tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                if (!StationSyntaxHelper.TryGetUnsupportedStationHandler(
                        invocation,
                        semanticModel,
                        out var handlerLocation)
                    || handlerLocation == null)
                {
                    continue;
                }

                modelContext.ReportDiagnostic(Diagnostic.Create(
                    TrainRouteDiagnostics.UnsupportedStationHandler,
                    handlerLocation));
            }
        }

        private static void ReportFactoryValidationDiagnostics(
            SemanticModelAnalysisContext modelContext,
            Compilation compilation)
        {
            var tree = modelContext.SemanticModel.SyntaxTree;
            if (tree.FilePath.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var processed = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is not MethodDeclarationSyntax methodDeclaration)
                {
                    continue;
                }

                var methodSymbol = modelContext.SemanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
                if (methodSymbol == null
                    || !FactoryAccessibilityHelper.IsExportedFactoryContract(methodSymbol)
                    || !StationSyntaxHelper.IsTrainRoute(methodSymbol.ReturnType)
                    || !processed.Add(methodSymbol))
                {
                    continue;
                }

                foreach (var diagnostic in RouteFactoryPathValidator.Validate(methodSymbol, compilation).Diagnostics)
                {
                    modelContext.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}
