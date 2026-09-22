using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests K4 peel/detect split (<see cref="RouteChainPeel"/> / <see cref="RouteAnchorDetector"/>).
    /// </summary>
    public sealed class RouteChainPeelTests
    {
        [Fact]
        public void try_advance_chain_peels_next_station()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "RouteChainPeelTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var creation = tree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single();

            var stations = ImmutableArray.CreateBuilder<StationChainLink>();
            Assert.True(RouteChainPeel.TryAdvanceChain(
                creation,
                model,
                stations,
                out var next,
                chainedInvocations: null));
            Assert.Equal("Seed", stations.Single().StationName);
            Assert.True(RouteChainPeel.TryAdvanceChain(
                next,
                model,
                stations,
                out _,
                chainedInvocations: null));
            Assert.Equal(new[] { "Seed", "Next" }, stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void detector_matches_walker_facade_for_creation()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "RouteAnchorDetectorTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var creation = tree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single();

            Assert.True(RouteAnchorDetector.TryDetect(creation, model, out var viaDetector));
            Assert.True(RouteChainWalker.TryDetectAnchorSite(creation, model, out var viaWalker));
            Assert.Equal(viaDetector.Kind, viaWalker.Kind);
            Assert.Equal(RouteChainAnchorKind.ObjectCreation, viaDetector.Kind);
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
