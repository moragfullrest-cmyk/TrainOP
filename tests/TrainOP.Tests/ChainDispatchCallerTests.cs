using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Runtime tests for chain-dispatched station handlers resolved via caller identity.
    /// </summary>
    public sealed class ChainDispatchCallerTests
    {
        /// <summary>
        /// Verifies that a payment chain resolves paymentId and amount wagons at runtime.
        /// </summary>
        [Fact]
        public void SeparateChains_PaymentRoute_UsesPaymentWagonNames()
        {
            var report = SeparateChainRoutes.Payment().DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(90m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that an order chain resolves orderId and total wagons at runtime.
        /// </summary>
        [Fact]
        public void SeparateChains_OrderRoute_UsesOrderWagonNames()
        {
            var report = SeparateChainRoutes.Order().DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("ord-1", report.TerminalSignal.Manifest.PullWagon<string>("orderId"));
            Assert.Equal(51m, report.TerminalSignal.Manifest.PullWagon<decimal>("total"));
        }

        /// <summary>
        /// Verifies that two separate chains with the same handler signature do not cross-contaminate wagon bindings.
        /// </summary>
        [Fact]
        public void SeparateChains_BothRoutesRunIndependently_WithoutCrossContamination()
        {
            var paymentReport = SeparateChainRoutes.Payment().DispatchTrain().Travel();
            var orderReport = SeparateChainRoutes.Order().DispatchTrain().Travel();

            Assert.Equal(90m, paymentReport.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal(51m, orderReport.TerminalSignal.Manifest.PullWagon<decimal>("total"));
            Assert.False(paymentReport.TerminalSignal.Manifest.HasWagon("orderId"));
            Assert.False(paymentReport.TerminalSignal.Manifest.HasWagon("total"));
            Assert.False(orderReport.TerminalSignal.Manifest.HasWagon("paymentId"));
            Assert.False(orderReport.TerminalSignal.Manifest.HasWagon("amount"));
        }

        /// <summary>
        /// Verifies that void handlers in separate chains mutate the correct manifest wagons.
        /// </summary>
        [Fact]
        public void SeparateChains_VoidHandlers_MutateCorrectWagons()
        {
            var paymentReport = VoidChainRoutes.Payment().DispatchTrain().Travel();
            var orderReport = VoidChainRoutes.Order().DispatchTrain().Travel();

            Assert.Equal("pay-1-touched", paymentReport.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(101m, paymentReport.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal("ord-1-touched", orderReport.TerminalSignal.Manifest.PullWagon<string>("orderId"));
            Assert.Equal(51m, orderReport.TerminalSignal.Manifest.PullWagon<decimal>("total"));
        }

        /// <summary>
        /// Verifies that local-variable route chains keep distinct wagon bindings at runtime.
        /// </summary>
        [Fact]
        public void LocalVariableChains_KeepDistinctWagonBindings()
        {
            var (paymentReport, orderReport) = LocalChainRoutes.BuildBoth();

            Assert.Equal(90m, paymentReport.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal(51m, orderReport.TerminalSignal.Manifest.PullWagon<decimal>("total"));
        }

        internal static class SeparateChainRoutes
        {
            public static TrainRoute Payment() => new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("Discount", (string paymentId, decimal amount) =>
                    new { paymentId, amount = amount * 0.9m });

            public static TrainRoute Order() => new TrainRoute()
                .Station("Seed", () => new { orderId = "ord-1", total = 50m })
                .Station("Validate", (string orderId, decimal total) =>
                    new { orderId, total = total + 1m });
        }

        internal static class VoidChainRoutes
        {
            public static TrainRoute Payment() => new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("Touch", (ref string paymentId, ref decimal amount) =>
                {
                    paymentId = paymentId + "-touched";
                    amount = amount + 1m;
                });

            public static TrainRoute Order() => new TrainRoute()
                .Station("Seed", () => new { orderId = "ord-1", total = 50m })
                .Station("Touch", (ref string orderId, ref decimal total) =>
                {
                    orderId = orderId + "-touched";
                    total = total + 1m;
                });
        }

        internal static class LocalChainRoutes
        {
            public static (RouteReport Payment, RouteReport Order) BuildBoth()
            {
                var payment = new TrainRoute()
                    .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                    .Station("Discount", (string paymentId, decimal amount) =>
                        new { paymentId, amount = amount * 0.9m });

                var order = new TrainRoute()
                    .Station("Seed", () => new { orderId = "ord-1", total = 50m })
                    .Station("Validate", (string orderId, decimal total) =>
                        new { orderId, total = total + 1m });

                return (payment.DispatchTrain().Travel(), order.DispatchTrain().Travel());
            }
        }
    }
}
