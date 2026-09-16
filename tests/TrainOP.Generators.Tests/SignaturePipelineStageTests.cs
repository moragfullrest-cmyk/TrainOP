using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Stage-boundary tests: GroupSignatures → AttachChainContext → BranchPlans.
    /// </summary>
    public sealed class SignaturePipelineStageTests
    {
        [Fact]
        public void Group_ThenAttach_ThenBranchPlans_LinearChain_CanonicalDispatch()
        {
            const string source = @"
using TrainOP;

public static class LinearRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var graph = BuildGraph(source);
            var groups = SignatureGroupingStage.Group(graph);

            Assert.NotEmpty(groups);

            AttachChainContextStage.Attach(groups.Values, graph.ChainIndex);
            var plans = BranchPlanStage.Build(groups.Values);

            Assert.NotEmpty(plans);
            Assert.All(plans, plan => Assert.False(plan.UsesChainDispatch));
        }

        [Fact]
        public void Group_ThenAttach_ThenBranchPlans_DivergentWagonNames_UsesChainDispatch()
        {
            const string source = @"
using TrainOP;

public static class DualWagonRoute
{
    public static TrainRoute BuildA() => new TrainRoute()
        .Station(""SeedA"", () => new { paymentId = ""p"" })
        .Station(""Next"", (string paymentId) => new { paymentId });

    public static TrainRoute BuildB() => new TrainRoute()
        .Station(""SeedB"", () => new { orderId = ""o"" })
        .Station(""Next"", (string orderId) => new { orderId });
}";

            var graph = BuildGraph(source);
            var groups = SignatureGroupingStage.Group(graph);

            AttachChainContextStage.Attach(groups.Values, graph.ChainIndex);
            var plans = BranchPlanStage.Build(groups.Values);

            Assert.Contains(plans, plan => plan.UsesChainDispatch);
        }

        [Fact]
        public void Group_WithoutAttach_BranchPlans_RemainCanonicalEvenWithDivergentNames()
        {
            // Without Attach, chain bindings are empty → RequiresChainDispatch is false
            // regardless of divergent entry wagon names across sites.
            const string source = @"
using TrainOP;

public static class DualWagonRoute
{
    public static TrainRoute BuildA() => new TrainRoute()
        .Station(""SeedA"", () => new { paymentId = ""p"" })
        .Station(""Next"", (string paymentId) => new { paymentId });

    public static TrainRoute BuildB() => new TrainRoute()
        .Station(""SeedB"", () => new { orderId = ""o"" })
        .Station(""Next"", (string orderId) => new { orderId });
}";

            var graph = BuildGraph(source);
            var groups = SignatureGroupingStage.Group(graph);
            var plansWithoutAttach = BranchPlanStage.Build(groups.Values);

            Assert.All(plansWithoutAttach, plan => Assert.False(plan.UsesChainDispatch));

            AttachChainContextStage.Attach(groups.Values, graph.ChainIndex);
            var plansAfterAttach = BranchPlanStage.Build(groups.Values);

            Assert.Contains(plansAfterAttach, plan => plan.UsesChainDispatch);
        }

        [Fact]
        public void WithSignaturePipeline_PopulatesGenerationModelBranchPlans()
        {
            const string source = @"
using TrainOP;

public static class LinearRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            var (sites, graph, compilation) = BuildSitesGraph(source);
            var model = GenerationModel.Build(sites, graph, compilation);
            var groups = SignatureGroupingStage.Group(model.RouteGraph);
            AttachChainContextStage.Attach(groups.Values, model.RouteGraph.ChainIndex);
            var plans = BranchPlanStage.Build(groups.Values);

            model = model.WithSignaturePipeline(
                groups.Values.ToImmutableArray(),
                plans);

            Assert.Equal(groups.Count, model.SignatureGroups.Length);
            Assert.Equal(plans.Length, model.BranchPlans.Length);
            Assert.NotEmpty(model.StationSignatures);
            Assert.NotEmpty(model.Anchors);
        }

        private static RouteGraph BuildGraph(string source)
        {
            return BuildSitesGraph(source).Graph;
        }

        private static (ImmutableArray<RouteSite> Sites, RouteGraph Graph, Compilation Compilation)
            BuildSitesGraph(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\SignaturePipeline.cs");
            var compilation = CSharpCompilation.Create(
                "SignaturePipelineStageTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var sites = RouteSiteDiscoverer.CollectAll(compilation);
            var graph = BuildChainsStage.Build(sites, compilation);
            return (sites, graph, compilation);
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
