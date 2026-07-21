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

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1-disc", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(90m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }
    }
}
