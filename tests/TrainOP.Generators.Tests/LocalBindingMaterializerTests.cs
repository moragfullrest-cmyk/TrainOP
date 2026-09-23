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
    /// Tests <see cref="LocalBindingMaterializer"/>.
    /// </summary>
    public sealed class LocalBindingMaterializerTests
    {
        [Fact]
        public void try_materialize_local_after_new_is_local_variable_anchor()
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

            Assert.True(LocalBindingMaterializer.TryMaterialize(identifier, model, out var binding));
            Assert.Same(identifier, binding.Identifier);
            Assert.IsType<CreationSeed>(binding.Origin);
            Assert.Equal("Build", binding.ContainingMethod.Name);
        }

        [Fact]
        public void try_materialize_local_after_private_factory_is_method_invocation_anchor()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var route = CreateSeed();
        return route
            .Station(""Next"", (int id) => new { id = id + 1 });
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetLocalReceiver(source, "route", out var identifier, out var model);

            Assert.True(LocalBindingMaterializer.TryMaterialize(identifier, model, out var binding));
            Assert.IsType<FactoryCall>(binding.Origin);
            Assert.Equal(FactoryCallKind.Inline, ((FactoryCall)binding.Origin).Kind);
        }

        [Fact]
        public void try_materialize_local_after_public_factory_is_factory_schema_anchor()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var route = CreateSeed();
        return route
            .Station(""Next"", (int id) => new { id = id + 1 });
    }

    public static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetLocalReceiver(source, "route", out var identifier, out var model);

            Assert.True(LocalBindingMaterializer.TryMaterialize(identifier, model, out var binding));
            Assert.Equal(FactoryCallKind.Schema, ((FactoryCall)binding.Origin).Kind);
        }

        [Fact]
        public void try_materialize_rejects_identifier_without_known_origin()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(TrainRoute route) => route
        .Station(""Seed"", () => new { id = 1 });
}";

            GetLocalReceiver(source, "route", out var identifier, out var model);

            Assert.False(LocalBindingMaterializer.TryMaterialize(identifier, model, out _));
        }

        [Fact]
        public void try_materialize_join_assign_ternary_is_local_variable_anchor()
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

            Assert.True(LocalBindingMaterializer.TryMaterialize(identifier, model, out var binding));
            Assert.Same(identifier, binding.Identifier);
            Assert.Null(binding.Origin);
            Assert.Null(binding.FactoryMethod);
            Assert.Single(binding.InitialWagons);
            Assert.Equal("id", binding.InitialWagons[0].Name);
        }

        [Fact]
        public void try_materialize_join_assign_shared_factory_stamps_factory_origin()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool flag)
    {
        var route = flag ? CreateSeed() : CreateSeed();
        return route
            .Station(""Join"", (int id) => new { id });
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            GetLocalReceiver(source, "route", out var identifier, out var model);

            Assert.True(LocalBindingMaterializer.TryMaterialize(identifier, model, out var binding));
            Assert.IsType<FactoryCall>(binding.Origin);
            Assert.Equal("CreateSeed", binding.FactoryMethod.Name);
        }

        private static void GetLocalReceiver(
            string source,
            string localName,
            out IdentifierNameSyntax identifier,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "LocalBindingMaterializerTests",
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
