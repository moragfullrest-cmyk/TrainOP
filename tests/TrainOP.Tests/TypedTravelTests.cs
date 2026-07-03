using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Tests typed deconstruction of terminal wagons from Travel and TravelAsync results.
    /// </summary>
    public sealed class TypedTravelTests
    {
        /// <summary>
        /// Verifies that Travel deconstructs terminal wagons from a data-oriented route chain.
        /// </summary>
        [Fact]
        public void Travel_DeconstructsTerminalWagons_FromDataOrientedChain()
        {
            var route = PaymentRoute.Build();

            (string paymentId, decimal amount) = route.DispatchTrain().Travel();

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(90m, amount);
        }

        /// <summary>
        /// Verifies that Travel deconstructs both terminal wagons and the route report from a data-oriented chain.
        /// </summary>
        [Fact]
        public void Travel_DeconstructsTerminalWagonsAndReport_FromDataOrientedChain()
        {
            var route = PaymentRoute.Build();

            var report = route.DispatchTrain().Travel();
            (string paymentId, decimal amount) = report;

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(90m, amount);
            Assert.True(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
        }

        /// <summary>
        /// Verifies that TravelAsync deconstructs terminal wagons from an async data-oriented route chain.
        /// </summary>
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

            (string paymentId, decimal amount) = await route.DispatchTrain().TravelAsync();

            Assert.Equal("pay-async-async", paymentId);
            Assert.Equal(10m, amount);
        }

        /// <summary>
        /// Verifies that Travel deconstructs remaining wagons after a partial station return omits some inputs.
        /// </summary>
        [Fact]
        public void Travel_DeconstructsRemainingWagons_AfterPartialReturn()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
                .Station("Partial", (string paymentId, decimal amount) =>
                    new { paymentId = paymentId + "-merged" });

            var report = route.DispatchTrain().Travel();

            Assert.Equal("pay-partial-merged", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal("keep", report.TerminalSignal.Manifest.PullWagon<string>("traceId"));
            Assert.False(report.TerminalSignal.Manifest.HasWagon("amount"));
        }

        private static class PaymentRoute
        {
            public static TrainRoute Build() => new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("Discount", (string paymentId, decimal amount) =>
                    new { paymentId, amount = amount * 0.9m })
                .Station("Validate", (string paymentId, decimal amount) =>
                    amount > 0
                        ? RailwaySignals.Green(new { paymentId, amount })
                        : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"));
        }
    }
}
