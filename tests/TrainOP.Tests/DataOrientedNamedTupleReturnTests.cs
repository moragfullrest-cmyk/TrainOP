using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Tests merging of reordered named tuple returns from data-oriented stations.
    /// </summary>
    public sealed class DataOrientedNamedTupleReturnTests
    {
        /// <summary>
        /// Verifies that a station returning a reordered named tuple merges wagon values by name.
        /// </summary>
        [Fact]
        public void Station_ReturnsReorderedNamedTuple_MergesByName()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-recover", amount = 50m })
                .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                    (amount: amount * 0.9m, paymentId));

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-recover", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(45m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }
    }
}
