using System.Threading.Tasks;
using TrainOP;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    public sealed class TypedTravelTests
    {
        [Fact]
        public void Travel_DeconstructsTerminalWagons_FromDataOrientedChain()
        {
            var route = PaymentRoute.Build();

            var (paymentId, amount) = route.DispatchTrain().Travel();

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(90m, amount);
        }

        [Fact]
        public void Travel_DeconstructsTerminalWagonsAndReport_FromDataOrientedChain()
        {
            var route = PaymentRoute.Build();

            var report = route.DispatchTrain().Travel();
            var (paymentId, amount) = report;

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(90m, amount);
            Assert.True(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
        }

        [Fact]
        public async Task TravelAsync_DeconstructsTerminalWagons_FromAsyncDataOrientedChain()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-async", amount = 5m })
                .Station("Double", async (string paymentId, decimal amount, System.Threading.CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return new { paymentId = paymentId + "-async", amount = amount * 2m };
                });

            var (paymentId, amount) = await route.DispatchTrain().TravelAsync();

            Assert.Equal("pay-async-async", paymentId);
            Assert.Equal(10m, amount);
        }

        [Fact]
        public void Travel_DeconstructsRemainingWagons_AfterPartialReturn()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
                .Station("Partial", (string paymentId, decimal amount) =>
                    new { paymentId = paymentId + "-merged" });

            var report = route.DispatchTrain().Travel();

            Assert.Equal("pay-partial-merged", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
            Assert.Equal("keep", report.TerminalSignal.Manifest.PullCar<string>("traceId"));
            Assert.False(report.TerminalSignal.Manifest.HasCar("amount"));
        }

        private static class PaymentRoute
        {
            public static TrainRoute Build() => new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("Discount", (string paymentId, decimal amount) =>
                    new { paymentId, amount = amount * 0.9m })
                .Station("Validate", (string paymentId, decimal amount) =>
                    amount > 0
                        ? Data.Ok(new { paymentId, amount })
                        : Data.Fail("INVALID_TOTAL", "amount must be positive"));
        }
    }
}
