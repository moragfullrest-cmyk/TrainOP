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
            var report = PaymentRoute().Travel();
            return report.Manifest.PullWagon<decimal>("amount");
        }

        /// <summary>
        /// Builds the order chain and travels once.
        /// </summary>
        public static decimal BuildAndTravelOrder()
        {
            var report = OrderRoute().Travel();
            return report.Manifest.PullWagon<decimal>("total");
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
            var report = LongPaymentRoute().Travel();
            return report.Manifest.PullWagon<decimal>("amount");
        }

        /// <summary>
        /// Creates a reusable payment route for travel-only benchmarks.
        /// </summary>
        public static TrainRoute CreatePaymentRoute() => PaymentRoute();

        /// <summary>
        /// Creates a reusable long payment route for travel-only benchmarks.
        /// </summary>
        public static TrainRoute CreateLongPaymentRoute() => LongPaymentRoute();

        /// <summary>
        /// Travels an already built route.
        /// </summary>
        public static decimal Travel(TrainRoute route)
        {
            var report = route.Travel();
            return report.Manifest.PullWagon<decimal>("amount");
        }

        /// <summary>
        /// Travels an already built route without recording visits.
        /// </summary>
        public static decimal TravelLight(TrainRoute route)
        {
            var report = route.TravelLight();
            return report.Manifest.PullWagon<decimal>("amount");
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
