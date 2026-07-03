using TrainOP;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    public sealed class DataOrientedNamedTupleReturnTests
    {
        [Fact]
        public void Station_ReturnsReorderedNamedTuple_MergesByName()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-recover", amount = 50m })
                .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                    (amount: amount * 0.9m, paymentId));

            var report = route.DispatchTrain().Travel();
            (string paymentId, decimal amount) = report;

            Assert.Equal("pay-recover", paymentId);
            Assert.Equal(45m, amount);
            Assert.True(report.ReachedDestination);
        }
    }
}
