using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Parts;
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

            var model = compilation.GetSemanticModel(syntaxTree);
            var objectCreation = syntaxTree.GetRoot().DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(n => n.Type is IdentifierNameSyntax id && id.Identifier.ValueText == "TrainRoute");

            Assert.True(CreationSeedMaterializer.TryMaterialize(objectCreation, model, out var seed));

            var actual = CallerChainKeyBuilder.Build(seed, compilation);
            var lineNumber = objectCreation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var expected = CallerChainKeyFormat.Build(@"C:\repo\Test0.cs", lineNumber, "Build");

            Assert.Equal(expected, actual);
            Assert.Matches("^[0-9a-f]{16}$", actual);
        }

        [Fact]
        public void CallerChainKeyBuilder_MethodInvocation_MultilineFactory_UsesInnerCtorKey()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed()
    {
        return new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\FactoryKey.cs");
            var compilation = CSharpCompilation.Create(
                "CallerChainKeyBuilderFactoryTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            var createSeed = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "CreateSeed");

            var factoryInvocation = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "CreateSeed");

            Assert.True(FactoryCallMaterializer.TryMaterialize(factoryInvocation, model, out var factoryCall));

            var objectCreation = createSeed.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(n => n.Type is IdentifierNameSyntax id && id.Identifier.ValueText == "TrainRoute");
            Assert.True(CreationSeedMaterializer.TryMaterialize(objectCreation, model, out var creation));

            var factoryKey = CallerChainKeyBuilder.Build(factoryCall, compilation);
            var ctorKey = CallerChainKeyBuilder.Build(creation, compilation);

            Assert.False(string.IsNullOrEmpty(factoryKey));
            Assert.Equal(ctorKey, factoryKey);
            Assert.NotEqual(
                CallerChainKeyFormat.Build(
                    @"C:\repo\FactoryKey.cs",
                    createSeed.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    "CreateSeed"),
                factoryKey);
        }

        [Fact]
        public void CallerChainKeyBuilder_MethodInvocation_ExpressionBodiedSameLine_StillMatchesCtor()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\SameLine.cs");
            var compilation = CSharpCompilation.Create(
                "CallerChainKeyBuilderSameLineTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            var createSeed = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "CreateSeed");

            var factoryInvocation = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "CreateSeed");

            Assert.True(FactoryCallMaterializer.TryMaterialize(factoryInvocation, model, out var factoryCall));

            var objectCreation = createSeed.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(n => n.Type is IdentifierNameSyntax id && id.Identifier.ValueText == "TrainRoute");
            Assert.True(CreationSeedMaterializer.TryMaterialize(objectCreation, model, out var creation));

            Assert.Equal(
                CallerChainKeyBuilder.Build(creation, compilation),
                CallerChainKeyBuilder.Build(factoryCall, compilation));
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

        /// <summary>
        /// Verifies schema factory without resolvable dispatch metadata returns an empty key (no method-location guess).
        /// </summary>
        [Fact]
        public void CallerChainKeyBuilder_FactorySchema_WithoutDispatchMetadata_ReturnsEmpty()
        {
            var libTree = CSharpSyntaxTree.ParseText(@"
using TrainOP;
public static class PublicFactory
{
    public static TrainRoute Build() => new TrainRoute()
        .RegisterStation(""Seed"", m => m.LoadWagon(""id"", 1));
}", path: "Lib.cs");
            var libCompilation = CSharpCompilation.Create(
                "FactorySchemaEmptyKeyLib",
                new[] { libTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new System.IO.MemoryStream();
            Assert.True(libCompilation.Emit(stream).Success);
            var image = stream.ToArray();

            var consumerTree = CSharpSyntaxTree.ParseText(@"
using TrainOP;
public static class Consumer
{
    public static TrainRoute Extend() => PublicFactory.Build()
        .Station(""Next"", (int id) => new { id });
}", path: "Consumer.cs");
            var consumerCompilation = CSharpCompilation.Create(
                "FactorySchemaEmptyKeyConsumer",
                new[] { consumerTree },
                GetMetadataReferences()
                    .Concat(new[] { MetadataReference.CreateFromImage(image) })
                    .ToArray(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = consumerCompilation.GetSemanticModel(consumerTree);
            var invocation = consumerTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(n => n.Expression is MemberAccessExpressionSyntax ma
                    && ma.Name.Identifier.ValueText == "Build");

            Assert.True(FactoryCallMaterializer.TryMaterialize(invocation, model, out var factoryCall));
            Assert.Equal(string.Empty, CallerChainKeyBuilder.Build(factoryCall, consumerCompilation));
        }

        [Fact]
        public void CallerChainKeyBuilder_Build_from_FactoryCall_part_is_stable()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\PartKey.cs");
            var compilation = CSharpCompilation.Create(
                "CallerChainKeyBuilderPartTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var model = compilation.GetSemanticModel(syntaxTree);

            var factoryInvocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "CreateSeed");

            Assert.True(FactoryCallMaterializer.TryMaterialize(
                factoryInvocation,
                model,
                out var factoryCall));

            var key = CallerChainKeyBuilder.Build(factoryCall, compilation);
            Assert.False(string.IsNullOrEmpty(key));
            Assert.Equal(key, CallerChainKeyBuilder.Build(factoryCall, compilation));
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
