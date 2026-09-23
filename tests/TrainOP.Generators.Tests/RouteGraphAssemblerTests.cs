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
    public sealed class RouteGraphAssemblerTests
    {
        [Fact]
        public void RouteGraphAssembler_Build_LinearFluentChain_AssignsSequentialIndices()
        {
            const string source = @"
using TrainOP;

public static class LinearRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""pay-1"", amount = 100m })
        .Station(""Discount"", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m })
        .Station(""Validate"", (string paymentId, decimal amount) =>
            amount > 0
                ? RailwaySignals.Green(new { paymentId, amount })
                : RailwaySignals.Red(""INVALID_TOTAL"", ""amount must be positive""));
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.Equal(3, chain.Stations.Length);
            Assert.Equal(0, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Seed").StationIndex);
            Assert.Equal(1, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Discount").StationIndex);
            Assert.Equal(2, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Validate").StationIndex);
            Assert.False(string.IsNullOrEmpty(CallerChainKeyBuilder.Build(chain.Origin)));
        }

        [Fact]
        public void RouteGraphAssembler_Build_LocalVariablePattern_UsesCtorLocationForChainKey()
        {
            const string source = @"
using TrainOP;

public static class LocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        return route
            .Station(""Seed"", () => new { id = 1 })
            .Station(""Next"", (int id) => new { id = id + 1 });
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.Equal(2, chain.Stations.Length);
            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.False(string.IsNullOrEmpty(CallerChainKeyBuilder.Build(chain.Origin)));
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalChain_AssignsSequentialIndices()
        {
            const string source = @"
using TrainOP;

public static class StatementLocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal("Seed", chain.Stations[0].StationName);
            Assert.Equal("Next", chain.Stations[1].StationName);
            Assert.Equal(0, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Seed").StationIndex);
            Assert.Equal(1, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Next").StationIndex);
            Assert.False(string.IsNullOrEmpty(CallerChainKeyBuilder.Build(chain.Origin)));
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalMixedFluent_CollectsAllStations()
        {
            const string source = @"
using TrainOP;

public static class MixedStatementLocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route.Station(""A"", () => new { id = 1 }).Station(""B"", (int id) => new { id });
        route.Station(""C"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.Equal(3, chain.Stations.Length);
            Assert.Equal(new[] { "A", "B", "C" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalReassignment_ProducesTwoChains()
        {
            const string source = @"
using TrainOP;

public static class ReassignedStatementLocalRoute
{
    public static void BuildBoth()
    {
        var route = new TrainRoute();
        route.Station(""First"", () => new { id = 1 });

        route = new TrainRoute();
        route.Station(""Second"", () => new { id = 2 });
    }
}";

            var graph = BuildGraph(source);

            Assert.Equal(2, graph.Chains.Length);
            Assert.Contains(graph.Chains, c => c.Stations.Any(s => s.StationName == "First"));
            Assert.Contains(graph.Chains, c => c.Stations.Any(s => s.StationName == "Second"));
        }

        [Fact]
        public void RouteGraphAssembler_Build_FluentRhsNewThenStatementLocal_CollectsSeedAndNext()
        {
            const string source = @"
using TrainOP;

public static class FluentRhsNewRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
            Assert.Equal(0, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Seed").StationIndex);
            Assert.Equal(1, graph.ChainIndex.Values.SelectMany(x => x).Single(b => b.StationName == "Next").StationIndex);
        }

        [Fact]
        public void RouteChainWalker_EndingAt_FluentRhsNewThenStatementLocal_IncludesSeedAndNext()
        {
            const string source = @"
using TrainOP;

public static class EndingAtFluentRhsRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\EndingAtFluentRhs.cs");
            var compilation = CSharpCompilation.Create(
                "RouteGraphAssemblerEndingAtFluentRhsTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var returnExpression = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Single()
                .Expression;

            Assert.True(BuildChainsStage.EndingAt(returnExpression, semanticModel, out var chain));
            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_FluentRhsFactoryThenStatementLocal_CollectsMidAndTail()
        {
            const string source = @"
using TrainOP;

public static class FluentRhsFactoryRoute
{
    public static TrainRoute Build()
    {
        var route = CreateSeed()
            .Station(""Mid"", (int id) => new { id });
        route.Station(""Tail"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Tail"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.NotNull(consumerChain.FactoryMethod);
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod.Name);
            Assert.Equal(2, consumerChain.Stations.Length);
            Assert.Equal(new[] { "Mid", "Tail" }, consumerChain.Stations.Select(s => s.StationName).ToArray());

            var midBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Mid");
            var tailBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Tail");
            Assert.Equal(1, midBinding.StationIndex);
            Assert.Equal(2, tailBinding.StationIndex);
        }

        [Fact]
        public void RouteChainWalker_EndingAt_FluentRhsFactoryThenStatementLocal_IncludesMidAndTail()
        {
            const string source = @"
using TrainOP;

public static class EndingAtFluentRhsFactoryRoute
{
    public static TrainRoute Build()
    {
        var route = CreateSeed()
            .Station(""Mid"", (int id) => new { id });
        route.Station(""Tail"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\EndingAtFluentRhsFactory.cs");
            var compilation = CSharpCompilation.Create(
                "RouteGraphAssemblerEndingAtFluentRhsFactoryTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var returnExpression = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Single()
                .Expression;

            Assert.True(BuildChainsStage.EndingAt(returnExpression, semanticModel, out var chain));
            Assert.True(chain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (chain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateSeed", chain.FactoryMethod?.Name);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Mid", "Tail" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteChainWalker_EndingAt_BareReturnLocal_IncludesStatementStations()
        {
            const string source = @"
using TrainOP;

public static class EndingAtStatementLocalRoute
{
    public static TrainRoute Build()
    {
        var route = new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\EndingAtStatement.cs");
            var compilation = CSharpCompilation.Create(
                "RouteGraphAssemblerEndingAtTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var returnExpression = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Single()
                .Expression;

            Assert.True(BuildChainsStage.EndingAt(returnExpression, semanticModel, out var chain));
            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_FactoryExtension_RegistersFactoryAnchor()
        {
            const string source = @"
using TrainOP;

public static class FactoryRoute
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Next"", (int id) => new { id = id + 1 });

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.NotNull(consumerChain.FactoryMethod);
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod.Name);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterPrivateFactory_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class StatementFactoryLocalRoute
{
    public static TrainRoute Build()
    {
        var route = CreateSeed();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.NotNull(consumerChain.FactoryMethod);
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod.Name);
            Assert.Equal(1, consumerChain.Stations.Length);
            Assert.Equal("Next", consumerChain.Stations[0].StationName);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_LocalFunctionFactoryExtension_RegistersFactoryAnchor()
        {
            const string source = @"
using TrainOP;

public static class LocalFunctionFactoryRoute
{
    public static TrainRoute Build()
    {
        TrainRoute Local() => new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });

        return Local()
            .Station(""Next"", (int id) => new { id = id + 1 });
    }
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.NotNull(consumerChain.FactoryMethod);
            Assert.Equal("Local", consumerChain.FactoryMethod.Name);
            Assert.Equal(MethodKind.LocalFunction, consumerChain.FactoryMethod.MethodKind);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterLocalFunctionFactory_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class StatementLocalFunctionFactoryRoute
{
    public static TrainRoute Build()
    {
        TrainRoute Local() => new TrainRoute()
            .Station(""Seed"", () => new { id = 1 });

        var route = Local();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("Local", consumerChain.FactoryMethod?.Name);
            Assert.Equal(MethodKind.LocalFunction, consumerChain.FactoryMethod.MethodKind);
            Assert.Equal(1, consumerChain.Stations.Length);
            Assert.Equal("Next", consumerChain.Stations[0].StationName);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_AwaitAsyncFactoryExtension_RegistersFactoryAnchor()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class AwaitAsyncFactoryRoute
{
    public static async Task<TrainRoute> BuildAsync() =>
        (await CreateAsync())
            .Station(""Next"", (int id) => new { id = id + 1 });

    private static async Task<TrainRoute> CreateAsync() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateAsync", consumerChain.FactoryMethod?.Name);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterAwaitAsyncFactory_OffsetsConsumerIndex()
        {
            const string source = @"
using System.Threading.Tasks;
using TrainOP;

public static class StatementAwaitAsyncFactoryRoute
{
    public static async Task<TrainRoute> BuildAsync()
    {
        var route = await CreateAsync();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static async Task<TrainRoute> CreateAsync() =>
        new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateAsync", consumerChain.FactoryMethod?.Name);
            Assert.Equal(1, consumerChain.Stations.Length);
            Assert.Equal("Next", consumerChain.Stations[0].StationName);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterOutFactory_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class StatementOutFactoryRoute
{
    public static TrainRoute Build()
    {
        Get(out TrainRoute route);
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static void Get(out TrainRoute route) =>
        route = new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("Get", consumerChain.FactoryMethod?.Name);
            Assert.Equal(1, consumerChain.Stations.Length);
            Assert.Equal("Next", consumerChain.Stations[0].StationName);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterOutPredeclaredLocal_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class StatementOutPredeclaredRoute
{
    public static TrainRoute Build()
    {
        TrainRoute route;
        Get(out route);
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static void Get(out TrainRoute route) =>
        route = new TrainRoute().Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("Get", consumerChain.FactoryMethod?.Name);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterTupleDeconstruct_CollectsSeedAndNext()
        {
            const string source = @"
using TrainOP;

public static class TupleDeconstructRoute
{
    public static TrainRoute Build()
    {
        (var route, _) = (new TrainRoute().Station(""Seed"", () => new { id = 1 }), 0);
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterVarTupleDeconstruct_CollectsNext()
        {
            const string source = @"
using TrainOP;

public static class VarTupleDeconstructRoute
{
    public static TrainRoute Build()
    {
        var (route, _) = (new TrainRoute(), 0);
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterTupleDeconstructFactory_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class TupleDeconstructFactoryRoute
{
    public static TrainRoute Build()
    {
        (var route, _) = (CreateSeed(), 0);
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod?.Name);
            Assert.Equal(1, consumerChain.Stations.Length);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterIsPattern_CollectsSeedAndNext()
        {
            const string source = @"
using TrainOP;

public static class IsPatternRoute
{
    public static TrainRoute Build()
    {
        if (new TrainRoute().Station(""Seed"", () => new { id = 1 }) is TrainRoute route)
        {
            route.Station(""Next"", (int id) => new { id = id + 1 });
            return route;
        }

        return new TrainRoute();
    }
}";

            var graph = BuildGraph(source);
            var chain = graph.Chains.Single(c => c.Stations.Any(s => s.StationName == "Next"));

            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterSwitchCasePattern_CollectsNext()
        {
            const string source = @"
using TrainOP;

public static class SwitchCasePatternRoute
{
    public static TrainRoute Build()
    {
        switch (CreateSeed())
        {
            case TrainRoute route:
                route.Station(""Next"", (int id) => new { id = id + 1 });
                return route;
            default:
                return new TrainRoute();
        }
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod?.Name);
            Assert.Equal(1, consumerChain.Stations.Length);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterTernaryAssign_CollectsSeedAndNext()
        {
            const string source = @"
using TrainOP;

public static class TernaryAssignRoute
{
    public static TrainRoute Build(bool flag)
    {
        var route = flag
            ? new TrainRoute()
            : new TrainRoute();
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterTernaryFactoryAssign_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class TernaryFactoryAssignRoute
{
    public static TrainRoute Build(bool flag)
    {
        var route = flag ? CreateSeed() : CreateSeed();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod?.Name);
            Assert.Equal(1, consumerChain.Stations.Length);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterSwitchAssign_CollectsSeedAndNext()
        {
            const string source = @"
using TrainOP;

public static class SwitchAssignRoute
{
    public static TrainRoute Build(int kind)
    {
        var route = kind switch
        {
            0 => new TrainRoute(),
            _ => new TrainRoute()
        };
        route.Station(""Seed"", () => new { id = 1 });
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }
}";

            var graph = BuildGraph(source);
            var chain = Assert.Single(graph.Chains);

            Assert.IsType<LocalBinding>(chain.Origin);
            Assert.Equal(2, chain.Stations.Length);
            Assert.Equal(new[] { "Seed", "Next" }, chain.Stations.Select(s => s.StationName).ToArray());
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterSwitchFactoryAssign_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class SwitchFactoryAssignRoute
{
    public static TrainRoute Build(int kind)
    {
        var route = kind switch
        {
            0 => CreateSeed(),
            _ => CreateSeed()
        };
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod?.Name);
            Assert.Equal(1, consumerChain.Stations.Length);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteGraphAssembler_Build_StatementLocalAfterPublicFactory_UsesFactorySchemaAnchor()
        {
            const string source = @"
using TrainOP;

public static class StatementPublicFactoryLocalRoute
{
    public static TrainRoute Build()
    {
        var route = CreateSeed();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    public static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var graph = BuildGraph(source);
            var consumerChain = graph.Chains.Single(chain =>
                chain.Stations.Any(station => station.StationName == "Next"));

            Assert.True(consumerChain.Origin is FactoryCall { Kind: FactoryCallKind.Schema } || (consumerChain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Schema));
            Assert.NotNull(consumerChain.FactoryMethod);
            Assert.Equal("CreateSeed", consumerChain.FactoryMethod.Name);
            Assert.Equal(1, consumerChain.Stations.Length);

            var nextBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Next");
            Assert.Equal(1, nextBinding.StationIndex);
        }

        [Fact]
        public void RouteChainWalker_EndingAt_StatementLocalAfterPrivateFactory_IncludesConsumerStations()
        {
            const string source = @"
using TrainOP;

public static class EndingAtFactoryLocalRoute
{
    public static TrainRoute Build()
    {
        var route = CreateSeed();
        route.Station(""Next"", (int id) => new { id = id + 1 });
        return route;
    }

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 });
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\EndingAtFactoryLocal.cs");
            var compilation = CSharpCompilation.Create(
                "RouteGraphAssemblerEndingAtFactoryLocalTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var buildMethod = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "Build");
            var returnExpression = buildMethod
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Single()
                .Expression;

            Assert.True(BuildChainsStage.EndingAt(returnExpression, semanticModel, out var chain));
            Assert.True(chain.Origin is FactoryCall { Kind: FactoryCallKind.Inline } || (chain.Origin is LocalBinding lb && lb.FactoryKind == FactoryCallKind.Inline));
            Assert.Equal("CreateSeed", chain.FactoryMethod?.Name);
            Assert.Equal(1, chain.Stations.Length);
            Assert.Equal("Next", chain.Stations[0].StationName);
        }

        [Fact]
        public void RouteGraphAssembler_Build_FactoryWithTwoInnerStations_OffsetsConsumerIndex()
        {
            const string source = @"
using TrainOP;

public static class FactoryOffsetRoute
{
    public static TrainRoute Build() => CreateSeed()
        .Station(""Finalize"", (string paymentId) => new { paymentId, ok = true });

    private static TrainRoute CreateSeed() => new TrainRoute()
        .Station(""Seed"", () => new { paymentId = ""p"" })
        .Station(""Step"", (string paymentId) => new { paymentId });
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\FactoryOffset.cs");
            var compilation = CSharpCompilation.Create(
                "RouteGraphAssemblerOffsetTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var sites = RouteSiteDiscoverer.CollectAll(compilation);
            var graph = BuildChainsStage.Build(sites, compilation);

            var consumerBinding = graph.ChainIndex.Values
                .SelectMany(x => x)
                .Single(b => b.StationName == "Finalize");

            Assert.Equal(2, consumerBinding.StationIndex);

            var root = syntaxTree.GetRoot();
            var createSeed = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == "CreateSeed");
            var objectCreation = createSeed.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(n => n.Type is IdentifierNameSyntax id
                    && id.Identifier.ValueText == "TrainRoute");
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var createSeedSymbol = semanticModel.GetDeclaredSymbol(createSeed) as IMethodSymbol;
            var ctorSeed = new CreationSeed(
                objectCreation,
                objectCreation.GetLocation(),
                createSeedSymbol);

            Assert.Equal(CallerChainKeyBuilder.Build(ctorSeed, compilation), consumerBinding.ChainId);
        }

        [Fact]
        public void RouteGraphAssembler_Build_TwoChains_ProducesDistinctChainIds()
        {
            const string source = @"
using TrainOP;

public static class DualRoute
{
    public static TrainRoute BuildA() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });

    public static TrainRoute BuildB() => new TrainRoute()
        .Station(""Seed"", () => new { id = 2 })
        .Station(""Next"", (int id) => new { id = id + 2 });
}";

            var graph = BuildGraph(source);

            Assert.Equal(2, graph.Chains.Length);
            var chainIds = graph.Chains
                .Select(chain => CallerChainKeyBuilder.Build(chain.Origin))
                .ToArray();
            Assert.Equal(2, chainIds.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void RouteGraphAssembler_Build_OrphanStation_IsNotChained()
        {
            const string source = @"
using TrainOP;

public static class OrphanRoute
{
    public static TrainRoute Build()
    {
        var orphan = new TrainRoute().Station(""Orphan"", (string paymentId) => new { paymentId });
        return new TrainRoute()
            .Station(""Seed"", () => new { paymentId = ""pay-1"" })
            .Station(""Next"", (string paymentId) => new { paymentId = paymentId + ""-ok"" });
    }
}";

            var graph = BuildGraph(source);
            var orphanInvocation = graph.StationSites
                .Single(site => site.StationName == "Orphan")
                .Invocation;

            Assert.True(graph.IsChainedInvocation(orphanInvocation.GetLocation()));
            Assert.True(graph.IsChainedInvocation(
                graph.StationSites.Single(site => site.StationName == "Seed").Invocation.GetLocation()));
        }

        [Fact]
        public void RouteGraphAssembler_TryGetChainForInvocation_ResolvesDownstreamChain()
        {
            const string source = @"
using TrainOP;

public static class DownstreamRoute
{
    public static TrainRoute Build() => new TrainRoute()
        .Station(""Seed"", () => new { id = 1 })
        .Station(""Next"", (int id) => new { id = id + 1 });
}";

            var graph = BuildGraph(source);
            var downstream = graph.StationSites.Single(site => site.StationName == "Next").Invocation;

            Assert.True(graph.TryGetChainForInvocation(downstream, out var chain));
            Assert.Equal(2, chain.Stations.Length);
        }

        private static RouteGraph BuildGraph(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, path: @"C:\repo\Test0.cs");
            var compilation = CSharpCompilation.Create(
                "RouteGraphAssemblerTests",
                new[] { syntaxTree },
                GetMetadataReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var sites = RouteSiteDiscoverer.CollectAll(compilation);
            return BuildChainsStage.Build(sites, compilation);
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
