using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests diagnostic output from <see cref="ChainValidationAnalyzer"/> on route chains.
    /// </summary>
    public sealed class ChainValidationAnalyzerTests
    {
        /// <summary>
        /// Verifies that a valid route chain produces no analyzer errors.
        /// </summary>
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

        /// <summary>
        /// Verifies that the first station is allowed when wagons come from an external Travel manifest.
        /// </summary>
        [Fact]
        public async Task Analyzer_AllowsFirstStation_WhenWagonsComeFromExternalTravelManifest()
        {
            const string source = @"
using TrainOP;

public static class ExternalSeedRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Double"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 2m });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP002");
        }

        /// <summary>
        /// Verifies that TOP002 is reported when a required wagon is missing from the chain.
        /// </summary>
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

        /// <summary>
        /// Verifies that TOP003 is reported when a wagon type conflicts with a prior station.
        /// </summary>
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

        /// <summary>
        /// Verifies that TOP004 is reported when a removed wagon is required by a later station.
        /// </summary>
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

        /// <summary>
        /// Verifies that TOP007 is reported when a handler is defined outside a fluent chain.
        /// </summary>
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

        /// <summary>
        /// Verifies that a station after a service station is allowed in the chain.
        /// </summary>
        [Fact]
        public async Task Analyzer_AllowsStation_AfterServiceStation()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            value > 0 ? RailwaySignals.Green(new { value }) : RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (int value) => RailwaySignals.Green(new { value }))
        .Station(""After"", (int value) => new { value = value + 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP007");
        }

        /// <summary>
        /// Verifies that a red return does not flag unreachable stations for removed wagon diagnostics.
        /// </summary>
        [Fact]
        public async Task Analyzer_RedReturn_DoesNotRemoveWagonsForUnreachableStation()
        {
            const string source = @"
using TrainOP;

public static class FailRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            RailwaySignals.Red(""ERR"", ""bad""))
        .Station(""MustNotRun"", (int value) => new { value });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that TOP006 is reported when a handler returns an unnamed tuple.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop006_WhenHandlerReturnsUnnamedTuple()
        {
            const string source = @"
using TrainOP;

public static class TupleRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""ByTuple"", (string paymentId, decimal amount) =>
            (paymentId + ""-tuple"", amount + 1m));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP006");
        }

        /// <summary>
        /// Verifies that TOP006 is not reported when a handler returns a named tuple.
        /// </summary>
        [Fact]
        public async Task Analyzer_DoesNotReportTop006_WhenHandlerReturnsNamedTuple()
        {
            const string source = @"
using TrainOP;

public static class TupleRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""ByTuple"", (string paymentId, decimal amount) =>
            (paymentId: paymentId + ""-tuple"", amount: amount + 1m));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
        }

        /// <summary>
        /// Verifies that TOP006 is not reported when tuple element names are inferred from identifiers.
        /// </summary>
        [Fact]
        public async Task Analyzer_DoesNotReportTop006_WhenTupleElementNamesAreInferredFromIdentifiers()
        {
            const string source = @"
using TrainOP;

public static class TupleRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""ByTuple"", (string paymentId, decimal amount) =>
            (paymentId, amount));
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
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
