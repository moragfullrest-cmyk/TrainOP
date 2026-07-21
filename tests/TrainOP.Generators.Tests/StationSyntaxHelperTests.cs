using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Handlers;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests station invocation parsing in <see cref="StationSyntaxHelper"/>.
    /// </summary>
    public sealed class StationSyntaxHelperTests
    {
        [Fact]
        public void try_get_data_station_invocation_recognizes_const_station_name()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    private const string ValidateStation = ""Validate"";
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { x = 1 })
        .Station(ValidateStation, (int x) => new { x = x + 1 });
}";

            TryGetSecondStation(source, out var invocation, out var model);

            var ok = StationSyntaxHelper.TryGetDataStationInvocation(
                invocation,
                model,
                out var stationName,
                out _,
                out _);

            Assert.True(ok);
            Assert.Equal("Validate", stationName);
        }

        [Fact]
        public void try_get_data_station_invocation_recognizes_local_with_literal_initializer()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var validateStation = ""Validate"";
        return new TrainRoute()
            .Station(""Seed"", () => new { x = 1 })
            .Station(validateStation, (int x) => new { x = x + 1 });
    }
}";

            TryGetSecondStation(source, out var invocation, out var model);

            var ok = StationSyntaxHelper.TryGetDataStationInvocation(
                invocation,
                model,
                out var stationName,
                out _,
                out _);

            Assert.True(ok);
            Assert.Equal("Validate", stationName);
        }

        [Fact]
        public void try_get_data_station_invocation_recognizes_readonly_field_initializer()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    private static readonly string ValidateStation = ""Validate"";
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { x = 1 })
        .Station(ValidateStation, (int x) => new { x = x + 1 });
}";

            TryGetSecondStation(source, out var invocation, out var model);

            var ok = StationSyntaxHelper.TryGetDataStationInvocation(
                invocation,
                model,
                out var stationName,
                out _,
                out _);

            Assert.True(ok);
            Assert.Equal("Validate", stationName);
        }

        [Fact]
        public void try_get_data_station_invocation_recognizes_runtime_parameter()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build(string stationName) => new TrainRoute()
        .Station(""Seed"", () => new { x = 1 })
        .Station(stationName, (int x) => new { x = x + 1 });
}";

            TryGetSecondStation(source, out var invocation, out var model);

            var ok = StationSyntaxHelper.TryGetDataStationInvocation(
                invocation,
                model,
                out var stationName,
                out _,
                out _);

            Assert.True(ok);
            Assert.Equal("stationName", stationName);
        }

        private static void TryGetSecondStation(
            string source,
            out InvocationExpressionSyntax invocation,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "StationSyntaxHelperTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            invocation = tree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(StationSyntaxHelper.IsCandidateStationInvocation)
                .First();
        }

        private static MetadataReference[] GetMetadataReferences()
        {
            var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            return new[]
            {
                MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Private.CoreLib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TrainOP.CargoManifest).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)
            };
        }
    }
}
