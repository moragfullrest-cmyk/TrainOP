using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ChainDispatchPolicy"/> (when chain-aware dispatch is required).
    /// </summary>
    public sealed class ChainDispatchPolicyTests
    {
        [Fact]
        public void RequiresChainDispatch_NoChainBindings_ReturnsFalse()
        {
            var required = ChainDispatchPolicy.RequiresChainDispatch(
                chainBindings: new List<ChainSiteBinding>(),
                returnShapes: new[] { AnonymousShape("id") },
                entryWagonNameKeys: new[] { "id" });

            Assert.False(required);
        }

        [Fact]
        public void RequiresChainDispatch_DivergentEntryWagonNames_ReturnsTrue()
        {
            var binding = CreateBinding(wagonNames: new[] { "paymentId" });
            var required = ChainDispatchPolicy.RequiresChainDispatch(
                chainBindings: new List<ChainSiteBinding> { CreateChainBinding(binding) },
                returnShapes: new[] { AnonymousShape("paymentId") },
                entryWagonNameKeys: new[] { "paymentId", "orderId" });

            Assert.True(required);
        }

        [Fact]
        public void RequiresChainDispatch_UniformWagonNames_SingleAnonymousShape_ReturnsFalse()
        {
            var binding = CreateBinding(wagonNames: new[] { "id" });
            var required = ChainDispatchPolicy.RequiresChainDispatch(
                chainBindings: new List<ChainSiteBinding> { CreateChainBinding(binding) },
                returnShapes: new[] { AnonymousShape("id") },
                entryWagonNameKeys: new[] { "id", "id" });

            Assert.False(required);
        }

        [Fact]
        public void UsesChainDispatch_ServiceStation_ReturnsFalse()
        {
            var uses = ChainDispatchPolicy.UsesChainDispatch(
                chainBindings: new List<ChainSiteBinding>
                {
                    CreateChainBinding(CreateBinding(new[] { "a" })),
                    CreateChainBinding(CreateBinding(new[] { "b" })),
                },
                returnShapes: new[] { AnonymousShape("x") },
                isServiceStation: true);

            Assert.False(uses);
        }

        [Fact]
        public void UsesChainDispatch_DivergentChainWagonNames_ReturnsTrue()
        {
            var uses = ChainDispatchPolicy.UsesChainDispatch(
                chainBindings: new List<ChainSiteBinding>
                {
                    CreateChainBinding(CreateBinding(new[] { "paymentId" })),
                    CreateChainBinding(CreateBinding(new[] { "orderId" })),
                },
                returnShapes: new[] { AnonymousShape("ok") },
                isServiceStation: false);

            Assert.True(uses);
        }

        [Fact]
        public void UsesChainDispatch_TypedShapesCannotConsolidate_ReturnsTrue()
        {
            var namedTuple = new ReturnShape(
                ImmutableArray.Create(Wagon("Item1")),
                isCargoManifest: false,
                isValueTuple: true,
                returnTypeDisplay: "(string paymentId)",
                useGenericReturn: false,
                hasDefaultItemNTupleElements: false);
            var defaultItemN = new ReturnShape(
                ImmutableArray.Create(Wagon("Item1")),
                isCargoManifest: false,
                isValueTuple: true,
                returnTypeDisplay: "(string)",
                useGenericReturn: false,
                hasDefaultItemNTupleElements: true);

            var uses = ChainDispatchPolicy.UsesChainDispatch(
                chainBindings: new List<ChainSiteBinding>
                {
                    CreateChainBinding(CreateBinding(System.Array.Empty<string>())),
                },
                returnShapes: new[] { namedTuple, defaultItemN },
                isServiceStation: false);

            Assert.True(uses);
        }

        private static ReturnShape AnonymousShape(string memberName)
        {
            return new ReturnShape(
                ImmutableArray.Create(Wagon(memberName)),
                isCargoManifest: false,
                isValueTuple: false,
                returnTypeDisplay: "global::System.Object",
                useGenericReturn: true);
        }

        private static WagonBinding Wagon(string name)
        {
            return new WagonBinding(name, "global::System.String", typeSymbol: null, location: null);
        }

        private static StationHandlerBinding CreateBinding(string[] wagonNames, bool isServiceStation = false)
        {
            var wagons = ImmutableArray.CreateBuilder<WagonBinding>();
            foreach (var name in wagonNames)
            {
                wagons.Add(Wagon(name));
            }

            var input = new HandlerInputParameters(
                wagons.ToImmutable(),
                isServiceStation ? HandlerStationKind.ServiceStation : HandlerStationKind.Station,
                includeManifest: false,
                includeRedSignal: false,
                includeSignalIssue: false,
                includeSignalIssues: false,
                hasCancellationToken: false);

            return new StationHandlerBinding(input, HandlerOutputParameters.From(AnonymousShape("x")), isAsync: false);
        }

        private static ChainSiteBinding CreateChainBinding(StationHandlerBinding schema)
        {
            return new ChainSiteBinding(
                chainId: "chain-1",
                stationIndex: 0,
                stationName: "S",
                invocation: null,
                schema: schema);
        }
    }
}
