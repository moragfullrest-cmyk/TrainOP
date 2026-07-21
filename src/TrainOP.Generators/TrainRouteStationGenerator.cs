using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
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
                RouteSchemasFile.AddSource(productionContext, compilation);
                var graph = RouteGraphAssembler.Build(sites, compilation);
                var groups = new Dictionary<string, DelegateSignatureGroup>(StringComparer.Ordinal);
                var processedInvocationKeys = new HashSet<string>(StringComparer.Ordinal);

                foreach (var site in graph.StationSites
                    .OrderBy(site => site.IdentityLocation.SourceSpan.Start))
                {
                    AddDiscoveredCall(
                        groups,
                        processedInvocationKeys,
                        graph.ChainIndex,
                        productionContext,
                        site.HandlerBinding,
                        site.HandlerLocation,
                        site.Invocation);
                }

                foreach (var chainBinding in graph.ChainIndex.Values
                    .SelectMany(x => x)
                    .OrderBy(binding => binding.InvocationLocation.SourceSpan.Start))
                {
                    if (chainBinding.Schema == null || chainBinding.Invocation == null)
                    {
                        continue;
                    }

                    AddDiscoveredCall(
                        groups,
                        processedInvocationKeys,
                        graph.ChainIndex,
                        productionContext,
                        chainBinding.Schema,
                        chainBinding.InvocationLocation,
                        chainBinding.Invocation);
                }

                if (groups.Count == 0)
                {
                    return;
                }

                var mergedSchemas = groups.Values
                    .Select(group => group.ToMerged(productionContext))
                    .OrderBy(x => x.DelegateTypeId, StringComparer.Ordinal)
                    .ToImmutableArray();

                TrainRouteExtensionsFile.AddSource(productionContext, mergedSchemas);
            });
        }

        private static void AddDiscoveredCall(
            Dictionary<string, DelegateSignatureGroup> groups,
            HashSet<string> processedInvocationKeys,
            IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> chainIndex,
            SourceProductionContext productionContext,
            StationHandlerBinding handlerBinding,
            Location location,
            InvocationExpressionSyntax invocation)
        {
            if (handlerBinding == null || invocation == null)
            {
                return;
            }

            var invocationLocation = invocation.GetLocation();
            var invocationKey = ChainSiteBindingLookup.BuildLocationKey(invocationLocation);
            if (invocationKey.Length == 0 || !processedInvocationKeys.Add(invocationKey))
            {
                return;
            }

            var typeSignature = DelegateTypeSignature.From(handlerBinding);
            var groupingKey = handlerBinding.BuildGroupingKey(typeSignature.TypeId);
            if (!groups.TryGetValue(groupingKey, out var group))
            {
                group = new DelegateSignatureGroup(typeSignature);
                groups[groupingKey] = group;
            }

            if (ChainSiteBindingLookup.TryResolveAll(chainIndex, invocationLocation, out var chainBindings)
                && chainBindings.Length > 0)
            {
                for (var i = 0; i < chainBindings.Length; i++)
                {
                    group.Add(handlerBinding, location, chainBindings[i], productionContext);
                }
            }
            else
            {
                group.Add(handlerBinding, location, null, productionContext);
            }
        }
    }
}
