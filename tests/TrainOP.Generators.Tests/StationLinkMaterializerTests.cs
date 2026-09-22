using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Parts;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests <see cref="StationLinkMaterializer"/> (C1).
    /// </summary>
    public sealed class StationLinkMaterializerTests
    {
        [Fact]
        public void try_materialize_station_resolves_name_and_handler()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            GetStationByName(source, "Next", out var invocation, out var model);

            Assert.True(StationLinkMaterializer.TryMaterialize(invocation, model, out var link));
            Assert.Equal(StationLinkKind.Station, link.Kind);
            Assert.Equal("Next", link.StationName);
            Assert.Same(invocation, link.Invocation);
            Assert.NotNull(link.Handler);

            var legacy = link.ToStationChainLink();
            Assert.Equal("Next", legacy.StationName);
            Assert.Same(invocation, legacy.Invocation);
        }

        [Fact]
        public void try_materialize_service_station_resolves_kind()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .ServiceStation(""Recover"", (ref int id, RedSignal red) => RailwaySignals.White);
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "StationLinkMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var service = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(StationSyntaxHelper.IsCandidateServiceStationInvocation);

            Assert.True(StationLinkMaterializer.TryMaterializeServiceStation(service, model, null, out var link));
            Assert.Equal(StationLinkKind.ServiceStation, link.Kind);
            Assert.Equal("Recover", link.StationName);
        }

        [Fact]
        public void try_materialize_rejects_non_station_invocation()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Seed"", () => new { id = 1 });

    private static TrainRoute CreateSeed() => new TrainRoute();
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "StationLinkMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var factory = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(inv =>
                    inv.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "CreateSeed");

            Assert.False(StationLinkMaterializer.TryMaterialize(factory, model, out _));
        }

        private static void GetStationByName(
            string source,
            string stationName,
            out InvocationExpressionSyntax invocation,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "StationLinkMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            // Nested fluent spans share SpanStart (outer covers inner); select by name literal.
            invocation = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(StationSyntaxHelper.IsCandidateStationInvocation)
                .Single(inv =>
                    inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
                    && lit.Token.ValueText == stationName);
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
