using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TrainOP.Generators;
using Xunit;

namespace TrainOP.Generators.Tests
{
    public sealed class TrainRouteStationGeneratorTests
    {
        [Fact]
        public void Generator_EmitsStationExtension_ForRefHandler()
        {
            const string source = @"
using TrainOP;

public static class RefRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", manifest => manifest.LoadCar(""paymentId"", ""pay"").LoadCar(""amount"", 4m))
        .Station(""UpdateRef"", (string paymentId, ref decimal amount) =>
            new { paymentId = paymentId + ""-ref"", amount = amount + 6m });
}";

            var generated = RunGenerators(source);

            Assert.Contains("private static readonly bool[] RefFlags_", generated);
            Assert.Contains("ref decimal amount", generated);
            Assert.Contains("refLocalValues", generated);
            Assert.Contains("StationMerge.ToSignal(manifest, stationReturn, stationName", generated);
        }

        [Fact]
        public void Generator_EmitsHasCar_ForOptionalWagon()
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

            Assert.Contains("manifest.HasCar(\"amount\")", generated);
            Assert.Contains("default(", generated);
            Assert.Contains("decimal?", generated);
        }

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
            new { paymentId, amount = amount * 0.9m });
}";

            var generated = RunGenerators(source);

            Assert.Contains("public static class TrainRouteStationExtensions", generated);
            Assert.Contains("delegate object TrainStationHandler_", generated);
            Assert.Contains("public static TrainRoute Station(this TrainRoute route", generated);
            Assert.Contains("manifest.PullCar<string>(\"paymentId\")", generated);
            Assert.Contains("manifest.PullCar<decimal>(\"amount\")", generated);
            Assert.Contains("StationMerge.ToSignal", generated);
        }

        [Fact]
        public void Generator_DoesNotEmit_ForManifestOnlyStation()
        {
            const string source = @"
using TrainOP;

public static class Flow
{
    public static TrainRoute Build() => new TrainRoute()
        .AttachStation(""Seed"", manifest => manifest.LoadCar(""paymentId"", ""x""));
}";

            var generated = RunGenerators(source);
            Assert.DoesNotContain("TrainRouteStationExtensions", generated);
        }

        private static string RunGenerators(string source)
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
