using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests source generation for typed Travel and RouteReport deconstruction extensions.
    /// </summary>
    public sealed class TrainRouteTravelGeneratorTests
    {
        /// <summary>
        /// Verifies that the generator emits deconstruct extensions for a sample-style route with data-fail validation.
        /// </summary>
        [Fact]
        public void Generator_Emits_ForSampleStyleRouteWithDataFail()
        {
            const string source = @"
using TrainOP;

namespace TrainOP.Samples;

internal sealed class DataOrientedStationExample
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""data-route"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Validate"", (string paymentId, decimal amount) =>
            amount > 0
                ? RailwaySignals.Green(new { paymentId, amount })
                : RailwaySignals.Red(""INVALID_TOTAL"", ""amount must be positive""));
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static class TrainRouteTravelExtensions", generated);
            Assert.Contains("out global::System.String paymentId", generated);
            Assert.Contains("out global::System.Decimal amount", generated);
        }

        /// <summary>
        /// Verifies that the generator emits RouteReport Deconstruct for a data-oriented route chain.
        /// </summary>
        [Fact]
        public void Generator_EmitsRouteReportDeconstruct_ForDataOrientedChain()
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

            var generated = RunGenerators(source);

            Assert.Contains("public static class TrainRouteTravelExtensions", generated);
            Assert.Contains("public static void Deconstruct(", generated);
            Assert.Contains("this RouteReport report,", generated);
            Assert.Contains("out global::System.String paymentId", generated);
            Assert.Contains("out global::System.Decimal amount", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
        }

        /// <summary>
        /// Verifies that ambiguous deconstruct overloads are skipped when schemas share arity across routes.
        /// </summary>
        [Fact]
        public void Generator_SkipsVarAmbiguousDeconstruct_WhenSchemasShareArity()
        {
            const string source = @"
using TrainOP;

public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}

public static class OtherRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""other"", traceId = ""t"" });
}";

            var generated = RunGenerators(source);

            Assert.Contains("out global::System.String paymentId", generated);
            Assert.Contains("out global::System.Decimal amount", generated);
            Assert.DoesNotContain("out global::System.String traceId", generated);
        }

        /// <summary>
        /// Verifies that terminal wagon deconstruct omits wagons removed by a partial return.
        /// </summary>
        [Fact]
        public void Generator_EmitsTerminalWagons_AfterPartialReturn()
        {
            const string source = @"
using TrainOP;

public static class PartialRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-partial"", amount = 3m, traceId = ""keep"" })
        .Station(""Partial"", (string paymentId, decimal amount) =>
            new { paymentId = paymentId + ""-merged"" });
}";

            var generated = RunGenerators(source);

            Assert.Contains("out global::System.String paymentId", generated);
            Assert.Contains("out global::System.String traceId", generated);
            Assert.DoesNotContain("out global::System.Decimal amount", generated);
        }

        private static string RunGenerators(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "TravelGeneratorTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generators = new ISourceGenerator[]
            {
                new TrainRouteStationGenerator().AsSourceGenerator(),
                new TrainRouteTravelGenerator().AsSourceGenerator(),
            };

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);
            driver = driver.RunGenerators(compilation);

            return string.Join(
                Environment.NewLine + "-----" + Environment.NewLine,
                driver.GetRunResult().Results
                    .SelectMany(x => x.GeneratedSources)
                    .Select(x => x.SourceText.ToString()));
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
