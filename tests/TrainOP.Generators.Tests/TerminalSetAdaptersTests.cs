using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Wagons;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TerminalSet"/> and <see cref="TerminalSetAdapters"/>.
    /// </summary>
    public sealed class TerminalSetAdaptersTests
    {
        [Fact]
        public void Empty_KnownOrigin_HasNoWagons_NotUnknown()
        {
            var set = TerminalSet.Empty(TerminalSet.Origin.Linear);

            Assert.Equal(TerminalSet.Origin.Linear, set.Provenance);
            Assert.Empty(set.Wagons);
            Assert.False(set.HasUnknownReturn);
        }

        [Fact]
        public void Unknown_SetsFlag_AndEmptyWagons()
        {
            var set = TerminalSet.Unknown(TerminalSet.Origin.Join);

            Assert.Equal(TerminalSet.Origin.Join, set.Provenance);
            Assert.True(set.HasUnknownReturn);
            Assert.Empty(set.Wagons);
        }

        [Fact]
        public void FromSimulation_Null_ReturnsUnknownLinear()
        {
            var set = TerminalSetAdapters.FromSimulation(null);

            Assert.Equal(TerminalSet.Origin.Linear, set.Provenance);
            Assert.True(set.HasUnknownReturn);
        }

        [Fact]
        public void FromSimulation_KnownWagons_PreservesOriginAndWagons()
        {
            var wagons = ImmutableArray.Create(Wagon("amount"));
            var simulation = new ChainSimulationResult(
                wagons,
                hasUnknownReturn: false,
                ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>.Empty);

            var set = TerminalSetAdapters.FromSimulation(simulation, TerminalSet.Origin.FactoryPath);

            Assert.Equal(TerminalSet.Origin.FactoryPath, set.Provenance);
            Assert.False(set.HasUnknownReturn);
            Assert.Single(set.Wagons);
            Assert.Equal("amount", set.Wagons[0].Name);
        }

        [Fact]
        public void FromSimulation_UnknownReturn_ReturnsUnknown()
        {
            var simulation = new ChainSimulationResult(
                ImmutableArray.Create(Wagon("ghost")),
                hasUnknownReturn: true,
                ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>.Empty);

            var set = TerminalSetAdapters.FromSimulation(simulation);

            Assert.True(set.HasUnknownReturn);
            Assert.Empty(set.Wagons);
        }

        [Fact]
        public void FromFactoryPath_Known_TagsFactoryPathOrigin()
        {
            var path = new TrainOP.Generators.Route.FactoryPathSimulation(
                ImmutableArray.Create(Wagon("id")),
                hasUnknownReturn: false,
                location: null);

            var set = TerminalSetAdapters.FromFactoryPath(path);

            Assert.Equal(TerminalSet.Origin.FactoryPath, set.Provenance);
            Assert.Single(set.Wagons);
            Assert.Equal("id", set.Wagons[0].Name);
        }

        [Fact]
        public void FromJoin_DefaultArray_ReturnsEmptyJoin()
        {
            var set = TerminalSetAdapters.FromJoin(default);

            Assert.Equal(TerminalSet.Origin.Join, set.Provenance);
            Assert.False(set.HasUnknownReturn);
            Assert.Empty(set.Wagons);
        }

        [Fact]
        public void FromAnchorSeed_Empty_ReturnsEmptyAnchorSeed()
        {
            var set = TerminalSetAdapters.FromAnchorSeed(ImmutableArray<WagonBinding>.Empty);

            Assert.Equal(TerminalSet.Origin.AnchorSeed, set.Provenance);
            Assert.Empty(set.Wagons);
        }

        [Fact]
        public void ToWagons_Unknown_ReturnsEmpty()
        {
            var wagons = TerminalSetAdapters.ToWagons(TerminalSet.Unknown(TerminalSet.Origin.Linear));

            Assert.Empty(wagons);
        }

        [Fact]
        public void ToWagons_Known_ReturnsSameBindings()
        {
            var set = new TerminalSet(ImmutableArray.Create(Wagon("a"), Wagon("b")), TerminalSet.Origin.Join);

            var wagons = TerminalSetAdapters.ToWagons(set);

            Assert.Equal(2, wagons.Length);
            Assert.Equal("a", wagons[0].Name);
            Assert.Equal("b", wagons[1].Name);
        }

        private static WagonBinding Wagon(string name)
        {
            return new WagonBinding(name, "global::System.Int32", typeSymbol: null, location: null);
        }
    }
}
