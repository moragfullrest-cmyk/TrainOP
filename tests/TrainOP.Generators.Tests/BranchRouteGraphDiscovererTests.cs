using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Models;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests for <see cref="BranchRouteGraphDiscoverer"/> leaf discovery under forking receivers.
    /// </summary>
    public sealed class BranchRouteGraphDiscovererTests
    {
        /// <summary>
        /// Ternary with two fluent arms yields two resolved graphs with matching station names.
        /// </summary>
        [Fact]
        public void Discover_Ternary_TwoFluentArms_ResolvesBothGraphs()
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

            var graphs = Discover(source);

            Assert.Equal(2, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));
            Assert.Equal(new[] { "Left" }, graphs[0].Chain.Stations.Select(s => s.StationName));
            Assert.Equal(new[] { "Right" }, graphs[1].Chain.Stations.Select(s => s.StationName));
            Assert.All(graphs, g => Assert.NotNull(g.Simulation));
        }

        /// <summary>
        /// Ternary seed arms that produce the same wagon name/type expose matching <see cref="ChainSimulationResult.TerminalWagons"/>.
        /// </summary>
        [Fact]
        public void Discover_Ternary_SeedArms_TerminalWagonsMatchNameAndType()
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

            var graphs = Discover(source);

            Assert.Equal(2, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));
            Assert.All(graphs, g =>
            {
                Assert.False(g.Simulation.HasUnknownReturn);
                Assert.Single(g.Simulation.TerminalWagons);
                var wagon = g.Simulation.TerminalWagons[0];
                Assert.Equal("value", wagon.Name);
                Assert.Equal(SpecialType.System_Int32, wagon.TypeSymbol.SpecialType);
            });
        }

        /// <summary>
        /// Ternary with two bare <c>new TrainRoute()</c> arms yields two resolved zero-station graphs.
        /// </summary>
        [Fact]
        public void Discover_Ternary_TwoBareCreations_ResolvesZeroStationGraphs()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft ? new TrainRoute() : new TrainRoute())
            .Station(""Seed"", () => new { value = 1 });
}";

            var graphs = Discover(source);

            Assert.Equal(2, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));
            Assert.All(graphs, g => Assert.Empty(g.Chain.Stations));
            Assert.All(graphs, g => Assert.NotNull(g.Simulation));
        }

        /// <summary>
        /// Bare <c>new TrainRoute()</c> arms produce empty <see cref="ChainSimulationResult.TerminalWagons"/>.
        /// </summary>
        [Fact]
        public void Discover_Ternary_BareCreations_TerminalWagonsEmpty()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft ? new TrainRoute() : new TrainRoute())
            .Station(""Seed"", () => new { value = 1 });
}";

            var graphs = Discover(source);

            Assert.Equal(2, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));
            Assert.All(graphs, g => Assert.Empty(g.Simulation.TerminalWagons));
        }

        /// <summary>
        /// Multi-station arm folds through rename/transform so <see cref="ChainSimulationResult.TerminalWagons"/> reflect final live state.
        /// </summary>
        [Fact]
        public void Discover_Ternary_MultiStationArm_TerminalWagonsReflectFinalState()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft
            ? new TrainRoute()
                .Station(""Seed"", () => new { value = 1 })
                .Station(""Rename"", (int value) => new { amount = value * 2 })
            : new TrainRoute().Station(""Right"", () => new { amount = 0 }))
        .Station(""Join"", (int amount) => new { amount });
}";

            var graphs = Discover(source);

            Assert.Equal(2, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));

            var leftTerminals = graphs[0].Simulation.TerminalWagons;
            Assert.Single(leftTerminals);
            Assert.Equal("amount", leftTerminals[0].Name);
            Assert.Equal(SpecialType.System_Int32, leftTerminals[0].TypeSymbol.SpecialType);
            Assert.DoesNotContain(leftTerminals, w => w.Name == "value");

            var rightTerminals = graphs[1].Simulation.TerminalWagons;
            Assert.Single(rightTerminals);
            Assert.Equal("amount", rightTerminals[0].Name);
            Assert.Equal(SpecialType.System_Int32, rightTerminals[0].TypeSymbol.SpecialType);
        }

        /// <summary>
        /// Coalesce of two locals assigned from <c>new TrainRoute()</c> yields two resolved graphs.
        /// </summary>
        [Fact]
        public void Discover_Coalesce_TwoLocalsFromCreation_ResolvesBothGraphs()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var a = new TrainRoute();
        var b = new TrainRoute();
        return (a ?? b).Station(""Seed"", () => new { value = 1 });
    }
}";

            var graphs = Discover(source);

            Assert.Equal(2, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));
            Assert.All(graphs, g => Assert.Empty(g.Chain.Stations));
            Assert.All(graphs, g => Assert.NotNull(g.Simulation));
        }

        /// <summary>
        /// Switch expression with multiple arms yields one graph per arm.
        /// </summary>
        [Fact]
        public void Discover_SwitchExpression_MultipleArms_YieldsGraphPerArm()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(int kind) =>
        (kind switch
        {
            1 => new TrainRoute().Station(""One"", () => new { value = 1 }),
            2 => new TrainRoute().Station(""Two"", () => new { value = 2 }),
            _ => new TrainRoute().Station(""Other"", () => new { value = 0 })
        }).Station(""Join"", (int value) => new { value });
}";

            var graphs = Discover(source);

            Assert.Equal(3, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));
            Assert.Equal("One", graphs[0].Chain.Stations[0].StationName);
            Assert.Equal("Two", graphs[1].Chain.Stations[0].StationName);
            Assert.Equal("Other", graphs[2].Chain.Stations[0].StationName);
        }

        /// <summary>
        /// An unresolved arm (e.g. <c>GetRoute()</c>) yields <c>IsResolved == false</c> for that branch.
        /// </summary>
        [Fact]
        public void Discover_UnresolvedArm_IsResolvedFalse()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useFactory) =>
        (useFactory
            ? GetRoute()
            : new TrainRoute().Station(""Ok"", () => new { value = 1 }))
        .Station(""Join"", (int value) => new { value });

    private static TrainRoute GetRoute() => new TrainRoute();
}";

            var graphs = Discover(source);

            Assert.Equal(2, graphs.Length);
            Assert.False(graphs[0].IsResolved);
            Assert.Null(graphs[0].Chain);
            Assert.Null(graphs[0].Simulation);
            Assert.True(graphs[1].IsResolved);
            Assert.Equal("Ok", graphs[1].Chain.Stations[0].StationName);
        }

        /// <summary>
        /// Nested ternary yields three leaf graphs.
        /// </summary>
        [Fact]
        public void Discover_NestedTernary_YieldsThreeLeaves()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool first, bool second) =>
        (first
            ? new TrainRoute().Station(""A"", () => new { value = 1 })
            : second
                ? new TrainRoute().Station(""B"", () => new { value = 2 })
                : new TrainRoute().Station(""C"", () => new { value = 3 }))
        .Station(""Join"", (int value) => new { value });
}";

            var graphs = Discover(source);

            Assert.Equal(3, graphs.Length);
            Assert.All(graphs, g => Assert.True(g.IsResolved));
            Assert.Equal(new[] { "A", "B", "C" }, graphs.Select(g => g.Chain.Stations[0].StationName));
        }

        private static ImmutableArray<BranchRouteGraph> Discover(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "BranchDiscovererTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(syntaxTree);
            var receiver = FindForkReceiver(syntaxTree);
            Assert.NotNull(receiver);

            return BranchRouteGraphDiscoverer.Discover(receiver, model);
        }

        /// <summary>
        /// Locates the forking receiver of the first <c>.Station</c> whose receiver is a ternary / coalesce / switch.
        /// </summary>
        private static ExpressionSyntax FindForkReceiver(SyntaxTree syntaxTree)
        {
            foreach (var invocation in syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
                    || !string.Equals(memberAccess.Name.Identifier.ValueText, "Station", StringComparison.Ordinal))
                {
                    continue;
                }

                var peeled = ReceiverExpressionPeel.UnwrapTransparent(memberAccess.Expression);
                if (peeled is ConditionalExpressionSyntax
                    || peeled is SwitchExpressionSyntax
                    || (peeled is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression)))
                {
                    return memberAccess.Expression;
                }
            }

            return null;
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
