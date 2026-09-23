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
    /// Tests dispatch/keys ports on <see cref="FactoryCall"/>.
    /// </summary>
    public sealed class FactoryCallDispatchTests
    {
        [Fact]
        public void uses_schema_dispatch_maps_inline_and_schema()
        {
            const string inlineSource = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed();

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            const string schemaSource = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed();

    public static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetFactoryCall(inlineSource, "CreateSeed", out var inlineCall, out _);
            GetFactoryCall(schemaSource, "CreateSeed", out var schemaCall, out _);

            Assert.Equal(FactoryCallKind.Inline, inlineCall.Kind);
            Assert.False(inlineCall.UsesSchemaDispatch);
            Assert.Equal(FactoryCallKind.Schema, schemaCall.Kind);
            Assert.True(schemaCall.UsesSchemaDispatch);
        }

        [Fact]
        public void try_build_caller_chain_key_matches_inner_ctor_for_inline_factory()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed()
    {
        return new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });
    }
}";

            GetFactoryCall(source, "CreateSeed", out var factoryCall, out var compilation);

            Assert.True(factoryCall.TryBuildCallerChainKey(compilation, out var factoryKey));
            Assert.False(string.IsNullOrEmpty(factoryKey));

            var tree = compilation.SyntaxTrees.Single();
            var createSeed = tree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "CreateSeed");
            var objectCreation = createSeed.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single();
            var ctorKey = CallerChainKeyBuilder.BuildFromLocation(
                objectCreation.GetLocation(),
                "CreateSeed");

            Assert.Equal(ctorKey, factoryKey);
        }

        [Fact]
        public void try_resolve_dispatch_identity_includes_extension_station_count()
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

            GetFactoryCall(source, "CreateSeed", out var factoryCall, out var compilation);

            Assert.True(factoryCall.TryResolveDispatchIdentity(
                compilation,
                out var key,
                out var stationCount));
            Assert.False(string.IsNullOrEmpty(key));
            Assert.Equal(1, stationCount);
        }

        [Fact]
        public void try_build_caller_chain_key_from_origin_matches_factory_call()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed();

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetFactoryCall(source, "CreateSeed", out var factoryCall, out var compilation);
            Assert.True(FactoryCall.TryBuildCallerChainKeyFromOrigin(
                factoryCall,
                compilation,
                out var fromOrigin));
            Assert.True(factoryCall.TryBuildCallerChainKey(compilation, out var direct));
            Assert.Equal(direct, fromOrigin);
        }

        private static void GetFactoryCall(
            string source,
            string factoryName,
            out FactoryCall factoryCall,
            out Compilation compilation)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\FactoryCallDispatch.cs");
            compilation = CSharpCompilation.Create(
                "FactoryCallDispatchTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var invocation = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(inv =>
                    inv.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == factoryName);

            Assert.True(FactoryCallMaterializer.TryMaterialize(invocation, model, out factoryCall));
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
