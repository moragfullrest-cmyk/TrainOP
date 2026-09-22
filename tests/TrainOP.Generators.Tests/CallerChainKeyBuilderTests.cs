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

            var actual = CallerChainKeyBuilder.Build(anchor, compilation);

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

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            var createSeed = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "CreateSeed");
            var createSeedSymbol = semanticModel.GetDeclaredSymbol(createSeed) as IMethodSymbol;
            Assert.NotNull(createSeedSymbol);

            var factoryInvocation = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "CreateSeed");

            var methodInvocationAnchor = new RouteChainAnchor(
                RouteChainAnchorKind.MethodInvocation,
                factoryInvocation,
                factoryInvocation.GetLocation(),
                semanticModel.GetDeclaredSymbol(
                    root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                        .Single(m => m.Identifier.ValueText == "Build")) as IMethodSymbol,
                createSeedSymbol);

            var objectCreation = createSeed.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(n => n.Type is IdentifierNameSyntax id && id.Identifier.ValueText == "TrainRoute");

            var objectCreationAnchor = new RouteChainAnchor(
                RouteChainAnchorKind.ObjectCreation,
                objectCreation,
                objectCreation.GetLocation(),
                createSeedSymbol);

            var factoryKey = CallerChainKeyBuilder.Build(methodInvocationAnchor, compilation);
            var ctorKey = CallerChainKeyBuilder.Build(objectCreationAnchor, compilation);

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

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            var createSeed = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "CreateSeed");
            var createSeedSymbol = semanticModel.GetDeclaredSymbol(createSeed) as IMethodSymbol;
            Assert.NotNull(createSeedSymbol);

            var factoryInvocation = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "CreateSeed");

            var methodInvocationAnchor = new RouteChainAnchor(
                RouteChainAnchorKind.MethodInvocation,
                factoryInvocation,
                factoryInvocation.GetLocation(),
                semanticModel.GetDeclaredSymbol(
                    root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                        .Single(m => m.Identifier.ValueText == "Build")) as IMethodSymbol,
                createSeedSymbol);

            var objectCreation = createSeed.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(n => n.Type is IdentifierNameSyntax id && id.Identifier.ValueText == "TrainRoute");

            var objectCreationAnchor = new RouteChainAnchor(
                RouteChainAnchorKind.ObjectCreation,
                objectCreation,
                objectCreation.GetLocation(),
                createSeedSymbol);

            Assert.Equal(
                CallerChainKeyBuilder.Build(objectCreationAnchor, compilation),
                CallerChainKeyBuilder.Build(methodInvocationAnchor, compilation));
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
        /// Verifies FactorySchema anchors without resolvable dispatch metadata return an empty key (no method-location guess).
        /// </summary>
        [Fact]
        public void CallerChainKeyBuilder_FactorySchema_WithoutDispatchMetadata_ReturnsEmpty()
        {
            // Metadata-only factory (no body, no schema attributes): FactorySchema must not guess method location.
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

            var factory = consumerCompilation.GetTypeByMetadataName("PublicFactory");
            Assert.NotNull(factory);
            var buildMethod = factory.GetMembers("Build").OfType<IMethodSymbol>().Single();

            var extend = consumerTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "Extend");
            var invocation = extend.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .First(n => n.Expression is MemberAccessExpressionSyntax ma
                    && ma.Name.Identifier.ValueText == "Build");

            var anchor = new RouteChainAnchor(
                RouteChainAnchorKind.FactorySchema,
                invocation,
                invocation.GetLocation(),
                consumerCompilation.GetSemanticModel(consumerTree).GetDeclaredSymbol(extend) as IMethodSymbol,
                buildMethod);

            Assert.Equal(string.Empty, CallerChainKeyBuilder.Build(anchor, consumerCompilation));
        }

        [Fact]
        public void CallerChainKeyBuilder_Build_from_FactoryCall_part_matches_anchor()
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
            var root = syntaxTree.GetRoot();

            var factoryInvocation = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(invocation =>
                    invocation.Expression is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "CreateSeed");

            Assert.True(TrainOP.Generators.Parts.FactoryCallMaterializer.TryMaterialize(
                factoryInvocation,
                model,
                out var factoryCall));

            Assert.True(TrainOP.Generators.Parts.LegacyRoutePartAdapter.TryToLegacyAnchor(
                factoryCall,
                out var anchor));

            Assert.Equal(
                CallerChainKeyBuilder.Build(anchor, compilation),
                CallerChainKeyBuilder.Build(factoryCall, compilation));
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
