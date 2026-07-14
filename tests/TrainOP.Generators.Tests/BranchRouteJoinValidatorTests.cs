using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// Tests for <see cref="BranchRouteJoinValidator"/> merge validation at forking receivers.
    /// </summary>
    public sealed class BranchRouteJoinValidatorTests
    {
        /// <summary>
        /// Ternary seed arms with matching wagon types merge successfully with that intersection.
        /// </summary>
        [Fact]
        public void Validate_TernaryMatchingSeedArms_CanMerge_MergedTerminalsMatch()
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

            var validation = ValidateFirstJoin(source);

            Assert.True(validation.CanMerge);
            Assert.Empty(validation.Diagnostics);
            Assert.Single(validation.MergedTerminalWagons);
            Assert.Equal("value", validation.MergedTerminalWagons[0].Name);
            Assert.Equal(SpecialType.System_Int32, validation.MergedTerminalWagons[0].TypeSymbol.SpecialType);
        }

        /// <summary>
        /// Bare <c>new TrainRoute()</c> arms merge with an empty terminal intersection.
        /// </summary>
        [Fact]
        public void Validate_TernaryBareNewArms_CanMerge_EmptyTerminals()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft ? new TrainRoute() : new TrainRoute())
        .Station(""Join"", () => new { value = 1 });
}";

            var validation = ValidateFirstJoin(source);

            Assert.True(validation.CanMerge);
            Assert.Empty(validation.Diagnostics);
            Assert.Empty(validation.MergedTerminalWagons);
        }

        /// <summary>
        /// An unresolved arm (<c>GetRoute()</c>) fails the join with TOP008.
        /// </summary>
        [Fact]
        public void Validate_TernaryUnresolvedArm_CannotMerge_ReportsTop015()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft
            ? new TrainRoute().Station(""Left"", () => new { value = 1 })
            : GetRoute())
        .Station(""Join"", (int value) => new { value });

    private static TrainRoute GetRoute() =>
        new TrainRoute().Station(""Other"", () => new { value = 0 });
}";

            var validation = ValidateFirstJoin(source);

            Assert.False(validation.CanMerge);
            Assert.Empty(validation.MergedTerminalWagons);
            Assert.Contains(validation.Diagnostics, d => d.Id == "TOP008");
            Assert.Contains(
                validation.Diagnostics,
                d => d.GetMessage().Contains("not resolvable", StringComparison.Ordinal));
        }

        /// <summary>
        /// Conflicting types for the same wagon name across arms fail with TOP008.
        /// </summary>
        [Fact]
        public void Validate_TernaryConflictingWagonTypes_CannotMerge_ReportsTop015()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft
            ? new TrainRoute().Station(""Left"", () => new { value = 1 })
            : new TrainRoute().Station(""Right"", () => new { value = ""text"" }))
        .Station(""Join"", (int value) => new { value });
}";

            var validation = ValidateFirstJoin(source);

            Assert.False(validation.CanMerge);
            Assert.Empty(validation.MergedTerminalWagons);
            Assert.Contains(validation.Diagnostics, d => d.Id == "TOP008");
            Assert.Contains(
                validation.Diagnostics,
                d => d.GetMessage().Contains("conflicting types", StringComparison.Ordinal));
        }

        /// <summary>
        /// Arms producing <c>{a}</c> vs <c>{a,b}</c> merge to the intersection <c>{a}</c>.
        /// </summary>
        [Fact]
        public void Validate_TernaryIntersection_OnlySharedWagonNames()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(bool useLeft) =>
        (useLeft
            ? new TrainRoute().Station(""Left"", () => new { a = 1 })
            : new TrainRoute().Station(""Right"", () => new { a = 2, b = 3 }))
        .Station(""Join"", (int a) => new { a });
}";

            var validation = ValidateFirstJoin(source);

            Assert.True(validation.CanMerge);
            Assert.Empty(validation.Diagnostics);
            Assert.Single(validation.MergedTerminalWagons);
            Assert.Equal("a", validation.MergedTerminalWagons[0].Name);
        }

        /// <summary>
        /// <see cref="BranchRouteJoinMerger.TryMerge"/> mirrors <see cref="BranchRouteJoinValidator.Validate"/>.
        /// </summary>
        [Fact]
        public void TryMerge_MatchingArms_ReturnsTrueWithMergedTerminals()
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

            var joinSet = FindFirstJoin(source);
            var merged = BranchRouteJoinMerger.TryMerge(joinSet, out var validation);

            Assert.True(merged);
            Assert.True(validation.CanMerge);
            Assert.Single(validation.MergedTerminalWagons);
        }

        private static BranchRouteJoinValidation ValidateFirstJoin(string source)
        {
            return BranchRouteJoinValidator.Validate(FindFirstJoin(source));
        }

        private static BranchRouteJoinSet FindFirstJoin(string source)
        {
            var (syntaxTree, model) = Compile(source);
            var joinSets = BranchRouteJoinSetFinder.Find(syntaxTree, model);
            Assert.NotEmpty(joinSets);
            return joinSets[0];
        }

        private static (SyntaxTree SyntaxTree, SemanticModel Model) Compile(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "BranchJoinValidatorTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            return (syntaxTree, compilation.GetSemanticModel(syntaxTree));
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
