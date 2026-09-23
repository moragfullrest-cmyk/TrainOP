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
            var stationSites = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => RouteSiteDiscoverer.IsCandidateStationSite(node),
                static (generatorContext, _) => RouteSiteDiscoverer.TryDiscoverStation(generatorContext)).Collect();

            var anchorSites = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => RouteSiteDiscoverer.IsCandidateAnchorSite(node),
                static (generatorContext, _) => RouteSiteDiscoverer.TryDiscoverAnchor(generatorContext)).Collect();

            var allSites = stationSites
                .Combine(anchorSites)
                .Select(static (pair, _) => RouteSiteDiscoverer.MergeSites(pair.Left, pair.Right));

            var combined = context.CompilationProvider.Combine(allSites);

            context.RegisterSourceOutput(combined, (productionContext, source) =>
            {
                var compilation = source.Left;
                var sites = source.Right;

                // Build IR first; AddSource only in EmitAll.
                var schemaCollect = SchemaDescriptorsStage.Collect(compilation);
                var graph = BuildChainsStage.Build(sites, compilation);
                var generationModel = GenerationModel.Build(
                    sites,
                    graph,
                    compilation,
                    schemaCollect.Descriptors,
                    schemaCollect.Diagnostics);

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
