using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Tests terminal wagon access through RouteReport.
    /// </summary>
    public sealed class RouteReportAccessTests
    {
        /// <summary>
        /// Verifies that Travel exposes terminal wagons via report getters.
        /// </summary>
        [Fact]
        public void Travel_ReadsTerminalWagons_FromDataOrientedChain()
        {
            var route = PaymentRoute.Build();

            var report = route.DispatchTrain().Travel();
            var paymentId = report.Get<string>("paymentId");
            var amount = report.Get<decimal>("amount");

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(90m, amount);
        }

        /// <summary>
        /// Verifies that Travel keeps report details available on successful route completion.
        /// </summary>
        [Fact]
        public void Travel_ProvidesReport_OnSuccessfulRoute()
        {
            var route = PaymentRoute.Build();
            var report = route.DispatchTrain().Travel();
            var paymentId = report.Get<string>("paymentId");
            var amount = report.Get<decimal>("amount");

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(90m, amount);
            Assert.True(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
        }

        /// <summary>
        /// Verifies that TravelAsync exposes terminal wagons via report getters.
        /// </summary>
        [Fact]
        public async Task TravelAsync_ReadsTerminalWagons_FromAsyncDataOrientedChain()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-async", amount = 5m })
                .Station("Double", async (string paymentId, decimal amount, System.Threading.CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return new { paymentId = paymentId + "-async", amount = amount * 2m };
                });

            var report = await route.DispatchTrain().TravelAsync();
            var paymentId = report.Get<string>("paymentId");
            var amount = report.Get<decimal>("amount");

            Assert.Equal("pay-async-async", paymentId);
            Assert.Equal(10m, amount);
        }

        /// <summary>
        /// Verifies that Travel keeps remaining wagons after a partial station return.
        /// </summary>
        [Fact]
        public void Travel_KeepsRemainingWagons_AfterPartialReturn()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
                .Station("Partial", (string paymentId, decimal amount) =>
                    new { paymentId = paymentId + "-merged" });

            var report = route.DispatchTrain().Travel();
            var paymentId = report.Get<string>("paymentId");
            var traceId = report.Get<string>("traceId");

            Assert.Equal("pay-partial-merged", paymentId);
            Assert.Equal("keep", traceId);
        }

        /// <summary>
        /// Verifies that a red route exposes failure code and message on the report.
        /// </summary>
        [Fact]
        public void Travel_ExposesFailureDetails_OnRedRoute()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) => RailwaySignals.Red("ERR", "bad value"));

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal("ERR", report.FailureCode);
            Assert.Equal("bad value", report.FailureMessage);
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
