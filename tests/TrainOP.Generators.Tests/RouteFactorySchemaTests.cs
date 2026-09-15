using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrainOP.Generators.Wagons;
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

            var diagnostics = await TrainRouteValidationAnalyzerTests.RunAnalyzerAsync(source);

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

            Assert.Contains("[RouteSchemaFor(typeof(global::PaymentModule), \"Build\"", generated);
            Assert.Contains("CallerChainKey = \"", generated);
            Assert.Contains("StationCount = 2", generated);
            Assert.Contains("[RouteSchemaWagon(\"amount\"", generated);
            Assert.Contains("[RouteSchemaWagon(\"paymentId\"", generated);
            Assert.Contains("internal static class PaymentModule_Build_Schema { }", generated);
        }

        /// <summary>
        /// Verifies factory terminal schema emits allocated ItemN keys after a default ItemN tuple return.
        /// </summary>
        [Fact]
        public void Generator_EmitsItemNWagonKeys_ForDefaultItemNTupleTerminal()
        {
            const string source = @"
using TrainOP;

public static class PaymentModule
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId + ""-disc"", amount * 0.9m));
}";

            var generated = TrainRouteStationGeneratorTestsHelper.RunAllGeneratedSources(source);

            Assert.Contains("[RouteSchemaWagon(\"Item1\"", generated);
            Assert.Contains("[RouteSchemaWagon(\"Item2\"", generated);
            Assert.DoesNotContain("[RouteSchemaWagon(\"amount\"", generated);
            Assert.DoesNotContain("[RouteSchemaWagon(\"paymentId\"", generated);
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

        /// <summary>
        /// Verifies cross-assembly extension after a default ItemN factory terminal uses ItemN keys.
        /// </summary>
        [Fact]
        public async Task Analyzer_CrossAssemblyExtension_UsesItemNAllocatedTerminalWagons()
        {
            const string routeLibSource = @"
using TrainOP;

public static class PaymentModule
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            (paymentId + ""-disc"", amount * 0.9m));
}";

            const string consumerSource = @"
using TrainOP;

public static class AppRoute
{
    public static TrainRoute Build() =>
        PaymentModule.Build()
            .Station(""Finalize"", (decimal Item2, string Item1) =>
                new { paymentId = Item1, status = ""completed"" });
}";

            var diagnostics = await RunCrossAssemblyAnalyzerAsync(routeLibSource, consumerSource);

            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP005");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP001");
            Assert.DoesNotContain(diagnostics, d => d.Id == "TOP003");
        }

        /// <summary>
        /// Verifies exported schema metadata carries CallerChainKey and StationCount for extension dispatch.
        /// </summary>
        [Fact]
        public void Generator_CrossAssemblySchema_ExposesCallerChainKeyAndStationCount()
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

            var routeLibTree = CSharpSyntaxTree.ParseText(routeLibSource, path: "RouteLib.cs");
            var routeLibCompilation = CSharpCompilation.Create(
                "RouteLibDispatchMeta",
                new[] { routeLibTree },
                TrainRouteValidationAnalyzerTests.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            routeLibCompilation = RunGeneratorOnCompilation(routeLibCompilation, out var generated);
            Assert.Contains("CallerChainKey = \"", generated);
            Assert.Contains("StationCount = 2", generated);

            var image = EmitToImage(routeLibCompilation);
            var consumerCompilation = CSharpCompilation.Create(
                "RouteConsumerDispatchMeta",
                new[] { CSharpSyntaxTree.ParseText("public static class Marker { }", path: "Marker.cs") },
                TrainRouteValidationAnalyzerTests.GetMetadataReferences()
                    .Concat(new[] { MetadataReference.CreateFromImage(image) })
                    .ToArray(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var paymentModule = consumerCompilation.GetTypeByMetadataName("PaymentModule");
            Assert.NotNull(paymentModule);
            var buildMethod = paymentModule.GetMembers("Build").OfType<IMethodSymbol>().Single();

            Assert.True(ExternalRouteSchemaResolver.TryResolve(
                buildMethod,
                consumerCompilation,
                out ExternalRouteSchema schema));
            Assert.True(schema.HasDispatchIdentity);
            Assert.Equal(2, schema.StationCount);
            Assert.Matches("^[0-9a-f]{16}$", schema.CallerChainKey);
        }

        /// <summary>
        /// Verifies data-oriented ServiceStation registrations inside a factory are counted in StationCount
        /// (they call RegisterStation and consume a chain ordinal; builtin RedSignal-only ServiceStation does not).
        /// </summary>
        [Fact]
        public void Generator_FactoryWithServiceStation_EmitsStationCountIncludingServiceStation()
        {
            const string source = @"
using TrainOP;

public static class ServiceFactory
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Bump"", (int id) => new { id = id + 1 })
        .ServiceStation(""Recover"", (ref int id, RedSignal red) => RailwaySignals.White);
}";

            var compilation = CSharpCompilation.Create(
                "ServiceFactoryStationCount",
                new[] { CSharpSyntaxTree.ParseText(source, path: "ServiceFactory.cs") },
                TrainRouteValidationAnalyzerTests.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            RunGeneratorOnCompilation(compilation, out var generated);
            Assert.Contains("StationCount = 3", generated);
            Assert.Contains("CallerChainKey = \"", generated);
        }

        /// <summary>
        /// Verifies a legacy schema without CallerChainKey is treated as having no dispatch identity.
        /// </summary>
        [Fact]
        public void ExternalRouteSchema_EmptyCallerChainKey_HasNoDispatchIdentity()
        {
            var schema = new ExternalRouteSchema(
                ImmutableArray<WagonBinding>.Empty,
                callerChainKey: "",
                stationCount: 2);

            Assert.False(schema.HasDispatchIdentity);
            Assert.Equal(2, schema.StationCount);
            Assert.True(string.IsNullOrEmpty(schema.CallerChainKey));

            var withKey = new ExternalRouteSchema(
                ImmutableArray<WagonBinding>.Empty,
                callerChainKey: "abcd1234abcd1234",
                stationCount: 1);
            Assert.True(withKey.HasDispatchIdentity);
        }

        /// <summary>
        /// Verifies metadata-only factory with a legacy schema (no CallerChainKey) does not invent index-0 bindings.
        /// </summary>
        [Fact]
        public void RouteGraphAssembler_LegacySchemaWithoutCallerChainKey_SkipsConsumerExtensionBindings()
        {
            const string routeLibSource = @"
using TrainOP;

public static class LegacyModule
{
    public static TrainRoute Build() => new TrainRoute()
        .RegisterStation(""Seed"", manifest => manifest.LoadWagon(""id"", 1));
}";

            var routeLibCompilation = CSharpCompilation.Create(
                "LegacyRouteLib",
                new[] { CSharpSyntaxTree.ParseText(routeLibSource, path: "LegacyLib.cs") },
                TrainRouteValidationAnalyzerTests.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            // Intentionally no generator — consumer supplies a legacy schema without CallerChainKey.
            var image = EmitToImage(routeLibCompilation);

            const string consumerSource = @"
using TrainOP;
using System;

[RouteSchemaFor(typeof(LegacyModule), ""Build"")]
[RouteSchemaWagon(""id"", typeof(int))]
internal static class LegacyModule_Build_Schema { }

public static class Consumer
{
    public static TrainRoute Build() => LegacyModule.Build()
        .Station(""Next"", (int id) => new { id });
}";

            var consumerCompilation = CSharpCompilation.Create(
                "LegacyRouteConsumer",
                new[] { CSharpSyntaxTree.ParseText(consumerSource, path: "Consumer.cs") },
                TrainRouteValidationAnalyzerTests.GetMetadataReferences()
                    .Concat(new[] { MetadataReference.CreateFromImage(image) })
                    .ToArray(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var sites = RouteSiteDiscoverer.CollectAll(consumerCompilation);
            var graph = RouteGraphAssembler.Build(sites, consumerCompilation);

            Assert.Empty(
                graph.ChainIndex.Values
                    .SelectMany(x => x)
                    .Where(binding => binding.StationName == "Next"));
        }

        internal static async Task<ImmutableArray<Diagnostic>> RunCrossAssemblyAnalyzerAsync(
            string routeLibSource,
            string consumerSource)
        {
            var routeLibTree = CSharpSyntaxTree.ParseText(routeLibSource, path: "RouteLib.cs");
            var routeLibCompilation = CSharpCompilation.Create(
                "RouteLib",
                new[] { routeLibTree },
                TrainRouteValidationAnalyzerTests.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            routeLibCompilation = RunGeneratorOnCompilation(routeLibCompilation, out _);

            var consumerTree = CSharpSyntaxTree.ParseText(consumerSource, path: "Consumer.cs");
            var references = TrainRouteValidationAnalyzerTests.GetMetadataReferences()
                .Concat(new[] { MetadataReference.CreateFromImage(EmitToImage(routeLibCompilation)) })
                .ToArray();

            var consumerCompilation = CSharpCompilation.Create(
                "RouteConsumer",
                new[] { consumerTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new TrainRouteValidationAnalyzer());
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
                TrainRouteValidationAnalyzerTests.GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            RouteFactorySchemaTests.RunGeneratorOnCompilation(compilation, out var generated);
            return generated;
        }
    }
}
