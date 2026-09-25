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
    /// Roslyn analyzer that reports TrainRoute validation diagnostics at compile time.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TrainRouteValidationAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// Diagnostics produced by route graph validation, simulation, and orphan handler detection.
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
                TrainRouteDiagnostics.ServiceStationAddsWagon,
                TrainRouteDiagnostics.ServiceStationRemovesWagon,
                TrainRouteDiagnostics.ServiceStationCargoManifestReplacement,
                TrainRouteDiagnostics.OutWagonConflictsWithReturn,
                TrainRouteDiagnostics.RefReadonlyWagonInReturn,
            ];

        /// <summary>
        /// Registers semantic-model analysis for route graphs in each compilation.
        /// </summary>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(AnalyzeCompilation);
        }

        /// <summary>
        /// Builds the shared route graph, reports factory-path diagnostics from schema collect, then per-tree chain checks.
        /// </summary>
        private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
        {
            var compilation = context.Compilation;
            var schemaDiagnostics = SchemaDescriptorsStage.Collect(compilation).Diagnostics;
            var graph = BuildChainsStage.Build(
                RoutePartDiscoverer.CollectAll(compilation),
                compilation);

            context.RegisterCompilationEndAction(endContext =>
            {
                foreach (var diagnostic in schemaDiagnostics)
                {
                    endContext.ReportDiagnostic(diagnostic);
                }
            });

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
                var joinSets = JoinChainsStage.Find(tree, semanticModel);

                ReportMultipleTrainRouteCreationsOnSameLine(modelContext, tree, semanticModel);
                ReportChainValidationDiagnostics(modelContext, graph, tree, semanticModel.Compilation);
                ReportBranchJoinDiagnostics(modelContext, graph, joinSets, semanticModel);
                ReportOrphanHandlers(modelContext, graph, tree, semanticModel, joinSets);
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
                if (chain.FactoryMethod != null)
                {
                    if (!RouteFactoryResolver.TryResolve(
                        chain.FactoryMethod,
                        compilation,
                        chain.AnchorLocation,
                        out _,
                        out var factoryDiagnostics))
                    {
                        foreach (var diagnostic in factoryDiagnostics)
                        {
                            modelContext.ReportDiagnostic(diagnostic);
                        }
                    }
                }

                var seed = TerminalSetAdapters.FromAnchorSeed(chain.InitialWagons);
                foreach (var diagnostic in ChainGraphSimulator
                    .Simulate(chain, seed.Wagons)
                    .Diagnostics)
                {
                    modelContext.ReportDiagnostic(diagnostic);
                }
            }
        }

        private static void ReportBranchJoinDiagnostics(
            SemanticModelAnalysisContext modelContext,
            RouteGraph graph,
            ImmutableArray<BranchRouteJoinSet> joinSets,
            SemanticModel semanticModel)
        {
            foreach (var joinSet in joinSets)
            {
                var joined = JoinChainsStage.Join(joinSet, semanticModel);
                foreach (var diagnostic in joined.Validation.Diagnostics)
                {
                    modelContext.ReportDiagnostic(diagnostic);
                }

                if (!joined.Validation.CanMerge || joinSet.DownstreamStation == null)
                {
                    continue;
                }

                if (!graph.TryGetChainForInvocation(joinSet.DownstreamStation, out var downstreamChain))
                {
                    continue;
                }

                foreach (var diagnostic in ChainGraphSimulator
                    .Simulate(downstreamChain, joined.MergedTerminals.Wagons)
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
            SemanticModel semanticModel,
            ImmutableArray<BranchRouteJoinSet> joinSets)
        {
            var joinDownstream = new HashSet<InvocationExpressionSyntax>();
            foreach (var joinSet in joinSets)
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

    }
}
