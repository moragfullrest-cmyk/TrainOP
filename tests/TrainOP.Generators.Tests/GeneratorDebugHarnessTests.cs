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
    /// In-process generator runs for debugger breakpoints (reliable alternative to Rebuild Samples).
    /// </summary>
    public sealed class GeneratorDebugHarnessTests
    {
        /// <summary>
        /// Runs the generator on <c>GeneratorDebugCoverageExample.cs</c>.
        /// Set breakpoints in <c>TrainRouteStationGenerator</c>, then Debug Test (not Rebuild Samples).
        /// </summary>
        [Fact]
        public void DebugHarness_RunsGenerator_OnCoverageExampleSource()
        {
            var sourcePath = ResolveCoverageExamplePath();
            Assert.True(File.Exists(sourcePath), "Coverage example not found: " + sourcePath);

            var source = File.ReadAllText(sourcePath);
            var generated = RunGenerators(source);

            Assert.Contains("class TrainRouteStationExtensions", generated);
            Assert.Contains("ResolveChainBinding_", generated);
        }

        private static string ResolveCoverageExamplePath()
        {
            var fromTestProject = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "samples", "TrainOP.Samples", "Examples", "GeneratorDebugCoverageExample.cs"));

            if (File.Exists(fromTestProject))
            {
                return fromTestProject;
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(
                    dir.FullName,
                    "samples",
                    "TrainOP.Samples",
                    "Examples",
                    "GeneratorDebugCoverageExample.cs");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return fromTestProject;
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
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: "GeneratorDebugCoverageExample.cs");
            var compilation = CSharpCompilation.Create(
                "GeneratorDebugHarness",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generators = new ISourceGenerator[]
            {
                new TrainRouteStationGenerator().AsSourceGenerator(),
            };

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators,
                additionalTexts: null,
                parseOptions: null);
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
