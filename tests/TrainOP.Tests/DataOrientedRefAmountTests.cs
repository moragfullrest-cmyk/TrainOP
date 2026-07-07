using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Tests ref parameters, optional wagons, and manifest access in data-oriented stations.
    /// </summary>
    public sealed class DataOrientedRefAmountTests
    {
        /// <summary>
        /// Verifies that a ref input not returned in the handler result is updated from the ref value.
        /// </summary>
        [Fact]
        public void Station_RefInputNotReturned_IsUpdatedFromRefValue()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-ref", amount = 4m })
                .Station("UpdateRef", (string paymentId, ref decimal amount) =>
                {
                    amount = amount + 6m;
                    return new { paymentId = paymentId + "-ref" };
                });

            var report = route.DispatchTrain().Travel();
            var manifest = report.TerminalSignal.Manifest;

            Assert.Equal("pay-ref-ref", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a multi-station route can mutate all wagons exclusively through ref parameters.
        /// </summary>
        [Fact]
        public void Route_AllWagonsUpdatedViaRef_CompletesWithMutatedValues()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-ref-all", amount = 10m })
                .Station("AddSuffix", (ref string paymentId, ref decimal amount) =>
                {
                    paymentId = paymentId + "-suffix";
                    amount = amount + 5m;
                })
                .Station("ApplyDiscount", (ref string paymentId, ref decimal amount) =>
                {
                    amount = amount * 0.9m;
                });

            var report = route.DispatchTrain().Travel();
            var manifest = report.TerminalSignal.Manifest;

            Assert.True(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
            Assert.Equal("pay-ref-all-suffix", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(13.5m, manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a void handler is equivalent to returning an empty anonymous type.
        /// </summary>
        [Fact]
        public void Station_VoidHandler_MatchesEmptyAnonymousReturn()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-void", amount = 8m, note = "drop" })
                .Station("MutateRef", (string paymentId, ref decimal amount, string note) =>
                {
                    amount = amount + 2m;
                });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.False(manifest.HasWagon("paymentId"));
            Assert.Equal(10m, manifest.PullWagon<decimal>("amount"));
            Assert.False(manifest.HasWagon("note"));
        }

        /// <summary>
        /// Verifies that a ref handler with a manifest parameter can read extra wagons from the manifest.
        /// </summary>
        [Fact]
        public void Station_RefInputWithManifest_CanReadExtraCars()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-ref-manifest", amount = 2m, note = "from-manifest" })
                .Station("UpdateRefWithManifest", (CargoManifest manifest, string paymentId, ref decimal amount, string note) =>
                {
                    amount = amount + manifest.PullWagon<decimal>("amount");
                    return new { paymentId = paymentId + "-" + note };
                });

            var report = route.DispatchTrain().Travel();
            var manifest = report.TerminalSignal.Manifest;

            Assert.Equal("pay-ref-manifest-from-manifest", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(4m, manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that an optional wagon missing from the manifest uses the default value.
        /// </summary>
        [Fact]
        public void Station_OptionalWagon_MissingFromManifest_UsesDefault()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-optional" })
                .Station("WithOptional", (string paymentId, decimal? amount) =>
                    new { paymentId, amount = amount ?? 7m });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-optional", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(7m, manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that an optional wagon present in the manifest uses the manifest value.
        /// </summary>
        [Fact]
        public void Station_OptionalWagon_PresentInManifest_UsesValue()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-optional", amount = 3m })
                .Station("WithOptional", (string paymentId, decimal? amount) =>
                    new { paymentId, amount = amount ?? 7m });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-optional", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(3m, manifest.PullWagon<decimal>("amount"));
        }
    }
}
