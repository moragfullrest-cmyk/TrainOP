using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TrainOP.Generators;
using Xunit;

namespace TrainOP.Generators.Tests
{
    public sealed class ChainValidationAnalyzerTests
    {
        [Fact]
        public async Task Analyzer_ValidChain_ProducesNoErrors()
        {
            const string source = @"
using TrainOP;

public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public async Task Analyzer_ReportsTop002_WhenWagonMissing()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"" })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP002");
        }

        [Fact]
        public async Task Analyzer_ReportsTop003_WhenWagonTypeConflicts()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"" })
        .Station(""Bad"", (int paymentId) => new { paymentId });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP003");
        }

        [Fact]
        public async Task Analyzer_ReportsTop004_WhenRemovedWagonRequiredLater()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Partial"", (string paymentId, decimal amount) => new { paymentId })
        .Station(""NeedAmount"", (decimal amount) => new { amount });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP004");
        }

        [Fact]
        public async Task Analyzer_ReportsTop007_WhenHandlerOutsideChain()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return route.Station(""Seed"", () => new { paymentId = ""pay-1"" });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP007");
        }

        [Fact]
        public async Task Analyzer_AllowsStation_AfterAttachRedSignalStation()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            value > 0 ? Data.Ok(new { value }) : Data.Fail(""ERR"", ""bad""))
        .AttachRedSignalStation(""Recovery"", red => RailwaySignals.Green(red.Manifest))
        .Station(""After"", (int value) => new { value = value + 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP007");
        }

        private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "AnalyzerTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ChainValidationAnalyzer());
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
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
