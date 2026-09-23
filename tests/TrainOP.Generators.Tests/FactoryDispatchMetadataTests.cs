using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests <see cref="FactoryDispatchMetadata"/> without kind-switch on origin ports.
    /// </summary>
    public sealed class FactoryDispatchMetadataTests
    {
        [Fact]
        public void try_resolve_from_body_uses_inner_ctor_key_and_station_count()
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

            var tree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\Dispatch.cs");
            var compilation = CSharpCompilation.Create(
                "FactoryDispatchMetadataTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var createSeed = tree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "CreateSeed");
            var createSeedSymbol = model.GetDeclaredSymbol(createSeed) as IMethodSymbol;
            Assert.NotNull(createSeedSymbol);

            Assert.True(FactoryDispatchMetadata.TryResolve(
                createSeedSymbol,
                compilation,
                out var key,
                out var stationCount));

            var objectCreation = createSeed.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single();
            var expected = CallerChainKeyBuilder.BuildFromLocation(
                objectCreation.GetLocation(),
                "CreateSeed");

            Assert.Equal(expected, key);
            Assert.Equal(1, stationCount);
        }

        [Fact]
        public void try_resolve_nested_factory_sums_upstream_and_extension_stations()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Outer() => Inner()
        .Station(""Outer"", (int id) => new { id });

    private static TrainRoute Inner() => new TrainRoute()
        .Station(""Inner"", () => new { id = 1 });
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\NestedDispatch.cs");
            var compilation = CSharpCompilation.Create(
                "FactoryDispatchMetadataNestedTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var outer = tree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "Outer");
            var outerSymbol = model.GetDeclaredSymbol(outer) as IMethodSymbol;
            Assert.NotNull(outerSymbol);

            Assert.True(FactoryDispatchMetadata.TryResolve(
                outerSymbol,
                compilation,
                out var key,
                out var stationCount));

            Assert.False(string.IsNullOrEmpty(key));
            // Inner body has 1 station; Outer extension adds 1 → nested resolve sees Outer chain stations
            // after resolving Inner upstream.
            Assert.Equal(2, stationCount);
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
