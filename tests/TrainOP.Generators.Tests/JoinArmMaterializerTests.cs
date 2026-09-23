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
    /// Tests <see cref="JoinArmMaterializer"/>.
    /// </summary>
    public sealed class JoinArmMaterializerTests
    {
        [Fact]
        public void try_materialize_arms_ternary_resolves_both()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft
            ? new TrainRoute().Station(""Left"", () => new { value = 1 })
            : new TrainRoute().Station(""Right"", () => new { value = 2 }))
        .Station(""Join"", (int value) => new { value });
}";

            GetForkReceiver(source, out var fork, out var model);

            Assert.True(JoinArmMaterializer.TryMaterializeArms(fork, model, out var arms));
            Assert.Equal(2, arms.Length);
            Assert.All(arms, a => Assert.True(a.IsResolved));
            Assert.Equal("Left", arms[0].Chain.Stations.Single().StationName);
            Assert.Equal("Right", arms[1].Chain.Stations.Single().StationName);
            Assert.All(arms, a => Assert.NotNull(a.ToBranchRouteGraph()));
        }

        [Fact]
        public void try_materialize_arms_coalesce_resolves_both()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(TrainRoute left) =>
        (left ?? new TrainRoute().Station(""Right"", () => new { value = 2 }))
        .Station(""Join"", (int value) => new { value });
}";

            GetForkReceiver(source, out var fork, out var model);

            Assert.True(JoinArmMaterializer.TryMaterializeArms(fork, model, out var arms));
            Assert.Equal(2, arms.Length);
        }

        [Fact]
        public void try_materialize_arms_rejects_non_fork()
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
                "JoinArmMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var creation = tree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .First();

            Assert.False(JoinArmMaterializer.TryMaterializeArms(creation, model, out _));
        }

        [Fact]
        public void try_materialize_leaf_resolves_fluent_arm()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Left"", () => new { value = 1 });
}";

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "JoinArmMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var endpoint = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Last(StationSyntaxHelper.IsCandidateStationInvocation);

            Assert.True(JoinArmMaterializer.TryMaterializeLeaf(endpoint, model, out var arm));
            Assert.True(arm.IsResolved);
            Assert.Equal("Left", arm.Chain.Stations.Single().StationName);
        }

        private static void GetForkReceiver(
            string source,
            out ExpressionSyntax fork,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "JoinArmMaterializerTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);

            // Nested Left/Right also match Station; pick Join by name (SpanStart equals the fork).
            var joinStation = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(StationSyntaxHelper.IsCandidateStationInvocation)
                .Single(inv =>
                    inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
                    && lit.Token.ValueText == "Join");
            var memberAccess = (MemberAccessExpressionSyntax)joinStation.Expression;
            fork = memberAccess.Expression;
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
