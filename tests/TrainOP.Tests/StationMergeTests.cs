using Xunit;

namespace TrainOP.Tests
{
    /// <summary>
    /// Tests manifest merging behavior in <see cref="StationMerge.Apply"/>.
    /// </summary>
    public sealed class StationMergeTests
    {
        /// <summary>
        /// Verifies that tuple element ordinals map reordered named tuple values to manifest wagons.
        /// </summary>
        [Fact]
        public void Apply_UsesTupleElementOrdinals_ForReorderedNamedTuple()
        {
            var manifest = new CargoManifest()
                .LoadWagon("paymentId", "pay-recover")
                .LoadWagon("amount", 50m);

            var stationReturn = (amount: 45m, paymentId: "pay-recover");

            var merged = StationMerge.Apply(
                manifest,
                stationReturn,
                new[] { "paymentId", "amount" },
                removeOmittedRegularInputs: true,
                tupleElementOrdinals: new[] { 1, 0 });

            Assert.Equal("pay-recover", merged.PullWagon<string>("paymentId"));
            Assert.Equal(45m, merged.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that return member names are used when merging seed handler anonymous type returns.
        /// </summary>
        [Fact]
        public void Apply_UsesReturnMemberNames_ForSeedHandler()
        {
            var manifest = new CargoManifest();
            var stationReturn = new { paymentId = "pay-seed", amount = 10m };

            var merged = StationMerge.Apply(
                manifest,
                stationReturn,
                new string[0],
                removeOmittedRegularInputs: false,
                tupleElementOrdinals: null,
                returnMemberNames: new[] { "paymentId", "amount" });

            Assert.Equal("pay-seed", merged.PullWagon<string>("paymentId"));
            Assert.Equal(10m, merged.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that tuple ordinals apply only to value tuple returns, not anonymous types.
        /// </summary>
        [Fact]
        public void Apply_UsesTupleOrdinalsOnlyForValueTupleReturns()
        {
            var manifest = new CargoManifest()
                .LoadWagon("paymentId", "pay-1")
                .LoadWagon("amount", 100m);

            var anonymousReturn = new { paymentId = "pay-2", amount = 90m };
            var mergedAnonymous = StationMerge.Apply(
                manifest,
                anonymousReturn,
                new[] { "paymentId", "amount" },
                removeOmittedRegularInputs: true,
                tupleElementOrdinals: new[] { 0, 1 });

            Assert.Equal("pay-2", mergedAnonymous.PullWagon<string>("paymentId"));
            Assert.Equal(90m, mergedAnonymous.PullWagon<decimal>("amount"));

            var tupleReturn = (amount: 95m, paymentId: "pay-3");
            var mergedTuple = StationMerge.Apply(
                manifest,
                tupleReturn,
                new[] { "paymentId", "amount" },
                removeOmittedRegularInputs: true,
                tupleElementOrdinals: new[] { 1, 0 });

            Assert.Equal("pay-3", mergedTuple.PullWagon<string>("paymentId"));
            Assert.Equal(95m, mergedTuple.PullWagon<decimal>("amount"));
        }
    }
}
