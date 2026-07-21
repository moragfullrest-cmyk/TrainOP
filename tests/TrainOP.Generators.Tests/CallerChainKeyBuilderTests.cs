using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    public sealed class CallerChainKeyBuilderTests
    {
        [Fact]
        public void CallerChainKeyBuilder_ObjectCreation_UsesFileLineMemberFromLocation()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m });
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\Test0.cs");
            var compilation = CSharpCompilation.Create(
                "CallerChainKeyBuilderTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            var methodDecl = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "Build");

            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl) as IMethodSymbol;
            Assert.NotNull(methodSymbol);

            var objectCreation = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(n => n.Type is IdentifierNameSyntax id && id.Identifier.ValueText == "TrainRoute");

            var anchor = new RouteChainAnchor(
                RouteChainAnchorKind.ObjectCreation,
                objectCreation,
                objectCreation.GetLocation(),
                methodSymbol);

            var actual = CallerChainKeyBuilder.Build(anchor);

            var lineNumber = objectCreation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var expected = CallerChainKeyFormat.Build(@"C:\repo\Test0.cs", lineNumber, "Build");

            Assert.Equal(expected, actual);
            Assert.Matches("^[0-9a-f]{16}$", actual);
        }

        [Fact]
        public void CallerChainKeyBuilder_UsesDistinctHashes_ForSameFileNameInDifferentFolders()
        {
            var lineNumber = 10;
            var memberName = "Build";

            var left = CallerChainKeyFormat.Build(@"C:\repo\alpha\Route.cs", lineNumber, memberName);
            var right = CallerChainKeyFormat.Build(@"C:\repo\beta\Route.cs", lineNumber, memberName);

            Assert.NotEqual(left, right);
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

