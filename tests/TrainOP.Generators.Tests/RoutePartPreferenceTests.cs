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
    /// Tests <see cref="RoutePartPreference"/> ports used by Assembler.
    /// </summary>
    public sealed class RoutePartPreferenceTests
    {
        [Fact]
        public void score_prefers_local_binding_over_factory_and_creation()
        {
            Assert.True(RoutePartPreference.Score(new LocalBinding(
                SyntaxFactory.IdentifierName("route"),
                Location.None,
                new CreationSeed(
                    SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("TrainRoute")),
                    Location.None))) > RoutePartPreference.Score(
                new FactoryCall(
                    SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Create")),
                    Location.None,
                    FactoryCallKind.Inline,
                    factoryMethod: null)));

            Assert.True(RoutePartPreference.Score(
                new FactoryCall(
                    SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("Create")),
                    Location.None,
                    FactoryCallKind.Inline,
                    factoryMethod: null))
                > RoutePartPreference.Score(
                    new CreationSeed(
                        SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("TrainRoute")),
                        Location.None)));
        }

        [Fact]
        public void score_and_origin_key_use_part_shape()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute().Station(""Seed"", () => new { id = 1 });
        return route;
    }
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "RoutePartPreferenceTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            var objectCreation = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single();
            var identifier = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Last(id => id.Identifier.ValueText == "route");

            Assert.True(CreationSeedMaterializer.TryMaterialize(objectCreation, model, out var creation));
            Assert.True(LocalBindingMaterializer.TryMaterialize(identifier, model, out var local));

            Assert.True(RoutePartPreference.Score(local) > RoutePartPreference.Score(creation));
            Assert.True(RoutePartPreference.IsOriginKeyed(local));
            Assert.False(RoutePartPreference.IsOriginKeyed(creation));
        }

        [Fact]
        public void score_join_seed_is_lowest_origin()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Join"", () => new { id = 1 });
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "RoutePartPreferenceBranchTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var station = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(StationSyntaxHelper.IsCandidateStationInvocation);

            var joinSeed = new JoinSeed(
                station,
                station,
                System.Collections.Immutable.ImmutableArray<JoinArm>.Empty,
                validation: null);

            Assert.Equal(1, RoutePartPreference.Score(joinSeed));
            Assert.False(RoutePartPreference.IsOriginKeyed(joinSeed));
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
