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
    /// Stage 6 boundary: <see cref="SchemaDescriptorsStage"/> export descriptor shape.
    /// </summary>
    public sealed class SchemaDescriptorsStageTests
    {
        [Fact]
        public void Collect_PublicFactory_BuildsDescriptorWithTerminalsAndDispatchIdentity()
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

            var result = SchemaDescriptorsStage.Collect(Compile(source));

            Assert.Empty(result.Diagnostics);
            var descriptor = Assert.Single(result.Descriptors);

            Assert.Equal("Build", descriptor.MethodName);
            Assert.Equal("PaymentModule_Build_Schema", descriptor.SchemaTypeName);
            Assert.Contains("PaymentModule", descriptor.OwnerTypeDisplay, StringComparison.Ordinal);
            Assert.True(descriptor.HasDispatchIdentity);
            Assert.False(string.IsNullOrEmpty(descriptor.CallerChainKey));
            Assert.Equal(2, descriptor.StationCount);
            Assert.Equal(TerminalSet.Origin.FactoryPath, descriptor.Terminals.Provenance);
            Assert.False(descriptor.Terminals.HasUnknownReturn);
            Assert.Equal(2, descriptor.TerminalWagons.Length);
            Assert.Contains(descriptor.TerminalWagons, w => w.Name == "paymentId");
            Assert.Contains(descriptor.TerminalWagons, w => w.Name == "amount");
        }

        [Fact]
        public void Collect_PrivateFactory_ReturnsNoDescriptors()
        {
            const string source = @"
using TrainOP;

public static class HiddenModule
{
    private static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var result = SchemaDescriptorsStage.Collect(Compile(source));

            Assert.Empty(result.Descriptors);
        }

        [Fact]
        public void Collect_NullCompilation_ReturnsEmpty()
        {
            var result = SchemaDescriptorsStage.Collect(null);

            Assert.Empty(result.Descriptors);
            Assert.Empty(result.Diagnostics);
        }

        private static Compilation Compile(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\SchemaDescriptors.cs");
            return CSharpCompilation.Create(
                "SchemaDescriptorsStageTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
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
