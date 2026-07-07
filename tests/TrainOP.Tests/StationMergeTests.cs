using Xunit;

namespace TrainOP.Tests
{
    /// <summary>
    /// Tests manifest merging behavior in <see cref="StationMerge.Apply"/>.
    /// </summary>
    public sealed class StationMergeTests
    {
        /// <summary>
        /// Verifies that reordered named tuple values merge into manifest wagons by member name.
        /// </summary>
        [Fact]
        public void Apply_MergesReorderedNamedTuple_ByMemberName()
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
                returnMemberNames: new[] { "amount", "paymentId" });

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
                returnMemberNames: new[] { "paymentId", "amount" });

            Assert.Equal("pay-seed", merged.PullWagon<string>("paymentId"));
            Assert.Equal(10m, merged.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that unnamed tuple returns merge by positional return member names (Item1, Item2).
        /// </summary>
        [Fact]
        public void Apply_MergesUnnamedTuple_ByPositionalReturnMemberNames()
        {
            var manifest = new CargoManifest()
                .LoadWagon("paymentId", "pay-1")
                .LoadWagon("amount", 100m);

            var stationReturn = ("pay-2", 90m);

            var merged = StationMerge.Apply(
                manifest,
                stationReturn,
                new[] { "paymentId", "amount" },
                removeOmittedRegularInputs: true,
                returnMemberNames: new[] { "Item1", "Item2" });

            Assert.Equal("pay-2", merged.PullWagon<string>("paymentId"));
            Assert.Equal(90m, merged.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that return member names do not affect anonymous type merging when wagon names match fields.
        /// </summary>
        [Fact]
        public void Apply_IgnoresPositionalReturnMemberNames_ForAnonymousTypes()
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
                returnMemberNames: new[] { "Item1", "Item2" });

            Assert.Equal("pay-2", mergedAnonymous.PullWagon<string>("paymentId"));
            Assert.Equal(90m, mergedAnonymous.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a void station return matches an empty anonymous type return.
        /// </summary>
        [Fact]
        public void Apply_VoidReturn_MatchesEmptyAnonymousReturn()
        {
            var manifest = new CargoManifest()
                .LoadWagon("paymentId", "pay-void")
                .LoadWagon("amount", 8m)
                .LoadWagon("note", "drop");

            var wagonNames = new[] { "paymentId", "amount", "note" };
            var refFlags = new[] { false, true, false };
            var refValues = new object[] { "pay-void", 10m, "drop" };
            var emptyReturn = new { };

            var mergedVoid = StationMerge.Apply(
                manifest,
                stationReturn: null,
                wagonNames,
                removeOmittedRegularInputs: true,
                byReferenceWagons: refFlags,
                refLocalValues: refValues);

            var mergedEmpty = StationMerge.Apply(
                manifest,
                emptyReturn,
                wagonNames,
                removeOmittedRegularInputs: true,
                byReferenceWagons: refFlags,
                refLocalValues: refValues);

            foreach (var merged in new[] { mergedVoid, mergedEmpty })
            {
                Assert.False(merged.HasWagon("paymentId"));
                Assert.Equal(10m, merged.PullWagon<decimal>("amount"));
                Assert.False(merged.HasWagon("note"));
            }
        }
    }
}
