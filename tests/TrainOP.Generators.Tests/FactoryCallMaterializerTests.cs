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
    /// Tests <see cref="FactoryCallMaterializer"/>.
    /// </summary>
    public sealed class FactoryCallMaterializerTests
    {
        [Fact]
        public void try_materialize_private_factory_is_inline()
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

            Assert.True(FactoryCallMaterializer.TryMaterialize(invocation, model, out var call));
            Assert.Equal(FactoryCallKind.Inline, call.Kind);
            Assert.Equal("CreateSeed", call.FactoryMethod.Name);
            Assert.Same(invocation, call.Root);
            Assert.False(call.InitialWagons.IsDefault);
            Assert.Equal(FactoryCallKind.Inline, call.Kind);
            Assert.True(RouteOriginPorts.TryGetRoot(call, out var root));
            Assert.Same(invocation, root);
        }

        [Fact]
        public void try_materialize_public_factory_is_schema()
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

            GetFactoryInvocation(source, "CreateSeed", out var invocation, out var model);

            Assert.True(FactoryCallMaterializer.TryMaterialize(invocation, model, out var call));
            Assert.Equal(FactoryCallKind.Schema, call.Kind);
            Assert.Equal("CreateSeed", call.FactoryMethod.Name);
            Assert.True(call.UsesSchemaDispatch);
        }

        [Fact]
        public void try_materialize_rejects_station_invocation()
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
                "FactoryCallMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var station = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(StationSyntaxHelper.IsCandidateStationInvocation);

            Assert.False(FactoryCallMaterializer.TryMaterialize(station, model, out _));
        }

        private static void GetFactoryInvocation(
            string source,
            string factoryName,
            out InvocationExpressionSyntax invocation,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "FactoryCallMaterializerTests",
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
