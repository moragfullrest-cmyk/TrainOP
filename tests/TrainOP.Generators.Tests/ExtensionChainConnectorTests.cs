using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests <see cref="ExtensionTailMaterializer"/> / <see cref="ExtensionChainConnector"/>.
    /// </summary>
    public sealed class ExtensionChainConnectorTests
    {
        [Fact]
        public void try_materialize_tail_after_private_factory()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 })
        .Station(""Tail"", (int id) => new { id });

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetFactoryInvocation(source, "CreateSeed", out var invocation, out var model);
            Assert.True(FactoryCallMaterializer.TryMaterialize(invocation, model, out var factoryCall));
            Assert.Equal(FactoryCallKind.Inline, factoryCall.Kind);

            Assert.True(ExtensionTailMaterializer.TryMaterialize(factoryCall, model, out var tail));
            Assert.Equal(2, tail.Stations.Length);
            Assert.Equal(new[] { "Next", "Tail" }, tail.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void try_connect_extends_factory_call()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetFactoryInvocation(source, "CreateSeed", out var invocation, out var model);
            Assert.True(FactoryCallMaterializer.TryMaterialize(invocation, model, out var factoryCall));

            Assert.True(ExtensionChainConnector.TryConnect(
                factoryCall,
                model,
                out var ctor,
                out var chain));

            Assert.Contains(ctor.Edges, e => e.Kind == PartEdgeKind.Extend);
            Assert.True(chain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (chain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateSeed", chain.FactoryMethod.Name);
            Assert.Equal(new[] { "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void try_connect_ending_at_public_factory_schema()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    public static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "ExtensionChainConnectorTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            // CreateSeed body also has Station("Seed"); pick the Build extension endpoint by name.
            var endpoint = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(StationSyntaxHelper.IsCandidateStationInvocation)
                .Single(inv =>
                    inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
                    && lit.Token.ValueText == "Next");

            Assert.True(ExtensionChainConnector.TryConnectEndingAt(
                endpoint,
                model,
                compilation,
                out var chain,
                out _));

            Assert.True(chain.Origin is FactoryCall { Kind: FactoryCallKind.Schema } || (chain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Schema));
            Assert.Equal("Next", chain.Stations.Single().StationName);
        }

        [Fact]
        public void try_materialize_rejects_factory_without_extension_stations()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed();

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetFactoryInvocation(source, "CreateSeed", out var invocation, out var model);
            Assert.True(FactoryCallMaterializer.TryMaterialize(invocation, model, out var factoryCall));
            Assert.False(ExtensionTailMaterializer.TryMaterialize(factoryCall, model, out _));
        }

        private static void GetFactoryInvocation(
            string source,
            string factoryName,
            out InvocationExpressionSyntax invocation,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "ExtensionChainConnectorTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            invocation = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(inv =>
                    inv.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == factoryName);
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
