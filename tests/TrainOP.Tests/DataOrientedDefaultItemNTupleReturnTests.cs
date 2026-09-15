using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Tests merging of default ItemN tuple returns from data-oriented stations.
    /// </summary>
    public sealed class DataOrientedDefaultItemNTupleReturnTests
    {
        /// <summary>
        /// Verifies that a station returning a default ItemN tuple merges values into input wagon keys by position.
        /// </summary>
        [Fact]
        public void Station_ReturnsDefaultItemNTuple_MergesIntoInputWagonKeys()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                    (paymentId + "-disc", amount * 0.9m));

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1-disc", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(90m, report.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies a partial default ItemN tuple return unloads omitted input wagons (runtime TOP003 parity).
        /// </summary>
        [Fact]
        public void Station_PartialDefaultItemNTuple_UnloadsOmittedWagon()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m, note = "keep?" })
                .Station("ApplyDiscount", (string paymentId, decimal amount, string note) =>
                    (paymentId + "-disc", amount * 0.9m))
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1-disc", report.Get<string>("paymentId"));
            Assert.Equal(90m, report.Get<decimal>("amount"));
            Assert.False(report.Manifest.HasWagon("note"));
        }

        /// <summary>
        /// Verifies a later station can consume wagons remapped from a default ItemN tuple return.
        /// </summary>
        [Fact]
        public void Station_DefaultItemNTuple_ThenNamedConsumer_ReadsInputWagonKeys()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                    (paymentId, amount * 0.9m))
                .Station("Finalize", (string paymentId, decimal amount) =>
                    new { paymentId, amount, status = "ok" })
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1", report.Get<string>("paymentId"));
            Assert.Equal(90m, report.Get<decimal>("amount"));
            Assert.Equal("ok", report.Get<string>("status"));
            Assert.False(report.Manifest.HasWagon("Item1"));
            Assert.False(report.Manifest.HasWagon("Item2"));
        }
    }
}
