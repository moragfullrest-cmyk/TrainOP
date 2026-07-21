using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    public sealed class RouteGraphAssemblerTests
    {
        [Fact]
        public void RouteGraphAssembler_Build_LinearFluentChain_AssignsSequentialIndices()
        {
            const string source = @"
using TrainOP;

public static class LinearRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Validate"", (string paymentId, decimal amount) =>
            amount > 0
                ? RailwaySignals.Green(new { paymentId, amount })
                : RailwaySignals.Red(""INVALID_TOTAL"", ""amount must be positive""));
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.Equal(3, chain.Stations.Length);
            Assert.Equal(0, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Seed").StationIndex);
            Assert.Equal(1, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Discount").StationIndex);
            Assert.Equal(2, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Validate").StationIndex);
            Assert.False(string.IsNullOrEmpty(CallerChainKeyBuilder.Build(chain.Anchor)));
        }

        [Fact]
        public void RouteGraphAssembler_Build_LocalVariablePattern_UsesCtorLocationForChainKey()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return route
            .Station(""Seed"", () => new { id = 1 })
            .Station(""Next"", (int id) => new { id = id + 1 });
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(RouteChainAnchorKind.LocalVariable, chain.Anchor.Kind);
            Assert.False(string.IsNullOrEmpty(CallerChainKeyBuilder.Build(chain.Anchor)));
        }

        [Fact]
        public void RouteGraphAssembler_Build_FactoryExtension_RegistersFactoryAnchor()
        {
            const string source = @"
using TrainOP;

public static class FactoryRoute
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.Equal(RouteChainAnchorKind.MethodInvocation, consumerChain.Anchor.Kind);
            Assert.NotNull(consumerChain.Anchor.FactoryMethod);
            Assert.Equal("CreateSeed", consumerChain.Anchor.FactoryMethod.Name);
        }

        [Fact]
        public void RouteGraphAssembler_Build_TwoChains_ProducesDistinctChainIds()
        {
            const string source = @"
using TrainOP;

public static class DualRoute
{
    public static TrainRoute BuildA() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });

    public static TrainRoute BuildB() => new TrainRoute()
        .Station(""Seed"", () => new { id = 2 })
        .Station(""Next"", (int id) => new { id = id + 2 });
}";

            var graph = BuildGraph(source);

            Assert.Equal(2, graph.Chains.Length);
            var chainIds = graph.Chains
                .Select(chain => CallerChainKeyBuilder.Build(chain.Anchor))
                .ToArray();
            Assert.Equal(2, chainIds.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void RouteGraphAssembler_Build_OrphanStation_IsNotChained()
        {
            const string source = @"
using TrainOP;

public static class OrphanRoute
{
    public static TrainRoute Build()
    {
        var orphan = new TrainRoute().Station(""Orphan"", (string paymentId) => new { paymentId });
        return new TrainRoute()
            .Station(""Seed"", () => new { paymentId = ""pay-1"" })
            .Station(""Next"", (string paymentId) => new { paymentId = paymentId + ""-ok"" });
    }
}";

            var graph = BuildGraph(source);
            var orphanInvocation = graph.StationSites
                .Single(site => site.StationName == "Orphan")
                .Invocation;

            Assert.True(graph.IsChainedInvocation(orphanInvocation.GetLocation()));
            Assert.True(graph.IsChainedInvocation(
                graph.StationSites.Single(site => site.StationName == "Seed").Invocation.GetLocation()));
        }

        [Fact]
        public void RouteGraphAssembler_TryGetChainForInvocation_ResolvesDownstreamChain()
        {
            const string source = @"
using TrainOP;

public static class DownstreamRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            var graph = BuildGraph(source);
            var downstream = graph.StationSites.Single(site => site.StationName == "Next").Invocation;

            Assert.True(graph.TryGetChainForInvocation(downstream, out var chain));
            Assert.Equal(2, chain.Stations.Length);
        }

        private static RouteGraph BuildGraph(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\Test0.cs");
            var compilation = CSharpCompilation.Create(
                "RouteGraphAssemblerTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var sites = RouteSiteDiscoverer.CollectAll(compilation);
            return RouteGraphAssembler.Build(sites, compilation);
        }

        private static MetadataReference[] GetMetadataReferences()
        {
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            return new[]
            {
                MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Private.CoreLib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TrainOP.CargoManifest).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)
            };
        }
    }
}
