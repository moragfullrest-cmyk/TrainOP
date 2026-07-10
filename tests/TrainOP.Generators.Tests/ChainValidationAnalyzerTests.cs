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

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
        }

        /// <summary>
        /// Verifies that TOP001 is reported when a required wagon is missing from the chain.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop001_WhenWagonMissing()
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

            Assert.Contains(diagnostics, d => d.Id == "TOP001");
        }

        /// <summary>
        /// Verifies that TOP002 is reported when a wagon type conflicts with a prior station.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop002_WhenWagonTypeConflicts()
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

            Assert.Contains(diagnostics, d => d.Id == "TOP002");
        }

        /// <summary>
        /// Verifies that TOP003 is reported when a removed wagon is required by a later station.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop003_WhenRemovedWagonRequiredLater()
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

            Assert.Contains(diagnostics, d => d.Id == "TOP003");
        }

        /// <summary>
        /// Verifies that a route built via a local variable is recognized as a valid chain.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return route.Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that a local variable with explicit type is recognized as a chain anchor.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_WithExplicitType_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        TrainRoute route = new TrainRoute();
        route.Station(""Seed"", () => new { paymentId = ""pay-1"" });
        return route;
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
        }

        /// <summary>
        /// Verifies that a reused local variable is anchored to its latest preceding assignment.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_ReuseAfterReassignment_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route = new TrainRoute();
        return route.Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Verifies that two route chains can be built by reusing the same local variable.
        /// </summary>
        [Fact]
        public async Task Analyzer_LocalVariableChain_TwoRoutesInOneMethod_ProducesNoTop006()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static void BuildBoth()
    {
        var route = new TrainRoute();
        route.Station(""First"", () => new { paymentId = ""pay-1"" });

        route = new TrainRoute();
        route.Station(""Second"", () => new { paymentId = ""pay-2"" });
    }
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
        }

        /// <summary>
        /// Verifies that TOP006 is reported when a local is assigned from a non-creation source.
        /// </summary>
        [Fact]
        public async Task Analyzer_ReportsTop006_WhenLocalIsNotAssignedFromCreation()
        {
            const string source = @"
using TrainOP;

public static class BrokenRoute
{
    public static TrainRoute Build()
    {
        var route = GetRoute();
        return route.Station(""Seed"", () => new { paymentId = ""pay-1"" });
    }

    private static TrainRoute GetRoute() => new TrainRoute();
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.Contains(diagnostics, d => d.Id == "TOP006");
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
        .ServiceStation(""Recovery"", (ref int value, RedSignal red) => RailwaySignals.Pass)
        .Station(""After"", (int value) => new { value = value + 1 });
}";

            var diagnostics = await RunAnalyzerAsync(source);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP006");
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
