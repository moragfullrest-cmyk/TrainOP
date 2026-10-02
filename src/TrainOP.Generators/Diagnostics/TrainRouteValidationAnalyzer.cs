using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Parts;
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
                TrainRouteDiagnostics.ParamsWagonNotLast,
                TrainRouteDiagnostics.SyncTravelOnAsyncChain,
                TrainRouteDiagnostics.NonConstantWagonDefault,
                TrainRouteDiagnostics.ServiceStationWriteback,
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
                ReportSyncTravelOnAsyncChain(modelContext, graph, tree, semanticModel);
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

        /// <summary>
        /// Reports TOP021 when <c>Travel</c> or <c>TravelLight</c> runs a chain the graph already resolved and that chain has an async station.
        /// A parameter, field, or method the graph did not open stays silent; runtime still rejects it.
        /// </summary>
        private static void ReportSyncTravelOnAsyncChain(
            SemanticModelAnalysisContext modelContext,
            RouteGraph graph,
            SyntaxTree tree,
            SemanticModel semanticModel)
        {
            foreach (var invocation in tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>())
            {
                if (!TryGetSyncTravelName(invocation, semanticModel, out var travelName, out var receiver))
                {
                    continue;
                }

                if (!ReceiverRunsKnownAsyncChain(receiver, graph, semanticModel))
                {
                    continue;
                }

                modelContext.ReportDiagnostic(Diagnostic.Create(
                    TrainRouteDiagnostics.SyncTravelOnAsyncChain,
                    invocation.GetLocation(),
                    travelName));
            }
        }

        private static bool TryGetSyncTravelName(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            out string travelName,
            out ExpressionSyntax receiver)
        {
            travelName = null;
            receiver = null;
            if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var name = memberAccess.Name.Identifier.ValueText;
            if (name != "Travel" && name != "TravelLight")
            {
                return false;
            }

            if (!IsSyncTravelOnRoute(invocation, memberAccess, semanticModel))
            {
                return false;
            }

            travelName = name;
            receiver = ReceiverExpressionSyntaxPeel.UnwrapTransparent(memberAccess.Expression);
            return receiver != null;
        }

        /// <summary>
        /// A bound <c>TrainRoute.Travel</c> / <c>TravelLight</c>, or the same names on a route receiver
        /// whose type is still an error because generated <c>Station</c> stubs are absent.
        /// </summary>
        private static bool IsSyncTravelOnRoute(
            InvocationExpressionSyntax invocation,
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel)
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
            {
                return StationSyntaxHelper.IsTrainRoute(method.ContainingType);
            }

            var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
            return StationSyntaxHelper.IsTrainRouteReceiver(
                memberAccess.Expression,
                typeInfo.Type ?? typeInfo.ConvertedType,
                semanticModel);
        }

        private static bool ReceiverRunsKnownAsyncChain(
            ExpressionSyntax receiver,
            RouteGraph graph,
            SemanticModel semanticModel)
        {
            if (receiver is InvocationExpressionSyntax invocation)
            {
                if (graph.TryGetChainForInvocation(invocation, out var stationChain)
                    && RouteHasAsyncStation(stationChain, graph))
                {
                    return true;
                }

                return semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                    && MethodHasAsyncChain(method, graph);
            }

            if (receiver is not IdentifierNameSyntax identifier)
            {
                return false;
            }

            if (semanticModel.GetSymbolInfo(identifier).Symbol is not ILocalSymbol local)
            {
                return false;
            }

            if (LocalHasAsyncChain(local, graph, semanticModel))
            {
                return true;
            }

            return LocalBindingMaterializer.TryMaterialize(identifier, semanticModel, out var binding)
                && binding.FactoryMethod != null
                && MethodHasAsyncChain(binding.FactoryMethod, graph);
        }

        private static bool LocalHasAsyncChain(
            ILocalSymbol local,
            RouteGraph graph,
            SemanticModel semanticModel)
        {
            foreach (var chain in graph.Chains)
            {
                if (chain.Origin is not LocalBinding binding
                    || binding.Identifier == null
                    || binding.Identifier.SyntaxTree != semanticModel.SyntaxTree)
                {
                    continue;
                }

                if (semanticModel.GetSymbolInfo(binding.Identifier).Symbol is not ILocalSymbol boundLocal
                    || !SymbolEqualityComparer.Default.Equals(boundLocal, local))
                {
                    continue;
                }

                if (RouteHasAsyncStation(chain, graph))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RouteHasAsyncStation(RouteChain chain, RouteGraph graph)
        {
            if (ChainHasAsyncStation(chain))
            {
                return true;
            }

            return chain.FactoryMethod != null && MethodHasAsyncChain(chain.FactoryMethod, graph);
        }

        private static bool MethodHasAsyncChain(IMethodSymbol method, RouteGraph graph)
        {
            if (method == null)
            {
                return false;
            }

            var target = method.OriginalDefinition ?? method;
            foreach (var chain in graph.Chains)
            {
                var containing = chain.ContainingMethod;
                if (containing == null)
                {
                    continue;
                }

                var candidate = containing.OriginalDefinition ?? containing;
                if (!SymbolEqualityComparer.Default.Equals(candidate, target))
                {
                    continue;
                }

                if (ChainHasAsyncStation(chain))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ChainHasAsyncStation(RouteChain chain)
        {
            if (chain == null)
            {
                return false;
            }

            for (var i = 0; i < chain.Stations.Length; i++)
            {
                if (chain.Stations[i].Handler != null && chain.Stations[i].Handler.IsAsync)
                {
                    return true;
                }
            }

            return false;
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
