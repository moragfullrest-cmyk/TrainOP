using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Route;

namespace TrainOP.Generators
{
    /// <summary>
    /// Data-oriented IR root for the generator pipeline (stages 1a–7).
    /// Holders only — no <c>StringBuilder</c> / <c>AddSource</c>.
    /// </summary>
    internal sealed class GenerationModel
    {
        /// <summary>
        /// Creates an IR snapshot from already-discovered sites and assembled graph.
        /// </summary>
        public GenerationModel(
            ImmutableArray<StationHandlerBinding> stationSignatures,
            ImmutableArray<RouteChainAnchor> anchors,
            ImmutableArray<DelegateSignatureGroup> signatureGroups,
            ImmutableArray<BranchPlan> branchPlans,
            RouteGraph routeGraph,
            ImmutableArray<TerminalSet> terminals,
            ImmutableArray<SchemaDescriptor> schemaDescriptors,
            ImmutableArray<JoinedChain> joinedChains,
            ImmutableArray<Diagnostic> diagnostics)
        {
            StationSignatures = stationSignatures.IsDefault
                ? ImmutableArray<StationHandlerBinding>.Empty
                : stationSignatures;
            Anchors = anchors.IsDefault
                ? ImmutableArray<RouteChainAnchor>.Empty
                : anchors;
            SignatureGroups = signatureGroups.IsDefault
                ? ImmutableArray<DelegateSignatureGroup>.Empty
                : signatureGroups;
            BranchPlans = branchPlans.IsDefault
                ? ImmutableArray<BranchPlan>.Empty
                : branchPlans;
            RouteGraph = routeGraph ?? RouteGraph.Empty;
            Terminals = terminals.IsDefault
                ? ImmutableArray<TerminalSet>.Empty
                : terminals;
            SchemaDescriptors = schemaDescriptors.IsDefault
                ? ImmutableArray<SchemaDescriptor>.Empty
                : schemaDescriptors;
            JoinedChains = joinedChains.IsDefault
                ? ImmutableArray<JoinedChain>.Empty
                : joinedChains;
            Diagnostics = diagnostics.IsDefault
                ? ImmutableArray<Diagnostic>.Empty
                : diagnostics;
        }

        /// <summary>Stage 1a: resolved station handler bindings.</summary>
        public ImmutableArray<StationHandlerBinding> StationSignatures { get; }

        /// <summary>Stage 1b: resolved chain anchors.</summary>
        public ImmutableArray<RouteChainAnchor> Anchors { get; }

        /// <summary>Stage 2: signature groups (after GroupSignatures; chain attached separately).</summary>
        public ImmutableArray<DelegateSignatureGroup> SignatureGroups { get; }

        /// <summary>Stage 3: branch plans (canonical ∥ chain-aware + TOP007).</summary>
        public ImmutableArray<BranchPlan> BranchPlans { get; }

        /// <summary>Stage 4: assembled route graph.</summary>
        public RouteGraph RouteGraph { get; }

        /// <summary>Stage 5: terminal sets (<see cref="TerminalSet"/> + <see cref="TerminalSet.Origin"/>).</summary>
        public ImmutableArray<TerminalSet> Terminals { get; }

        /// <summary>Stage 6: schema export descriptors for <c>RouteSchemas.g.cs</c>.</summary>
        public ImmutableArray<SchemaDescriptor> SchemaDescriptors { get; }

        /// <summary>Stage 7: joined chain IR from <see cref="JoinChainsStage"/>.</summary>
        public ImmutableArray<JoinedChain> JoinedChains { get; }

        /// <summary>Pipeline diagnostics accumulated into the model.</summary>
        public ImmutableArray<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// Builds IR from discovery sites and an assembled <see cref="RouteGraph"/>.
        /// When <paramref name="compilation"/> is provided also fills JoinChains / join-origin terminals.
        /// When <paramref name="schemaDescriptors"/> is default, descriptors (and schema diagnostics
        /// when <paramref name="diagnostics"/> is default) are collected from
        /// <paramref name="compilation"/> via <see cref="SchemaDescriptorsStage"/>.
        /// </summary>
        public static GenerationModel Build(
            ImmutableArray<RouteSite> sites,
            RouteGraph graph,
            Compilation compilation = null,
            ImmutableArray<SchemaDescriptor> schemaDescriptors = default,
            ImmutableArray<Diagnostic> diagnostics = default)
        {
            var joinedChains = compilation != null
                ? JoinChainsStage.Collect(compilation)
                : ImmutableArray<JoinedChain>.Empty;

            var terminals = BuildJoinTerminals(joinedChains);

            if (schemaDescriptors.IsDefault || diagnostics.IsDefault)
            {
                if (compilation != null)
                {
                    var schemaCollect = SchemaDescriptorsStage.Collect(compilation);
                    if (schemaDescriptors.IsDefault)
                    {
                        schemaDescriptors = schemaCollect.Descriptors;
                    }

                    if (diagnostics.IsDefault)
                    {
                        diagnostics = schemaCollect.Diagnostics;
                    }
                }
                else
                {
                    if (schemaDescriptors.IsDefault)
                    {
                        schemaDescriptors = ImmutableArray<SchemaDescriptor>.Empty;
                    }

                    if (diagnostics.IsDefault)
                    {
                        diagnostics = ImmutableArray<Diagnostic>.Empty;
                    }
                }
            }

            if (sites.IsDefaultOrEmpty)
            {
                return new GenerationModel(
                    ImmutableArray<StationHandlerBinding>.Empty,
                    ImmutableArray<RouteChainAnchor>.Empty,
                    ImmutableArray<DelegateSignatureGroup>.Empty,
                    ImmutableArray<BranchPlan>.Empty,
                    graph,
                    terminals,
                    schemaDescriptors,
                    joinedChains,
                    diagnostics);
            }

            var signatures = ImmutableArray.CreateBuilder<StationHandlerBinding>();
            var anchors = ImmutableArray.CreateBuilder<RouteChainAnchor>();

            for (var i = 0; i < sites.Length; i++)
            {
                var site = sites[i];
                if (site == null)
                {
                    continue;
                }

                if (site.IsStation)
                {
                    if (site.HandlerBinding != null)
                    {
                        signatures.Add(site.HandlerBinding);
                    }

                    continue;
                }

                if (site.Kind == RouteSiteKind.Anchor)
                {
                    anchors.Add(site.ToAnchor());
                }
            }

            // Anchor seeds as TerminalSet(Origin.AnchorSeed) when present.
            if (anchors.Count > 0)
            {
                var terminalBuilder = terminals.IsDefaultOrEmpty
                    ? ImmutableArray.CreateBuilder<TerminalSet>()
                    : terminals.ToBuilder();

                for (var i = 0; i < anchors.Count; i++)
                {
                    var seed = TerminalSetAdapters.FromAnchorSeed(anchors[i].InitialWagons);
                    if (!seed.Wagons.IsDefaultOrEmpty)
                    {
                        terminalBuilder.Add(seed);
                    }
                }

                terminals = terminalBuilder.ToImmutable();
            }

            return new GenerationModel(
                signatures.ToImmutable(),
                anchors.ToImmutable(),
                ImmutableArray<DelegateSignatureGroup>.Empty,
                ImmutableArray<BranchPlan>.Empty,
                graph,
                terminals,
                schemaDescriptors,
                joinedChains,
                diagnostics);
        }

        /// <summary>
        /// Attaches stage-2/3 signature pipeline results without rebuilding discovery IR.
        /// </summary>
        public GenerationModel WithSignaturePipeline(
            ImmutableArray<DelegateSignatureGroup> signatureGroups,
            ImmutableArray<BranchPlan> branchPlans)
        {
            return new GenerationModel(
                StationSignatures,
                Anchors,
                signatureGroups,
                branchPlans,
                RouteGraph,
                Terminals,
                SchemaDescriptors,
                JoinedChains,
                Diagnostics);
        }

        private static ImmutableArray<TerminalSet> BuildJoinTerminals(
            ImmutableArray<JoinedChain> joinedChains)
        {
            if (joinedChains.IsDefaultOrEmpty)
            {
                return ImmutableArray<TerminalSet>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<TerminalSet>();
            for (var i = 0; i < joinedChains.Length; i++)
            {
                var joined = joinedChains[i];
                if (joined?.MergedTerminals == null)
                {
                    continue;
                }

                if (joined.Validation != null && joined.Validation.CanMerge)
                {
                    builder.Add(joined.MergedTerminals);
                }
            }

            return builder.ToImmutable();
        }
    }

    /// <summary>
    /// Stage 3 BranchPlan: merged station schema with canonical ∥ chain-aware emit intent.
    /// </summary>
    internal sealed class BranchPlan
    {
        public BranchPlan(MergedStationSchema schema)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        /// <summary>Merged schema consumed by Extensions emit.</summary>
        public MergedStationSchema Schema { get; }

        /// <summary>Whether emit should use chain-aware dispatch for <see cref="Schema"/>.</summary>
        public bool UsesChainDispatch => Schema.UsesChainDispatch;
    }
}
