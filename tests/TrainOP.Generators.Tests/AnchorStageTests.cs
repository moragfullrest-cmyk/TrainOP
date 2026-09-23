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
    /// Tests <see cref="AnchorStage"/> origin-part resolution.
    /// </summary>
    public sealed class AnchorStageTests
    {
        [Fact]
        public void try_resolve_part_object_creation_is_creation_seed()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetNode<ObjectCreationExpressionSyntax>(source, out var node, out var model);

            Assert.True(AnchorStage.TryResolvePart(node, model, out var part));
            Assert.IsType<CreationSeed>(part);
        }

        [Fact]
        public void try_resolve_part_local_after_new_is_local_binding()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return route
            .Station(""Seed"", () => new { id = 1 });
    }
}";

            GetLocalReceiver(source, "route", out var identifier, out var model);

            Assert.True(AnchorStage.TryResolvePart(identifier, model, out var part));
            var binding = Assert.IsType<LocalBinding>(part);
            Assert.Same(identifier, binding.Identifier);
        }

        [Fact]
        public void try_resolve_part_factory_extension_is_factory_call()
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

            Assert.True(AnchorStage.TryResolvePart(invocation, model, out var part));
            var factoryCall = Assert.IsType<FactoryCall>(part);
            Assert.Equal(FactoryCallKind.Inline, factoryCall.Kind);
            Assert.Equal("CreateSeed", factoryCall.FactoryMethod.Name);
        }

        [Fact]
        public void try_resolve_part_join_assign_local_is_local_binding()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool flag)
    {
        var route = flag
            ? new TrainRoute().Station(""A"", () => new { id = 1 })
            : new TrainRoute().Station(""B"", () => new { id = 2 });
        return route
            .Station(""Join"", (int id) => new { id });
    }
}";

            GetLocalReceiver(source, "route", out var identifier, out var model);

            Assert.True(AnchorStage.TryResolvePart(identifier, model, out var part));
            var binding = Assert.IsType<LocalBinding>(part);
            Assert.Null(binding.Origin);
        }

        private static void GetNode<T>(
            string source,
            out T node,
            out SemanticModel model)
            where T : SyntaxNode
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "AnchorStageTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            node = tree.GetRoot().DescendantNodes().OfType<T>().First();
        }

        private static void GetLocalReceiver(
            string source,
            string localName,
            out IdentifierNameSyntax identifier,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "AnchorStageTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            identifier = tree.GetRoot()
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .First(id =>
                    id.Identifier.ValueText == localName
                    && id.Parent is MemberAccessExpressionSyntax member
                    && ReferenceEquals(member.Expression, id));
        }

        private static void GetFactoryInvocation(
            string source,
            string factoryName,
            out InvocationExpressionSyntax invocation,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "AnchorStageTests",
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
