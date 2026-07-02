using System.Threading;
using System.Threading.Tasks;
using TrainOP;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// End-to-end reference route on the data-oriented API.
    /// </summary>
    public sealed class DataOrientedPaymentRouteEndToEndTests
    {
        [Fact]
        public void PaymentRoute_HappyPath_DeconstructsTerminalWagons()
        {
            var report = PaymentRoute.BuildHappyPath().DispatchTrain().Travel();
            var (paymentId, amount) = report;

            Assert.True(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.Equal("pay-e2e-trace-e2e", paymentId);
            Assert.Equal(89m, amount);
            Assert.Equal("USD", report.TerminalSignal.Manifest.PullCar<string>("currency"));
        }

        [Fact]
        public void PaymentRoute_ValidationFailure_StopsAtValidateStation()
        {
            var report = PaymentRoute.BuildInvalidAmount().DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("INVALID_TOTAL", red.Issue.Code);
            Assert.Equal("Validate", red.Issue.StationName);
        }

        [Fact]
        public void PaymentRoute_ValidationFailure_RecoversAndCompletes()
        {
            var report = PaymentRoute.BuildWithRecovery().DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.Equal(2m, report.TerminalSignal.Manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public async Task PaymentRoute_AsyncHandler_CompletesWithTravelAsync()
        {
            var (paymentId, amount) = await PaymentRoute.BuildAsync().DispatchTrain().TravelAsync();

            Assert.Equal("pay-async-e2e", paymentId);
            Assert.Equal(200m, amount);
        }

        internal static class PaymentRoute
        {
            public static TrainRoute BuildHappyPath()
            {
                return new TrainRoute()
                    .Station("Seed", () => new
                    {
                        paymentId = "pay-e2e",
                        amount = 100m,
                        currency = "USD",
                        traceId = "trace-e2e",
                    })
                    .Station("Discount", (string paymentId, decimal amount) =>
                        new { paymentId, amount = amount * 0.9m })
                    .Station("Enrich", (CargoManifest manifest, string paymentId, decimal amount) =>
                        new
                        {
                            paymentId = paymentId + "-" + manifest.PullCar<string>("traceId"),
                            amount = amount - 1m,
                        })
                    .Station("Validate", (string paymentId, decimal amount) =>
                        amount > 0
                            ? Data.Ok(new { paymentId, amount })
                            : Data.Fail("INVALID_TOTAL", "amount must be positive"));
            }

            public static TrainRoute BuildInvalidAmount()
            {
                return new TrainRoute()
                    .Station("Seed", () => new { paymentId = "pay-fail", amount = -5m })
                    .Station("Validate", (string paymentId, decimal amount) =>
                        amount > 0
                            ? Data.Ok(new { paymentId, amount })
                            : Data.Fail("INVALID_TOTAL", "amount must be positive"))
                    .Station("MustNotRun", (string paymentId, decimal amount) =>
                        new { paymentId = "nope", amount });
            }

            public static TrainRoute BuildWithRecovery()
            {
                return new TrainRoute()
                    .Station("Seed", () => new { paymentId = "pay-recover", amount = 0m })
                    .Station("Validate", (string paymentId, decimal amount) =>
                        amount > 0
                            ? Data.Ok(new { paymentId, amount })
                            : Data.Fail("INVALID_TOTAL", "amount must be positive"))
                    .AttachRedSignalStation("Recovery", red =>
                        RailwaySignals.Green(red.Manifest.LoadCar("amount", 1m)))
                    .Station("Double", (string paymentId, decimal amount) =>
                        new { paymentId, amount = amount * 2m });
            }

            public static TrainRoute BuildAsync()
            {
                return new TrainRoute()
                    .Station("Seed", () => new { paymentId = "pay-async-e2e", amount = 100m })
                    .Station("Double", async (string paymentId, decimal amount, CancellationToken token) =>
                    {
                        await Task.Delay(1, token);
                        return new { paymentId, amount = amount * 2m };
                    });
            }
        }
    }
}
