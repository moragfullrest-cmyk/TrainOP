using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
                var chains = ChainDetector.DetectChains(tree, semanticModel);
                foreach (var chain in chains)
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

                    foreach (var diagnostic in ChainGraphValidator.Validate(chain))
                    {
                        modelContext.ReportDiagnostic(diagnostic);
                    }
                }

                ReportFactoryValidationDiagnostics(modelContext, compilation);

                var joinDownstream = new HashSet<InvocationExpressionSyntax>();
                var joinSets = BranchRouteJoinSetFinder.Find(tree, semanticModel);
                foreach (var joinSet in joinSets)
                {
                    var validation = BranchRouteJoinValidator.Validate(joinSet);
                    foreach (var diagnostic in validation.Diagnostics)
                    {
                        modelContext.ReportDiagnostic(diagnostic);
                    }

                    if (joinSet.DownstreamStation != null)
                    {
                        // Always suppress TOP005 on fork-downstream (TOP008 covers join failures).
                        joinDownstream.Add(joinSet.DownstreamStation);
                    }

                    if (!validation.CanMerge || joinSet.DownstreamStation == null)
                    {
                        continue;
                    }

                    if (ChainDetector.TryBuildChainFromStationInvocation(
                        joinSet.DownstreamStation,
                        semanticModel,
                        out var downstreamChain))
                    {
                        foreach (var link in downstreamChain.Stations)
                        {
                            joinDownstream.Add(link.Invocation);
                        }

                        foreach (var diagnostic in ChainGraphSimulator
                            .Simulate(downstreamChain, validation.MergedTerminalWagons)
                            .Diagnostics)
                        {
                            modelContext.ReportDiagnostic(diagnostic);
                        }
                    }
                }

                var chainedInvocations = ChainDetector.CollectChainedStationInvocations(tree, semanticModel);
                var chainedSet = new HashSet<InvocationExpressionSyntax>(chainedInvocations);
                foreach (var invocation in joinDownstream)
                {
                    chainedSet.Add(invocation);
                }

                foreach (var orphan in ChainDetector.DetectOrphanStationInvocations(
                    tree,
                    semanticModel,
                    chainedSet))
                {
                    modelContext.ReportDiagnostic(Diagnostic.Create(
                        TrainRouteDiagnostics.OrphanDataHandler,
                        orphan.ArgumentList.Arguments[1].GetLocation()));
                }

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
            });
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
