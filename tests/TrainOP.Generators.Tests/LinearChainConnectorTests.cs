using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TrainOP.Generators.Parts;
using TrainOP.Generators.Route;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests <see cref="LinearChainConnector"/> Bind/Append linear connect (C2).
    /// </summary>
    public sealed class LinearChainConnectorTests
    {
        [Fact]
        public void try_connect_fluent_new_appends_stations()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            GetObjectCreation(source, out var creation, out var model);
            Assert.True(CreationSeedMaterializer.TryMaterialize(creation, model, out var seed));

            Assert.True(LinearChainConnector.TryConnect(seed, model, out var ctor, out var chain));
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
            Assert.Equal(RouteChainAnchorKind.ObjectCreation, chain.Anchor.Kind);
            Assert.Equal(2, ctor.Edges.Count(e => e.Kind == PartEdgeKind.Append));
        }

        [Fact]
        public void try_connect_statement_local_binds_and_appends()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            GetLocalReceiver(source, "route", out var identifier, out var model);
            Assert.True(LocalBindingMaterializer.TryMaterialize(identifier, model, out var binding));

            Assert.True(LinearChainConnector.TryConnect(binding, model, out var ctor, out var chain));
            Assert.Equal(RouteChainAnchorKind.LocalVariable, chain.Anchor.Kind);
            Assert.Same(identifier, chain.Anchor.Root);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Contains(ctor.Edges, e => e.Kind == PartEdgeKind.Bind);
            Assert.Equal(2, ctor.Edges.Count(e => e.Kind == PartEdgeKind.Append));
        }

        [Fact]
        public void try_connect_fluent_assigned_to_local_folds_statement_tail()
        {
            const string source = @"
using TrainOP;

public static class Route
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            GetObjectCreation(source, out var creation, out var model);
            Assert.True(CreationSeedMaterializer.TryMaterialize(creation, model, out var seed));

            Assert.True(LinearChainConnector.TryConnect(seed, model, out _, out var chain));
            Assert.Equal(RouteChainAnchorKind.LocalVariable, chain.Anchor.Kind);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void part_edge_validator_rejects_append_of_non_station()
        {
            var creation = (ObjectCreationExpressionSyntax)SyntaxFactory.ParseExpression("new TrainRoute()");
            var seed = new CreationSeed(creation, Location.None);
            var local = new LocalBinding(
                SyntaxFactory.IdentifierName("route"),
                Location.None,
                seed);

            var ctor = new ChainConstructor();
            Assert.False(ctor.TryAppend(seed, local, out _));
            Assert.True(ctor.TryBind(seed, local, out _));
        }

        private static void GetObjectCreation(
            string source,
            out ObjectCreationExpressionSyntax creation,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "LinearChainConnectorTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            creation = tree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .First();
        }

        private static void GetLocalReceiver(
            string source,
            string localName,
            out IdentifierNameSyntax identifier,
            out SemanticModel model)
        {
            var tree = CSharpSyntaxTree.ParseText(source, path: "Test0.cs");
            var compilation = CSharpCompilation.Create(
                "LinearChainConnectorTests",
                new[] { tree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            model = compilation.GetSemanticModel(tree);
            identifier = tree.GetRoot()
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .First(id =>
                    id.Identifier.ValueText == localName
                    && id.Parent is MemberAccessExpressionSyntax member
                    && ReferenceEquals(member.Expression, id));
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
