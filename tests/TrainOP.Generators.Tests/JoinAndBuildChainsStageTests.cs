using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Facade entry tests for <see cref="JoinChainsStage"/> and <see cref="BuildChainsStage"/>.
    /// </summary>
    public sealed class JoinAndBuildChainsStageTests
    {
        [Fact]
        public void JoinChainsStage_Find_MatchesJoinSetFinder_ForTernary()
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

            var (tree, model, _) = Compile(source);
            var viaFacade = JoinChainsStage.Find(tree, model);
            var viaFinder = BranchRouteJoinSetFinder.Find(tree, model);

            Assert.Equal(viaFinder.Length, viaFacade.Length);
            Assert.Single(viaFacade);
        }

        [Fact]
        public void JoinChainsStage_Join_ProducesJoinedChainWithMergedTerminals()
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

            var (tree, model, _) = Compile(source);
            var joinSet = Assert.Single(JoinChainsStage.Find(tree, model));
            var joined = JoinChainsStage.Join(joinSet, model);

            Assert.True(joined.Validation.CanMerge);
            Assert.Equal(TerminalSet.Origin.Join, joined.MergedTerminals.Provenance);
            Assert.False(joined.MergedTerminals.HasUnknownReturn);
            Assert.Single(joined.MergedTerminals.Wagons);
            Assert.Equal("value", joined.MergedTerminals.Wagons[0].Name);
        }

        [Fact]
        public void JoinChainsStage_Collect_FillsGenerationModelJoinedChains()
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

            var (_, _, compilation) = Compile(source);
            var sites = RoutePartDiscoverer.CollectAll(compilation);
            var graph = BuildChainsStage.Build(sites, compilation);
            var model = GenerationModel.Build(sites, graph, compilation);

            Assert.NotEmpty(model.JoinedChains);
            Assert.Contains(model.Terminals, t => t.Provenance == TerminalSet.Origin.Join);
        }

        [Fact]
        public void JoinChainsStage_IsForkingExpression_RecognizesTernaryCoalesceSwitch()
        {
            const string source = @"
class C
{
    object M(bool b, object a, object c, int i) => b ? a : c ?? (i switch { 0 => a, _ => c });
}";

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();
            var conditional = root.DescendantNodes().OfType<ConditionalExpressionSyntax>().First();
            var coalesce = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
                .First(b => b.IsKind(SyntaxKind.CoalesceExpression));
            var switchExpr = root.DescendantNodes().OfType<SwitchExpressionSyntax>().First();
            var plain = root.DescendantNodes().OfType<IdentifierNameSyntax>().First();

            Assert.True(JoinChainsStage.IsForkingExpression(conditional));
            Assert.True(JoinChainsStage.IsForkingExpression(coalesce));
            Assert.True(JoinChainsStage.IsForkingExpression(switchExpr));
            Assert.False(JoinChainsStage.IsForkingExpression(plain));
        }

        [Fact]
        public void BuildChainsStage_Build_MatchesRouteGraphAssembler()
        {
            const string source = @"
using TrainOP;

public static class LinearRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            var (_, _, compilation) = Compile(source);
            var sites = RoutePartDiscoverer.CollectAll(compilation);
            var viaFacade = BuildChainsStage.Build(sites, compilation);
            var viaAssembler = RouteGraphAssembler.Build(sites, compilation);

            Assert.Equal(viaAssembler.Chains.Length, viaFacade.Chains.Length);
            Assert.Equal(viaAssembler.StationLinks.Length, viaFacade.StationLinks.Length);
            Assert.Single(viaFacade.Chains);
            Assert.Equal(2, viaFacade.Chains[0].Stations.Length);
        }

        [Fact]
        public void BuildChainsStage_EndingAt_BuildsChainThroughDownstreamStation()
        {
            const string source = @"
using TrainOP;

public static class LinearRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            var (tree, model, _) = Compile(source);
            var next = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(inv => inv.ToString().Contains("\"Next\"", StringComparison.Ordinal));

            Assert.True(BuildChainsStage.EndingAt(next, model, out var chain));
            Assert.Equal(2, chain.Stations.Length);
        }

        private static (SyntaxTree Tree, SemanticModel Model, Compilation Compilation) Compile(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\JoinBuildStages.cs");
            var compilation = CSharpCompilation.Create(
                "JoinAndBuildChainsStageTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return (syntaxTree, compilation.GetSemanticModel(syntaxTree), compilation);
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
