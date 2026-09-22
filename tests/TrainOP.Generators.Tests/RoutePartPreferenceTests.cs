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
    /// Tests <see cref="RoutePartPreference"/> ports used by Assembler (K1).
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
        public void score_legacy_anchor_uses_root_shape_not_kind_sprawl()
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

            var creationAnchor = new RouteChainAnchor(
                RouteChainAnchorKind.ObjectCreation,
                objectCreation,
                objectCreation.GetLocation(),
                model.GetDeclaredSymbol(root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single()) as IMethodSymbol);

            var localAnchor = new RouteChainAnchor(
                RouteChainAnchorKind.LocalVariable,
                identifier,
                objectCreation.GetLocation(),
                creationAnchor.ContainingMethod);

            Assert.True(RoutePartPreference.ScoreLegacyAnchor(localAnchor)
                > RoutePartPreference.ScoreLegacyAnchor(creationAnchor));
            Assert.True(RoutePartPreference.IsOriginKeyed(localAnchor));
            Assert.False(RoutePartPreference.IsOriginKeyed(creationAnchor));
        }

        [Fact]
        public void score_legacy_branch_join_shape_without_kind_switch()
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
            var model = compilation.GetSemanticModel(tree);
            var station = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(StationSyntaxHelper.IsCandidateStationInvocation);

            var joinAnchor = new RouteChainAnchor(
                RouteChainAnchorKind.BranchJoin,
                station,
                station.GetLocation(),
                model.GetDeclaredSymbol(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single()) as IMethodSymbol);

            Assert.Equal(1, RoutePartPreference.ScoreLegacyAnchor(joinAnchor));
            Assert.False(RoutePartPreference.IsOriginKeyed(joinAnchor));
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
