using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests for factory return-path validation and schema export.
    /// </summary>
    public sealed class RouteFactorySchemaTests
    {
        /// <summary>
        /// Verifies that divergent factory return paths report TOP012.
        /// </summary>
        [Fact]
        public async Task Analyzer_DivergentPublicFactoryPaths_ReportsTop012()
        {
            const string source = @"
using TrainOP;

public static class DivergentRoute
{
    public static TrainRoute Build(bool premium) =>
        premium
            ? new TrainRoute().Station(""A"", () => new { paymentId = ""p1"", amount = 1m })
            : new TrainRoute().Station(""B"", () => new { paymentId = ""p2"" });
}";

            var diagnostics = await ChainValidationAnalyzerTests.RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP012");
        }

        /// <summary>
        /// Verifies that the generator emits route schema metadata for public factories.
        /// </summary>
        [Fact]
        public void Generator_EmitsRouteSchema_ForPublicFactory()
        {
            const string source = @"
using TrainOP;

public static class PaymentModule
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var generated = TrainRouteStationGeneratorTestsHelper.RunAllGeneratedSources(source);

            Assert.Contains("[RouteSchemaFor(typeof(global::PaymentModule), \"Build\")]", generated);
            Assert.Contains("[RouteSchemaWagon(\"amount\"", generated);
            Assert.Contains("[RouteSchemaWagon(\"paymentId\"", generated);
            Assert.Contains("TerminalWagons", generated);
        }

        /// <summary>
        /// Verifies cross-assembly schema lookup allows downstream validation without TOP005.
        /// </summary>
        [Fact]
        public async Task Analyzer_CrossAssemblyExtension_UsesExportedSchema()
        {
            const string routeLibSource = @"
using TrainOP;

public static class PaymentModule
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            const string consumerSource = @"
using TrainOP;

public static class AppRoute
{
    public static TrainRoute Build() =>
        PaymentModule.Build()
            .Station(""Finalize"", (decimal amount, string paymentId) =>
                new { paymentId, status = ""completed"" });
}";

            var diagnostics = await RunCrossAssemblyAnalyzerAsync(routeLibSource, consumerSource);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
        }

        internal static async Task<ImmutableArray<Diagnostic>> RunCrossAssemblyAnalyzerAsync(
            string routeLibSource,
            string consumerSource)
        {
            var routeLibTree = CSharpSyntaxTree.ParseText(routeLibSource, path: "RouteLib.cs");
            var routeLibCompilation = CSharpCompilation.Create(
                "RouteLib",
                new[] { routeLibTree },
                ChainValidationAnalyzerTests.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            routeLibCompilation = RunGeneratorOnCompilation(routeLibCompilation, out _);

            var consumerTree = CSharpSyntaxTree.ParseText(consumerSource, path: "Consumer.cs");
            var references = ChainValidationAnalyzerTests.GetMetadataReferences()
                .Concat(new[] { MetadataReference.CreateFromImage(EmitToImage(routeLibCompilation)) })
                .ToArray();

            var consumerCompilation = CSharpCompilation.Create(
                "RouteConsumer",
                new[] { consumerTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ChainValidationAnalyzer());
            return await consumerCompilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        }

        internal static CSharpCompilation RunGeneratorOnCompilation(
            CSharpCompilation compilation,
            out string generated)
        {
            var generators = ImmutableArray.Create<ISourceGenerator>(
                new TrainRouteStationGenerator().AsSourceGenerator());
            var driver = CSharpGeneratorDriver.Create(generators).RunGenerators(compilation);
            generated = string.Join(
                System.Environment.NewLine,
                driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources.Select(source => source.SourceText.ToString())));
            return compilation.AddSyntaxTrees(driver.GetRunResult().GeneratedTrees);
        }

        private static byte[] EmitToImage(Compilation compilation)
        {
            using var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);
            Assert.True(emitResult.Success, string.Join(System.Environment.NewLine, emitResult.Diagnostics));
            return stream.ToArray();
        }
    }

    /// <summary>
    /// Helper exposing generator output for schema tests.
    /// </summary>
    internal static class TrainRouteStationGeneratorTestsHelper
    {
        public static string RunAllGeneratedSources(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "GeneratorSchemaTests",
                new[] { tree },
                ChainValidationAnalyzerTests.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            RouteFactorySchemaTests.RunGeneratorOnCompilation(compilation, out var generated);
            return generated;
        }
    }
}
