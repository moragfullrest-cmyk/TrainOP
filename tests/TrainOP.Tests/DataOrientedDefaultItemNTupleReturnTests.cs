using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Tests merging of default ItemN tuple returns from data-oriented stations.
    /// </summary>
    public sealed class DataOrientedDefaultItemNTupleReturnTests
    {
        /// <summary>
        /// Verifies that a station returning a default ItemN tuple allocates new ItemN wagons
        /// and unloads omitted input wagons.
        /// </summary>
        [Fact]
        public void Station_ReturnsDefaultItemNTuple_AllocatesItemNWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                    (paymentId + "-disc", amount * 0.9m));

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1-disc", report.Manifest.PullWagon<string>("Item1"));
            Assert.Equal(90m, report.Manifest.PullWagon<decimal>("Item2"));
            Assert.False(report.Manifest.HasWagon("paymentId"));
            Assert.False(report.Manifest.HasWagon("amount"));
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
            Assert.Equal("pay-1-disc", report.Get<string>("Item1"));
            Assert.Equal(90m, report.Get<decimal>("Item2"));
            Assert.False(report.Manifest.HasWagon("note"));
            Assert.False(report.Manifest.HasWagon("paymentId"));
        }

        /// <summary>
        /// Verifies a later station can consume allocated ItemN wagons from a default tuple return.
        /// </summary>
        [Fact]
        public void Station_DefaultItemNTuple_ThenNamedConsumer_ReadsItemNKeys()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                    (paymentId + "", amount * 0.9m))
                .Station("Finalize", (string Item1, decimal Item2) =>
                    new { paymentId = Item1, amount = Item2, status = "ok" })
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1", report.Get<string>("paymentId"));
            Assert.Equal(90m, report.Get<decimal>("amount"));
            Assert.Equal("ok", report.Get<string>("status"));
            Assert.False(report.Manifest.HasWagon("Item1"));
            Assert.False(report.Manifest.HasWagon("Item2"));
        }

        /// <summary>
        /// Verifies sequential unnamed tuples reuse Item1/Item2 after the next station spends them.
        /// </summary>
        [Fact]
        public void Station_SequentialDefaultItemN_ReusesItemN_AfterSpend()
        {
            var report = new TrainRoute()
                .Station("Seed", () => ("pay-1", 100m))
                .Station("Transform", (string Item1, decimal Item2) =>
                    (Item1 + "-x", Item2 * 0.5m))
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1-x", report.Get<string>("Item1"));
            Assert.Equal(50m, report.Get<decimal>("Item2"));
        }

        /// <summary>
        /// Verifies two consecutive unnamed returns without consuming prior ItemN allocate Item3/Item4.
        /// </summary>
        [Fact]
        public void Station_SecondUnnamedTuple_IncrementsWhenFirstItemNStillLive()
        {
            var report = new TrainRoute()
                .Station("Seed", () => ("a", 1m))
                .Station("AddMore", () => ("b", 2m))
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("a", report.Get<string>("Item1"));
            Assert.Equal(1m, report.Get<decimal>("Item2"));
            Assert.Equal("b", report.Get<string>("Item3"));
            Assert.Equal(2m, report.Get<decimal>("Item4"));
        }
    }
}
