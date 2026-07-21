namespace TrainOP.Benchmarks.Caller
{
    /// <summary>
    /// Chain-dispatch workloads for benchmarks.
    /// Call sites must live in the consuming assembly so the generator can emit caller dispatch code.
    /// </summary>
    public static class ChainDispatchScenarios
    {
        /// <summary>
        /// Builds the payment chain and travels once.
        /// </summary>
        public static decimal BuildAndTravelPayment()
        {
            var report = PaymentRoute().DispatchTrain().Travel();
            return report.TerminalSignal.Manifest.PullWagon<decimal>("amount");
        }

        /// <summary>
        /// Builds the order chain and travels once.
        /// </summary>
        public static decimal BuildAndTravelOrder()
        {
            var report = OrderRoute().DispatchTrain().Travel();
            return report.TerminalSignal.Manifest.PullWagon<decimal>("total");
        }

        /// <summary>
        /// Builds both conflicting-signature chains and travels each once.
        /// </summary>
        public static decimal BuildAndTravelBothChains()
        {
            var payment = BuildAndTravelPayment();
            var order = BuildAndTravelOrder();
            return payment + order;
        }

        /// <summary>
        /// Builds a longer payment pipeline (registration + travel).
        /// </summary>
        public static decimal BuildAndTravelLongPayment()
        {
            var report = LongPaymentRoute().DispatchTrain().Travel();
            return report.TerminalSignal.Manifest.PullWagon<decimal>("amount");
        }

        /// <summary>
        /// Creates a reusable train for travel-only benchmarks.
        /// </summary>
        public static Train CreatePaymentTrain() => PaymentRoute().DispatchTrain();

        /// <summary>
        /// Creates a reusable long-route train for travel-only benchmarks.
        /// </summary>
        public static Train CreateLongPaymentTrain() => LongPaymentRoute().DispatchTrain();

        /// <summary>
        /// Travels an already built train.
        /// </summary>
        public static decimal Travel(Train train)
        {
            var report = train.Travel();
            return report.TerminalSignal.Manifest.PullWagon<decimal>("amount");
        }

        private static TrainRoute PaymentRoute() => new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
            .Station("Discount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });

        private static TrainRoute OrderRoute() => new TrainRoute()
            .Station("Seed", () => new { orderId = "ord-1", total = 50m })
            .Station("Validate", (string orderId, decimal total) =>
                new { orderId, total = total + 1m });

        private static TrainRoute LongPaymentRoute() => new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
            .Station("Discount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m })
            .Station("Fee", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount + 1.5m })
            .Station("Tax", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 1.2m })
            .Station("Round", (string paymentId, decimal amount) =>
                new { paymentId, amount = decimal.Round(amount, 2) });
    }
}
