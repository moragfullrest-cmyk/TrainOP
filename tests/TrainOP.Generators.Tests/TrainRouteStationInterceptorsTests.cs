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
    /// Tests Roslyn interceptor emission for chain-dispatched legacy station call sites.
    /// </summary>
    public sealed class TrainRouteStationInterceptorsTests
    {
        /// <summary>
        /// Verifies that interceptors are emitted in a dedicated generated source file.
        /// </summary>
        [Fact]
        public void Generator_EmitsDedicatedInterceptorsSourceFile()
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

            var interceptors = GetInterceptorsSource(source);

            Assert.Contains("namespace TrainOP.Generated", interceptors);
            Assert.Contains("internal static class TrainRouteStationInterceptors", interceptors);
            Assert.Contains("file sealed class InterceptsLocationAttribute", interceptors);
            Assert.Contains("TrainRouteStationExtensions.StationCore_", interceptors);
        }

        /// <summary>
        /// Verifies that interceptors are not emitted when handlers share one wagon-name set.
        /// </summary>
        [Fact]
        public void Generator_DoesNotEmitInterceptors_WhenHandlersUseConsistentWagonNames()
        {
            const string source = @"
using TrainOP;

public static class ConsistentRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Double"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 2m });
}";

            var runResult = RunGeneratorDriver(source);
            var interceptors = GetInterceptorsSource(runResult);

            Assert.DoesNotContain("InterceptsLocation", interceptors);
            Assert.DoesNotContain("class TrainRouteStationInterceptors", interceptors);
        }

        /// <summary>
        /// Verifies that each legacy station in separate chains gets an interceptor forwarding to StationCore_.
        /// </summary>
        [Fact]
        public void Generator_EmitsInterceptors_ForEachLegacyStationInSeparateChains()
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

            var interceptors = GetInterceptorsSource(source);

            Assert.Equal(2, CountInterceptorAttributes(interceptors));
            Assert.Contains("ConflictingNameRoute.Payment", interceptors);
            Assert.Contains("ConflictingNameRoute.Order", interceptors);
            Assert.Equal(2, CountOccurrences(", 1);", interceptors));
            Assert.Contains("StationCore_", interceptors);
        }

        /// <summary>
        /// Verifies that interceptors use opaque InterceptsLocation data from Roslyn.
        /// </summary>
        [Fact]
        public void Generator_EmitsInterceptors_WithInterceptableLocationData()
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

            var interceptors = GetInterceptorsSource(source);

            Assert.Contains("InterceptsLocation(1, \"", interceptors);
            Assert.Contains("Station_0", interceptors);
        }

        /// <summary>
        /// Verifies that interceptors are emitted for void handlers in separate chains.
        /// </summary>
        [Fact]
        public void Generator_EmitsInterceptors_ForVoidHandlersInSeparateChains()
        {
            const string source = @"
using TrainOP;

public static class VoidChainRoute
{
    public static TrainRoute Payment() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Touch"", (string paymentId, decimal amount) => { });

    public static TrainRoute Order() => new TrainRoute()
        .Station(""Seed"", () => new { orderId = ""ord-1"", total = 50m })
        .Station(""Touch"", (string orderId, decimal total) => { });
}";

            var interceptors = GetInterceptorsSource(source);

            Assert.Equal(2, CountInterceptorAttributes(interceptors));
            Assert.Contains("Action<global::System.String, global::System.Decimal>", interceptors);
            Assert.Contains("StationCore_", interceptors);
        }

        /// <summary>
        /// Verifies that interceptors are emitted for mixed wagon names within one chain.
        /// </summary>
        [Fact]
        public void Generator_EmitsInterceptors_ForMixedWagonNamesInOneChain()
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

            var interceptors = GetInterceptorsSource(source);

            Assert.Equal(2, CountInterceptorAttributes(interceptors));
            Assert.Contains("MixedNameRoute.Build", interceptors);
            Assert.Contains(", 1);", interceptors);
            Assert.Contains(", 2);", interceptors);
        }

        /// <summary>
        /// Verifies that local-variable route chains get distinct chain ids in interceptor forwarding.
        /// </summary>
        [Fact]
        public void Generator_EmitsInterceptors_WithDistinctChainIds_ForLocalVariableChains()
        {
            const string source = @"
using TrainOP;

public static class LocalChainRoute
{
    public static TrainRoute Build()
    {
        var payment = new TrainRoute()
            .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
            .Station(""Discount"", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });

        var order = new TrainRoute()
            .Station(""Seed"", () => new { orderId = ""ord-1"", total = 50m })
            .Station(""Validate"", (string orderId, decimal total) =>
                new { orderId, total = total + 1m });

        return payment;
    }
}";

            var interceptors = GetInterceptorsSource(source);

            Assert.Contains("LocalChainRoute.Build@payment", interceptors);
            Assert.Contains("LocalChainRoute.Build@order", interceptors);
            Assert.Equal(2, CountInterceptorAttributes(interceptors));
        }

        /// <summary>
        /// Verifies that generated interceptors null-check route and handler arguments.
        /// </summary>
        [Fact]
        public void Generator_EmitsInterceptors_WithNullChecks()
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

            var interceptors = GetInterceptorsSource(source);

            Assert.Contains("throw new ArgumentNullException(nameof(route));", interceptors);
            Assert.Contains("throw new ArgumentNullException(nameof(handler));", interceptors);
        }

        /// <summary>
        /// Verifies reflection mode emits ParameterInfo binding and skips interceptors.
        /// </summary>
        [Fact]
        public void Generator_EmitsReflectionBinding_WhenChainDispatchModeIsReflection()
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

            var runResult = RunGeneratorDriver(source, mode: "reflection");
            var extensions = GetExtensionsSource(runResult);
            var interceptors = GetInterceptorsSource(runResult);

            Assert.Contains("StationHandlerParameterNames.GetWagonInputNames", extensions);
            Assert.DoesNotContain("StationCore_", extensions);
            Assert.DoesNotContain("InterceptsLocation", interceptors);
            Assert.DoesNotContain("class TrainRouteStationInterceptors", interceptors);
        }

        private static string GetInterceptorsSource(string source, string path = "Test0.cs")
        {
            return GetInterceptorsSource(RunGeneratorDriver(source, path));
        }

        private static string GetExtensionsSource(GeneratorDriverRunResult runResult)
        {
            foreach (var generatedSource in runResult.Results.SelectMany(result => result.GeneratedSources))
            {
                if (generatedSource.HintName.Contains("Extensions", StringComparison.Ordinal))
                {
                    return generatedSource.SourceText.ToString();
                }
            }

            return string.Empty;
        }

        private static string GetInterceptorsSource(GeneratorDriverRunResult runResult)
        {
            foreach (var generatedSource in runResult.Results.SelectMany(result => result.GeneratedSources))
            {
                if (generatedSource.HintName.Contains("Interceptors", StringComparison.Ordinal))
                {
                    return generatedSource.SourceText.ToString();
                }
            }

            return string.Empty;
        }

        private static int CountOccurrences(string needle, string haystack)
        {
            var count = 0;
            var index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static int CountInterceptorAttributes(string interceptorsSource)
        {
            return CountOccurrences("InterceptsLocation(1,", interceptorsSource);
        }

        private static GeneratorDriverRunResult RunGeneratorDriver(
            string source,
            string path = "Test0.cs",
            string mode = "stable")
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: path);
            var compilation = CSharpCompilation.Create(
                "InterceptorGeneratorTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generators = new ISourceGenerator[]
            {
                new TrainRouteStationGenerator().AsSourceGenerator(),
            };

            var namespaces = string.Equals(mode, "reflection", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : "TrainOP.Generated";
            var optionsProvider = TestAnalyzerConfigOptionsProvider.ForChainDispatchMode(
                mode,
                interceptorsNamespaces: namespaces);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators,
                additionalTexts: null,
                parseOptions: null,
                optionsProvider: optionsProvider);
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
