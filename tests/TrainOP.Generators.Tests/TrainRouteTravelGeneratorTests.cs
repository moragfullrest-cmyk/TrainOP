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

    /// Tests source generation for travel generator compatibility output.

    /// </summary>

    public sealed class TrainRouteTravelGeneratorTests

    {

        /// <summary>

        /// Verifies that travel generator does not emit RouteReport wrappers.

        /// </summary>

        [Fact]

        public void Generator_DoesNotEmitRouteTerminalWrappers()

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



            Assert.Contains("internal static class TrainRouteTravelGeneratorMarker", generated);
            Assert.DoesNotContain("RouteTerminal_", generated);
            Assert.DoesNotContain("TrainRouteTerminalTravelExtensions", generated);
            Assert.DoesNotContain("Travel<TTerminal>", generated);

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


