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
    /// Tests <see cref="JoinChainConnector"/> arms → JoinSeed + validator.
    /// </summary>
    public sealed class JoinChainConnectorTests
    {
        [Fact]
        public void try_connect_matching_ternary_can_merge()
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

            GetForkAndDownstream(source, out var fork, out var downstream, out var model);

            Assert.True(JoinChainConnector.TryConnect(
                fork,
                downstream,
                model,
                out var ctor,
                out var seed));

            Assert.True(seed.CanMerge);
            Assert.Equal(2, seed.Arms.Length);
            Assert.Equal(2, ctor.Edges.Count(e => e.Kind == PartEdgeKind.Join));
            Assert.Single(seed.MergedTerminalWagons);
            Assert.Equal("value", seed.MergedTerminalWagons[0].Name);
            Assert.True(RouteOriginPorts.IsOriginPart(seed));
        }

        [Fact]
        public void try_connect_unresolved_arm_cannot_merge_but_still_connects()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft, TrainRoute other) =>
        (useLeft
            ? new TrainRoute().Station(""Left"", () => new { value = 1 })
            : other)
        .Station(""Join"", (int value) => new { value });
}";

            GetForkAndDownstream(source, out var fork, out var downstream, out var model);

            Assert.True(JoinChainConnector.TryConnect(
                fork,
                downstream,
                model,
                out _,
                out var seed));

            Assert.False(seed.CanMerge);
            Assert.NotEmpty(seed.Validation.Diagnostics);
        }

        [Fact]
        public void join_chains_stage_join_uses_connector_validation()
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

            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "JoinChainConnectorTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(tree);
            var joinSet = Assert.Single(JoinChainsStage.Find(tree, model));

            var joined = JoinChainsStage.Join(joinSet, model);
            Assert.True(joined.Validation.CanMerge);
            Assert.Single(joined.Validation.MergedTerminalWagons);
        }

        private static void GetForkAndDownstream(
            string source,
            out ExpressionSyntax fork,
            out InvocationExpressionSyntax downstream,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "JoinChainConnectorTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);

            // Nested Left/Right also match Station; pick Join by name (SpanStart equals the fork).
            downstream = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(StationSyntaxHelper.IsCandidateStationInvocation)
                .Single(inv =>
                    inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit
                    && lit.Token.ValueText == "Join");
            var memberAccess = (MemberAccessExpressionSyntax)downstream.Expression;
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
