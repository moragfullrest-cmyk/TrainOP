using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests for <see cref="BranchRouteJoinSetFinder"/> join-set discovery under forking receivers.
    /// </summary>
    public sealed class BranchRouteJoinSetFinderTests
    {
        /// <summary>
        /// Ternary receiver of an outer <c>.Station("Join")</c> yields one join set with two branches.
        /// </summary>
        [Fact]
        public void Find_TernaryReceiver_OneJoinSet_TwoBranches()
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

            var joinSets = Find(source);

            Assert.Single(joinSets);
            var joinSet = joinSets[0];
            Assert.Equal("Join", GetDownstreamStationName(joinSet.DownstreamStation));
            Assert.Equal(2, joinSet.Branches.Length);
            Assert.All(joinSet.Branches, b => Assert.True(b.IsResolved));
        }

        /// <summary>
        /// Coalesce receiver of an outer Station yields one join set with two branches.
        /// </summary>
        [Fact]
        public void Find_CoalesceReceiver_OneJoinSet_TwoBranches()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var a = new TrainRoute();
        var b = new TrainRoute();
        return (a ?? b).Station(""Join"", () => new { value = 1 });
    }
}";

            var joinSets = Find(source);

            Assert.Single(joinSets);
            var joinSet = joinSets[0];
            Assert.Equal("Join", GetDownstreamStationName(joinSet.DownstreamStation));
            Assert.Equal(2, joinSet.Branches.Length);
            Assert.All(joinSet.Branches, b => Assert.True(b.IsResolved));
        }

        /// <summary>
        /// Switch expression with three arms yields one join set with three branches.
        /// </summary>
        [Fact]
        public void Find_SwitchThreeArms_OneJoinSet_ThreeBranches()
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

            var joinSets = Find(source);

            Assert.Single(joinSets);
            var joinSet = joinSets[0];
            Assert.Equal("Join", GetDownstreamStationName(joinSet.DownstreamStation));
            Assert.Equal(3, joinSet.Branches.Length);
            Assert.All(joinSet.Branches, b => Assert.True(b.IsResolved));
        }

        /// <summary>
        /// Nested ternary under an outer Station yields one join set with three flattened branches.
        /// </summary>
        [Fact]
        public void Find_NestedTernary_OneJoinSet_ThreeBranches()
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

            var joinSets = Find(source);

            Assert.Single(joinSets);
            var joinSet = joinSets[0];
            Assert.Equal("Join", GetDownstreamStationName(joinSet.DownstreamStation));
            Assert.Equal(3, joinSet.Branches.Length);
            Assert.All(joinSet.Branches, b => Assert.True(b.IsResolved));
        }

        /// <summary>
        /// Two independent ternaries in one method yield two join sets.
        /// </summary>
        [Fact]
        public void Find_TwoIndependentTernaries_TwoJoinSets()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute First(bool useLeft) =>
        (useLeft
            ? new TrainRoute().Station(""L1"", () => new { value = 1 })
            : new TrainRoute().Station(""R1"", () => new { value = 2 }))
        .Station(""Join1"", (int value) => new { value });

    public static TrainRoute Second(bool useLeft) =>
        (useLeft
            ? new TrainRoute().Station(""L2"", () => new { value = 3 })
            : new TrainRoute().Station(""R2"", () => new { value = 4 }))
        .Station(""Join2"", (int value) => new { value });
}";

            var joinSets = Find(source);

            Assert.Equal(2, joinSets.Length);
            var names = joinSets.Select(js => GetDownstreamStationName(js.DownstreamStation)).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "Join1", "Join2" }, names);
            Assert.All(joinSets, js =>
            {
                Assert.Equal(2, js.Branches.Length);
                Assert.All(js.Branches, b => Assert.True(b.IsResolved));
            });
        }

        /// <summary>
        /// Non-forking <c>new TrainRoute().Station</c> yields no join sets.
        /// </summary>
        [Fact]
        public void Find_NonForkingStation_ReturnsEmpty()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() =>
        new TrainRoute().Station(""Seed"", () => new { value = 1 });
}";

            var joinSets = Find(source);

            Assert.Empty(joinSets);
        }

        /// <summary>
        /// <see cref="BranchRouteJoinSetFinder.FromForkReceiver"/> discovers branches and wraps a join set.
        /// </summary>
        [Fact]
        public void FromForkReceiver_WrapsDiscoveredBranches()
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

            var (syntaxTree, model) = Compile(source);
            var forkReceiver = FindFirstForkReceiver(syntaxTree);
            Assert.NotNull(forkReceiver);

            var joinSet = BranchRouteJoinSetFinder.FromForkReceiver(forkReceiver, model);

            Assert.Same(forkReceiver, joinSet.JoinReceiver);
            Assert.Null(joinSet.DownstreamStation);
            Assert.Equal(2, joinSet.Branches.Length);
            Assert.All(joinSet.Branches, b => Assert.True(b.IsResolved));
        }

        private static ImmutableArray<BranchRouteJoinSet> Find(string source)
        {
            var (syntaxTree, model) = Compile(source);
            return BranchRouteJoinSetFinder.Find(syntaxTree, model);
        }

        private static (SyntaxTree SyntaxTree, SemanticModel Model) Compile(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "BranchJoinSetFinderTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return (syntaxTree, compilation.GetSemanticModel(syntaxTree));
        }

        /// <summary>
        /// Locates the forking receiver of the first <c>.Station</c> whose receiver is a ternary / coalesce / switch.
        /// </summary>
        private static ExpressionSyntax FindFirstForkReceiver(SyntaxTree syntaxTree)
        {
            foreach (var invocation in syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
                    || !string.Equals(memberAccess.Name.Identifier.ValueText, TrainRouteMethodNames.Station, StringComparison.Ordinal))
                {
                    continue;
                }

                var peeled = ReceiverExpressionSyntaxPeel.UnwrapTransparent(memberAccess.Expression);
                if (peeled is ConditionalExpressionSyntax
                    || peeled is SwitchExpressionSyntax
                    || (peeled is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression)))
                {
                    return memberAccess.Expression;
                }
            }

            return null;
        }

        private static string GetDownstreamStationName(InvocationExpressionSyntax downstreamStation)
        {
            Assert.NotNull(downstreamStation);
            Assert.NotEmpty(downstreamStation.ArgumentList.Arguments);

            var nameArg = downstreamStation.ArgumentList.Arguments[0].Expression;
            if (nameArg is LiteralExpressionSyntax literal)
            {
                return literal.Token.ValueText;
            }

            return nameArg.ToString().Trim('"');
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
