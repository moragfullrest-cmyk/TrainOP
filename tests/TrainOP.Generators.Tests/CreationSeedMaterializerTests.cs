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
    /// Tests <see cref="CreationSeedMaterializer"/>.
    /// </summary>
    public sealed class CreationSeedMaterializerTests
    {
        [Fact]
        public void try_materialize_recognizes_new_train_route()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetObjectCreation(source, out var creation, out var model);

            Assert.True(CreationSeedMaterializer.TryMaterialize(creation, model, out var seed));
            Assert.Same(creation, seed.Root);
            Assert.Equal(creation.GetLocation().SourceSpan, seed.Location.SourceSpan);
            Assert.NotNull(seed.ContainingMethod);
            Assert.Equal("Build", seed.ContainingMethod.Name);
            Assert.True(RouteOriginPorts.IsOriginPart(seed));
            Assert.True(RouteOriginPorts.TryGetRoot(seed, out var root));
            Assert.Same(creation, root);
        }

        [Fact]
        public void try_materialize_rejects_non_train_route_creation()
        {
            const string source = @"
public class Box { }
public static class Route
{
    public static Box Build() => new Box();
}";

            GetObjectCreation(source, out var creation, out var model);

            Assert.False(CreationSeedMaterializer.TryMaterialize(creation, model, out _));
        }

        [Fact]
        public void try_materialize_syntax_node_overload_ignores_non_object_creation()
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
                "CreationSeedMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var invocation = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First();

            Assert.False(CreationSeedMaterializer.TryMaterialize(invocation, model, out _));
        }

        private static void GetObjectCreation(
            string source,
            out ObjectCreationExpressionSyntax creation,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "CreationSeedMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            creation = tree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .First();
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
