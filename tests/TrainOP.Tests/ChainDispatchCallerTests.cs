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
            var report = SeparateChainRoutes.Payment().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(90m, report.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that an order chain resolves orderId and total wagons at runtime.
        /// </summary>
        [Fact]
        public void SeparateChains_OrderRoute_UsesOrderWagonNames()
        {
            var report = SeparateChainRoutes.Order().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("ord-1", report.Manifest.PullWagon<string>("orderId"));
            Assert.Equal(51m, report.Manifest.PullWagon<decimal>("total"));
        }

        /// <summary>
        /// Verifies that two separate chains with the same handler signature do not cross-contaminate wagon bindings.
        /// </summary>
        [Fact]
        public void SeparateChains_BothRoutesRunIndependently_WithoutCrossContamination()
        {
            var paymentReport = SeparateChainRoutes.Payment().Travel();
            var orderReport = SeparateChainRoutes.Order().Travel();

            Assert.Equal(90m, paymentReport.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal(51m, orderReport.Manifest.PullWagon<decimal>("total"));
            Assert.False(paymentReport.Manifest.HasWagon("orderId"));
            Assert.False(paymentReport.Manifest.HasWagon("total"));
            Assert.False(orderReport.Manifest.HasWagon("paymentId"));
            Assert.False(orderReport.Manifest.HasWagon("amount"));
        }

        /// <summary>
        /// Verifies factory-extension chain-dispatch keeps distinct wagon names for the same CLR handler shape.
        /// </summary>
        [Fact]
        public void FactoryExtension_ConflictingIntSignatures_ReadsCorrectWagonNames()
        {
            var alphaReport = FactoryExtensionConflictRoutes.Alpha().Travel();
            var betaReport = FactoryExtensionConflictRoutes.Beta().Travel();

            Assert.True(alphaReport.ReachedDestination);
            Assert.True(betaReport.ReachedDestination);
            Assert.Equal(1, alphaReport.Manifest.PullWagon<int>("alpha"));
            Assert.Equal(2, betaReport.Manifest.PullWagon<int>("beta"));
            Assert.False(alphaReport.Manifest.HasWagon("beta"));
            Assert.False(betaReport.Manifest.HasWagon("alpha"));
        }

        /// <summary>
        /// Verifies factory with two inner stations + consumer extension uses ordinal offset at runtime.
        /// </summary>
        [Fact]
        public void FactoryExtension_TwoInnerStations_ConsumerReadsFactoryWagons()
        {
            var report = FactoryExtensionOffsetRoutes.Build().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("p", report.Manifest.PullWagon<string>("paymentId"));
            Assert.True(report.Manifest.PullWagon<bool>("ok"));
            Assert.Equal(3, report.Visits.Count);
            Assert.Equal("Finalize", report.Visits[2].StationName);
        }

        /// <summary>
        /// Verifies that void handlers in separate chains mutate the correct manifest wagons.
        /// </summary>
        [Fact]
        public void SeparateChains_VoidHandlers_MutateCorrectWagons()
        {
            var paymentReport = VoidChainRoutes.Payment().Travel();
            var orderReport = VoidChainRoutes.Order().Travel();

            Assert.Equal("pay-1-touched", paymentReport.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(101m, paymentReport.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal("ord-1-touched", orderReport.Manifest.PullWagon<string>("orderId"));
            Assert.Equal(51m, orderReport.Manifest.PullWagon<decimal>("total"));
        }

        /// <summary>
        /// Verifies that local-variable route chains keep distinct wagon bindings at runtime.
        /// </summary>
        [Fact]
        public void LocalVariableChains_KeepDistinctWagonBindings()
        {
            var (paymentReport, orderReport) = LocalChainRoutes.BuildBoth();

            Assert.Equal(90m, paymentReport.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal(51m, orderReport.Manifest.PullWagon<decimal>("total"));
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

        internal static class FactoryExtensionConflictRoutes
        {
            public static TrainRoute Alpha() => new TrainRoute()
                .Station("Seed", () => new { alpha = 1 })
                .Station("Use", (int alpha) => new { alpha });

            public static TrainRoute Beta() => CreateBetaSeed()
                .Station("Use", (int beta) => new { beta });

            private static TrainRoute CreateBetaSeed() => new TrainRoute()
                .Station("Seed", () => new { beta = 2 });
        }

        internal static class FactoryExtensionOffsetRoutes
        {
            public static TrainRoute Build() => CreateSeed()
                .Station("Finalize", (string paymentId) => new { paymentId, ok = true });

            private static TrainRoute CreateSeed() => new TrainRoute()
                .Station("Seed", () => new { paymentId = "p" })
                .Station("Step", (string paymentId) => new { paymentId });
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

                return (payment.Travel(), order.Travel());
            }
        }
    }
}
