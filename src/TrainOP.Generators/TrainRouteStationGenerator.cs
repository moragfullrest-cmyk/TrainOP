using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;

namespace TrainOP.Generators
{
    /// <summary>
    /// Source generator that emits typed Station and ServiceStation extension methods for data-oriented handlers.
    /// </summary>
    [Generator]
    public sealed class TrainRouteStationGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Creates a generator instance (required for test hosts and MEF discovery).
        /// </summary>
        public TrainRouteStationGenerator()
        {
        }

        /// <summary>
        /// Registers syntax-driven discovery of station handlers and emits grouped extension source code.
        /// </summary>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var stationParts = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => RoutePartDiscoverer.IsCandidateStationSite(node),
                static (generatorContext, _) => RoutePartDiscoverer.TryDiscoverStation(generatorContext)).Collect();

            var anchorParts = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => RoutePartDiscoverer.IsCandidateAnchorSite(node),
                static (generatorContext, _) => RoutePartDiscoverer.TryDiscoverAnchor(generatorContext)).Collect();

            var allParts = stationParts
                .Combine(anchorParts)
                .Select(static (pair, _) => RoutePartDiscoverer.MergeParts(pair.Left, pair.Right));

            var combined = context.CompilationProvider.Combine(allParts);

            context.RegisterSourceOutput(combined, (productionContext, source) =>
            {
                var compilation = source.Left;
                var parts = source.Right;

                // Build IR first; AddSource only in EmitAll.
                // TOP012/013 stay on SchemaDescriptorsStage; the analyzer reports them.
                var schemaCollect = SchemaDescriptorsStage.Collect(compilation);
                var graph = BuildChainsStage.Build(parts, compilation);
                var generationModel = GenerationModel.Build(
                    parts,
                    graph,
                    compilation,
                    schemaCollect.Descriptors,
                    ImmutableArray<Diagnostic>.Empty);

                // Signature groups need the assembled graph; chain bindings attach before branch plans.
                var groups = SignatureGroupingStage.Group(generationModel.RouteGraph);
                AttachChainContextStage.Attach(groups.Values, generationModel.RouteGraph.ChainIndex);
                var branchPlans = BranchPlanStage.Build(groups.Values, productionContext);
                generationModel = generationModel.WithSignaturePipeline(
                    groups.Values.ToImmutableArray(),
                    branchPlans);

                GenerationEmit.EmitAll(productionContext, generationModel);
            });
        }
    }
}
