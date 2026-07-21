using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;
using TrainOP.Generators.Wagons;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests compile-time merge plan construction for typed station return codegen.
    /// </summary>
    public sealed class MergePlanBuilderTests
    {
        /// <summary>
        /// Verifies that default ItemN tuple returns map input wagons by positional return member names.
        /// </summary>
        [Fact]
        public void Build_MapsDefaultItemNTuple_ByPositionalReturnMemberNames()
        {
            var plan = MergePlanBuilder.Build(Binding(
                new[] { "paymentId", "amount" },
                TupleShape(hasDefaultItemN: true, "Item1", "Item2")));

            Assert.Equal(2, plan.InputSlots.Length);
            Assert.Equal("Item1", plan.InputSlots[0].ReturnMemberName);
            Assert.Equal("paymentId", plan.InputSlots[0].WagonName);
            Assert.Equal("Item2", plan.InputSlots[1].ReturnMemberName);
            Assert.Equal("amount", plan.InputSlots[1].WagonName);
            Assert.Empty(plan.ExtraSlots);
        }

        /// <summary>
        /// Verifies that inferred tuple element names map input wagons by member name.
        /// </summary>
        [Fact]
        public void Build_MapsInferredNamedTuple_ByMemberName()
        {
            var plan = MergePlanBuilder.Build(Binding(
                new[] { "paymentId", "amount" },
                NamedTupleShape("paymentId", "amount")));

            Assert.Equal("paymentId", plan.InputSlots[0].ReturnMemberName);
            Assert.Equal("amount", plan.InputSlots[1].ReturnMemberName);
            Assert.Empty(plan.ExtraSlots);
        }

        /// <summary>
        /// Verifies that reordered named tuple values still map to manifest wagons by member name.
        /// </summary>
        [Fact]
        public void Build_MapsReorderedNamedTuple_ByMemberName()
        {
            var plan = MergePlanBuilder.Build(Binding(
                new[] { "paymentId", "amount" },
                NamedTupleShape("amount", "paymentId")));

            Assert.Equal("paymentId", plan.InputSlots[0].ReturnMemberName);
            Assert.Equal("amount", plan.InputSlots[1].ReturnMemberName);
        }

        /// <summary>
        /// Verifies that partial returns leave later input wagons unmapped.
        /// </summary>
        [Fact]
        public void Build_LeavesMissingInputWagonsUnmapped_ForPartialReturn()
        {
            var plan = MergePlanBuilder.Build(Binding(
                new[] { "paymentId", "amount" },
                RecordShape("paymentId")));

            Assert.Equal("paymentId", plan.InputSlots[0].ReturnMemberName);
            Assert.Null(plan.InputSlots[1].ReturnMemberName);
            Assert.False(plan.InputSlots[1].IsMapped);
        }

        /// <summary>
        /// Verifies that extra return members beyond input wagons are emitted as extra slots.
        /// </summary>
        [Fact]
        public void Build_IncludesExtraReturnMembers_BeyondInputWagons()
        {
            var plan = MergePlanBuilder.Build(Binding(
                new[] { "marker" },
                RecordShape("after", "marker")));

            Assert.Single(plan.ExtraSlots);
            Assert.Equal("after", plan.ExtraSlots[0].ReturnMemberName);
        }

        private static StationHandlerBinding Binding(string[] inputWagons, ReturnShape returnShape)
        {
            var wagons = ImmutableArray.CreateBuilder<WagonBinding>();
            for (var i = 0; i < inputWagons.Length; i++)
            {
                wagons.Add(new WagonBinding(
                    inputWagons[i],
                    "global::System.Object",
                    null,
                    Location.None));
            }

            var input = new HandlerInputParameters(
                wagons.ToImmutable(),
                HandlerStationKind.Station,
                includeManifest: false,
                includeRedSignal: false,
                includeSignalIssue: false,
                hasCancellationToken: false);
            var output = HandlerOutputParameters.From(returnShape);
            return new StationHandlerBinding(input, output, isAsync: false);
        }

        private static ReturnShape TupleShape(bool hasDefaultItemN, params string[] memberNames)
        {
            return Shape(memberNames, isValueTuple: true, hasDefaultItemN: hasDefaultItemN);
        }

        private static ReturnShape NamedTupleShape(params string[] memberNames)
        {
            return Shape(memberNames, isValueTuple: true, hasDefaultItemN: false);
        }

        private static ReturnShape RecordShape(params string[] memberNames)
        {
            return Shape(memberNames, isValueTuple: false, hasDefaultItemN: false);
        }

        private static ReturnShape Shape(string[] memberNames, bool isValueTuple, bool hasDefaultItemN)
        {
            var members = ImmutableArray.CreateBuilder<WagonBinding>();
            for (var i = 0; i < memberNames.Length; i++)
            {
                members.Add(new WagonBinding(
                    memberNames[i],
                    "global::System.Object",
                    null,
                    Location.None));
            }

            return new ReturnShape(
                members.ToImmutable(),
                isCargoManifest: false,
                isValueTuple: isValueTuple,
                hasDefaultItemNTupleElements: hasDefaultItemN,
                returnTypeDisplay: isValueTuple
                    ? "(global::System.String, global::System.Decimal)"
                    : "global::Generated.Record");
        }
    }
}
