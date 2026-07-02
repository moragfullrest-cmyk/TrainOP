using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TrainOP.Generators
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ChainValidationAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                TrainRouteDiagnostics.MissingWagon,
                TrainRouteDiagnostics.WagonTypeConflict,
                TrainRouteDiagnostics.WagonRemovedButRequired,
                TrainRouteDiagnostics.CargoManifestReplacement,
                TrainRouteDiagnostics.TupleReturnOrder,
                TrainRouteDiagnostics.OrphanDataHandler,
                TrainRouteDiagnostics.UnusedSeedWagon);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(AnalyzeCompilation);
        }

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
                var chains = ChainDetector.DetectChains(tree, semanticModel);
                foreach (var chain in chains)
                {
                    foreach (var diagnostic in ChainGraphValidator.Validate(chain))
                    {
                        modelContext.ReportDiagnostic(diagnostic);
                    }
                }

                var chainedInvocations = ChainDetector.CollectChainedStationInvocations(tree, semanticModel);
                var chainedSet = new System.Collections.Generic.HashSet<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>(
                    chainedInvocations);

                foreach (var orphan in ChainDetector.DetectOrphanStationInvocations(
                    tree,
                    semanticModel,
                    chainedSet))
                {
                    modelContext.ReportDiagnostic(Diagnostic.Create(
                        TrainRouteDiagnostics.OrphanDataHandler,
                        orphan.ArgumentList.Arguments[1].GetLocation()));
                }
            });
        }
    }
}
