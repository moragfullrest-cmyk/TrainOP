using Xunit;

namespace TrainOP.ReflectionDispatch.Tests
{
    /// <summary>
    /// End-to-end chain-dispatch tests with <c>TrainOP_ChainDispatchMode=reflection</c>.
    /// </summary>
    public sealed class ReflectionChainDispatchTests
    {
        /// <summary>
        /// Verifies payment and order chains keep distinct wagon bindings via ParameterInfo.
        /// </summary>
        [Fact]
        public void SeparateChains_RunIndependently_WithoutCrossContamination()
        {
            var paymentReport = SeparateChainRoutes.Payment().DispatchTrain().Travel();
            var orderReport = SeparateChainRoutes.Order().DispatchTrain().Travel();

            Assert.Equal(90m, paymentReport.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal(51m, orderReport.TerminalSignal.Manifest.PullWagon<decimal>("total"));
            Assert.False(paymentReport.TerminalSignal.Manifest.HasWagon("orderId"));
            Assert.False(orderReport.TerminalSignal.Manifest.HasWagon("paymentId"));
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
    }
}
