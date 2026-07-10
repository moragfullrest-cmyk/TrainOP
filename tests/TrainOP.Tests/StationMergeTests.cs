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

        /// <summary>
        /// Verifies that a partial return removes omitted input wagons when return member metadata is merged out of wagon order.
        /// </summary>
        [Fact]
        public void Apply_PartialReturn_RemovesOmittedInputWagons_WhenReturnMembersAreMergedOutOfOrder()
        {
            var manifest = new CargoManifest()
                .LoadWagon("paymentId", "pay-partial")
                .LoadWagon("amount", 3m)
                .LoadWagon("traceId", "keep");

            var merged = StationMerge.Apply(
                manifest,
                new { paymentId = "pay-partial-merged" },
                new[] { "paymentId", "amount" },
                removeOmittedRegularInputs: true,
                returnMemberNames: new[] { "amount", "paymentId", "Item1", "Item2" });

            Assert.Equal("pay-partial-merged", merged.PullWagon<string>("paymentId"));
            Assert.False(merged.HasWagon("amount"));
            Assert.Equal("keep", merged.PullWagon<string>("traceId"));
        }

        /// <summary>
        /// Verifies that return members not listed as input wagons are still merged into the manifest.
        /// </summary>
        [Fact]
        public void Apply_MergesExtraReturnMembers_BeyondInputWagons()
        {
            var manifest = new CargoManifest()
                .LoadWagon("marker", false);

            var merged = StationMerge.Apply(
                manifest,
                new { after = "ok", marker = true },
                new[] { "marker" },
                removeOmittedRegularInputs: true,
                returnMemberNames: new[] { "after", "marker" });

            Assert.Equal("ok", merged.PullWagon<string>("after"));
            Assert.True(merged.PullWagon<bool>("marker"));
        }

        /// <summary>
        /// Verifies that extra return members are merged when return member metadata is omitted.
        /// </summary>
        [Fact]
        public void Apply_MergesExtraReturnMembers_WhenReturnMemberNamesAreNull()
        {
            var manifest = new CargoManifest()
                .LoadWagon("marker", false);

            var merged = StationMerge.Apply(
                manifest,
                new { after = "ok", marker = true },
                new[] { "marker" },
                removeOmittedRegularInputs: true,
                returnMemberNames: null);

            Assert.Equal("ok", merged.PullWagon<string>("after"));
            Assert.True(merged.PullWagon<bool>("marker"));
        }

        /// <summary>
        /// Verifies that service-station merge writes ref values back without adding new wagons from return payload.
        /// </summary>
        [Fact]
        public void ToServiceSignal_WritesRefValuesOnly_IgnoresGreenPayload()
        {
            var manifest = new CargoManifest()
                .LoadWagon("paymentId", "pay-recover")
                .LoadWagon("amount", 0m);

            var wagonNames = new[] { "paymentId", "amount" };
            var refFlags = new[] { true, true };
            var refValues = new object[] { "pay-fixed", 50m };

            var signal = StationMerge.ToServiceSignal(
                manifest,
                RailwaySignals.Green(new { paymentId = "ignored", amount = 99m, extra = "new" }),
                "Recovery",
                wagonNames,
                refFlags,
                refValues);

            var green = Assert.IsType<GreenSignal>(signal);
            Assert.Equal("pay-fixed", green.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(50m, green.Manifest.PullWagon<decimal>("amount"));
            Assert.False(green.Manifest.HasWagon("extra"));
        }

        /// <summary>
        /// Verifies that ApplyRefOnly updates existing wagons without creating new manifest keys.
        /// </summary>
        [Fact]
        public void ApplyRefOnly_UpdatesExistingWagonsOnly()
        {
            var manifest = new CargoManifest()
                .LoadWagon("value", 0);

            var merged = StationMerge.ApplyRefOnly(
                manifest,
                new[] { "value" },
                new[] { true },
                new object[] { 1 });

            Assert.Equal(1, merged.PullWagon<int>("value"));
            Assert.Single(merged.InspectWagons());
        }
    }
}

