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
    /// Tests source generation for data-oriented Station and ServiceStation extension methods.
    /// </summary>
    public sealed class TrainRouteStationGeneratorTests
    {
        /// <summary>
        /// Verifies that the generator emits a Station extension for a handler with ref parameters.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForRefHandler()
        {
            const string source = @"
using TrainOP;

public static class RefRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", manifest => manifest.LoadWagon(""paymentId"", ""pay"").LoadWagon(""amount"", 4m))
        .Station(""UpdateRef"", (string paymentId, ref decimal amount) =>
            new { paymentId = paymentId + ""-ref"", amount = amount + 6m });
}";

            var generated = RunGenerators(source);

            Assert.Contains("private static readonly bool[] RefFlags_", generated);
            Assert.Contains("ref global::System.Decimal p1", generated);
            Assert.Contains("refLocalValues", generated);
            Assert.Contains("StationMerge.ToSignal(manifest, stationReturn, stationName", generated);
            Assert.Contains("ReturnMembers_", generated);
        }

        /// <summary>
        /// Verifies that the generator emits a void delegate for handlers without a return value.
        /// </summary>
        [Fact]
        public void Generator_EmitsVoidDelegate_ForVoidHandler()
        {
            const string source = @"
using TrainOP;

public static class VoidRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay"", amount = 4m })
        .Station(""MutateRef"", (ref string paymentId, ref decimal amount) =>
        {
            paymentId = paymentId + ""-void"";
            amount = amount + 1m;
        });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public delegate void TrainStationHandler_", generated);
            Assert.Contains("object stationReturn = null;", generated);
            Assert.Contains("StationMerge.ToSignal(manifest, stationReturn, stationName", generated);
            Assert.Contains("RefFlags_", generated);
            Assert.Contains("refLocalValues", generated);
        }

        /// <summary>
        /// Verifies that the generator emits HasWagon checks for optional wagon parameters.
        /// </summary>
        [Fact]
        public void Generator_EmitsHasWagon_ForOptionalWagon()
        {
            const string source = @"
using TrainOP;

public static class OptionalRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay"" })
        .Station(""WithOptional"", (string paymentId, decimal? amount) =>
            new { paymentId, amount = amount ?? 0m });
}";

            var generated = RunGenerators(source);

            Assert.Contains("manifest.HasWagon(\"amount\")", generated);
            Assert.Contains("default(", generated);
            Assert.Contains("global::System.Decimal?", generated);
        }

        /// <summary>
        /// Verifies that the generator emits a ServiceStation extension for a data-oriented recovery handler.
        /// </summary>
        [Fact]
        public void Generator_EmitsServiceStationExtension_ForDataHandler()
        {
            const string source = @"
using TrainOP;

public static class RecoveryRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) =>
            value > 0 ? RailwaySignals.Green(new { value }) : RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (int value, SignalIssue issue) =>
            issue.Code == ""ERR"" ? RailwaySignals.Green(new { value = 1 }) : RailwaySignals.Red(""NOPE"", ""skip""))
        .Station(""After"", (int value) => new { value = value + 1 });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute ServiceStation(this TrainRoute route", generated);
            Assert.Contains("TrainServiceStationHandler_", generated);
            Assert.Contains("red.Manifest", generated);
            Assert.Contains("red.Issue", generated);
            Assert.Contains("StationMerge.ToSignal", generated);
        }

        /// <summary>
        /// Verifies that the generator does not emit extensions for a built-in red-signal ServiceStation handler.
        /// </summary>
        [Fact]
        public void Generator_DoesNotEmit_ForBuiltinRedSignalServiceStation()
        {
            const string source = @"
using System;
using TrainOP;

public static class LongRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { orderId = ""ORD-1"", amount = 200m, units = 12 })
        .Station(""CheckStock"", (string orderId, decimal amount, int units) =>
            units <= 10
                ? RailwaySignals.Green(new { orderId, amount, units })
                : RailwaySignals.Red(""STOCK_LIMIT"", ""too many""))
        .ServiceStation(""TerminalLogger"", red =>
        {
            var issue = red.Issue;
            var orderId = red.Manifest.PullWagon<string>(""orderId"");
            return RailwaySignals.Green(red.Manifest.LoadWagon(""units"", 10));
        });
}";

            var generated = RunGenerators(source);

            Assert.DoesNotContain("new string[] { \"red\" }", generated);
            Assert.DoesNotContain("PullWagon<>(\"red\")", generated);
            Assert.DoesNotContain("TrainServiceStationHandler_", generated);
        }

        /// <summary>
        /// Verifies that the generator emits a ServiceStation extension for a SignalIssue-only handler.
        /// </summary>
        [Fact]
        public void Generator_EmitsServiceStationExtension_ForSignalIssueOnlyHandler()
        {
            const string source = @"
using TrainOP;

public static class IssueRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { value = 0 })
        .Station(""Validate"", (int value) => RailwaySignals.Red(""ERR"", ""bad""))
        .ServiceStation(""Recovery"", (SignalIssue issue) =>
            RailwaySignals.Red(""CANNOT_RECOVER"", issue.Code));
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static TrainRoute ServiceStation(this TrainRoute route", generated);
            Assert.Contains("TrainServiceStationHandler_", generated);
            Assert.Contains("red.Issue", generated);
            Assert.DoesNotContain("new string[] { \"issue\" }", generated);
        }

        /// <summary>
        /// Verifies that the generator emits a Station extension for a data-oriented handler with tuple return.
        /// </summary>
        [Fact]
        public void Generator_EmitsStationExtension_ForDataHandler()
        {
            const string source = @"
using TrainOP;

public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId, amount: amount * 0.9m));
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static class TrainRouteStationExtensions", generated);
            Assert.Contains("delegate object TrainStationHandler_", generated);
            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
            Assert.Contains("manifest.PullWagon<global::System.Decimal>(\"amount\")", generated);
            Assert.Contains("StationMerge.ToSignal", generated);
            Assert.Contains("ReturnMembers_", generated);
        }

        /// <summary>
        /// Verifies that the generator emits ReturnMembers with ItemN names for an unnamed tuple return.
        /// </summary>
        [Fact]
        public void Generator_EmitsItemNames_ForUnnamedTupleReturn()
        {
            const string source = @"
using TrainOP;

public static class PaymentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId, amount * 0.9m));
}";

            var generated = RunGenerators(source);

            Assert.Contains("\"Item1\"", generated);
            Assert.Contains("\"Item2\"", generated);
            Assert.DoesNotContain("TupleOrdinals_", generated);
        }

        /// <summary>
        /// Verifies that a single Station overload is emitted when handlers share a type signature but differ in parameter names.
        /// </summary>
        [Fact]
        public void Generator_EmitsSingleStationOverload_WhenHandlersShareTypeSignatureButDifferInParameterNames()
        {
            const string source = @"
using TrainOP;

public static class MixedNameRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Validate"", (string orderId, decimal total) =>
            new { paymentId = orderId, amount = total + 1m });
}";

            var generated = RunGenerators(source);
            var stationOverloadCount = 0;
            var index = 0;
            while ((index = generated.IndexOf("public static TrainRoute Station(this TrainRoute route", index, StringComparison.Ordinal)) >= 0)
            {
                stationOverloadCount++;
                index++;
            }

            Assert.Equal(2, stationOverloadCount);
            Assert.Contains("global::System.String p0", generated);
            Assert.Contains("manifest.PullWagon<global::System.String>(\"paymentId\")", generated);
        }

        /// <summary>
        /// Verifies that TOP008 is reported when handlers share a type signature but use different manifest keys.
        /// </summary>
        [Fact]
        public void Generator_ReportsConflictingWagonNames_WhenHandlersShareTypeSignatureButUseDifferentManifestKeys()
        {
            const string source = @"
using TrainOP;

public static class ConflictingNameRoute
{
    public static TrainRoute Payment() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });

    public static TrainRoute Order() => new TrainRoute()
        .Station(""Seed"", () => new { orderId = ""ord-1"", total = 50m })
        .Station(""Validate"", (string orderId, decimal total) =>
            new { orderId, total = total + 1m });
}";

            var runResult = RunGeneratorDriver(source);
            var diagnostics = runResult.Results
                .SelectMany(result => result.Diagnostics)
                .ToList();

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "TOP008");
        }

        /// <summary>
        /// Verifies that void and object-return handlers with the same wagon signature get separate Station overloads.
        /// </summary>
        [Fact]
        public void Generator_EmitsSeparateStationOverloads_WhenHandlersShareSignatureButDifferInVoidReturn()
        {
            const string source = @"
using TrainOP;

public static class VoidAndObjectReturnRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Anonymous"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Void"", (string paymentId, decimal amount) =>
        {
        });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public delegate object TrainStationHandler_", generated);
            Assert.Contains("public delegate void TrainStationHandler_", generated);
            Assert.Equal(3, CountStationOverloads(generated));
        }

        /// <summary>
        /// Verifies that a single Station overload is emitted when handlers share a signature but differ in return shape.
        /// </summary>
        [Fact]
        public void Generator_EmitsSingleStationOverload_WhenHandlersShareSignatureButDifferInReturnShape()
        {
            const string source = @"
using TrainOP;

public static class MixedReturnRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Anonymous"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Tuple"", (string paymentId, decimal amount) =>
            (paymentId, amount: amount + 1m));
}";

            var generated = RunGenerators(source);

            Assert.Equal(2, CountStationOverloads(generated));
            Assert.Contains("ReturnMembers_", generated);
        }

        private static int CountStationOverloads(string generated)
        {
            var stationOverloadCount = 0;
            var index = 0;
            while ((index = generated.IndexOf("public static TrainRoute Station(this TrainRoute route", index, StringComparison.Ordinal)) >= 0)
            {
                stationOverloadCount++;
                index++;
            }

            return stationOverloadCount;
        }

        private static string RunGenerators(string source)
        {
            return string.Join(
                Environment.NewLine + "-----" + Environment.NewLine,
                RunGeneratorDriver(source).Results
                    .SelectMany(x => x.GeneratedSources)
                    .Select(x => x.SourceText.ToString()));
        }

        private static GeneratorDriverRunResult RunGeneratorDriver(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "GeneratorTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generators = new ISourceGenerator[]
            {
                new TrainRouteStationGenerator().AsSourceGenerator(),
            };

            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators);
            driver = driver.RunGenerators(compilation);

            return driver.GetRunResult();
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
