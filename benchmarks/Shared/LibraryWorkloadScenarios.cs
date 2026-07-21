using System.Threading;

namespace TrainOP.Benchmarks.Caller
{
    /// <summary>
    /// TrainOP workloads used for library-vs-manual speed comparison.
    /// </summary>
    public static class LibraryWorkloadScenarios
    {
        /// <summary>
        /// Checkout pipeline with validation, pricing, stock clamp, fraud gate, and rounding.
        /// </summary>
        public static decimal BuildAndTravelCheckout(CancellationToken cancellationToken = default)
        {
            var report = CheckoutRoute().DispatchTrain().Travel(cancellationToken);
            return report.TerminalSignal.Manifest.PullWagon<decimal>("amount");
        }

        /// <summary>
        /// Pre-builds the checkout train for travel-only benchmarks.
        /// </summary>
        public static Train CreateCheckoutTrain() => CheckoutRoute().DispatchTrain();

        /// <summary>
        /// Travels a pre-built checkout train.
        /// </summary>
        public static decimal Travel(Train train, CancellationToken cancellationToken = default)
        {
            var report = train.Travel(cancellationToken);
            return report.TerminalSignal.Manifest.PullWagon<decimal>("amount");
        }

        private static TrainRoute CheckoutRoute() => new TrainRoute()
            .Station("Seed", () => new { orderId = "ORD-1001", amount = 200m, units = 8, currency = "USD" })
            .Station("Validate", (string orderId, decimal amount, int units, string currency, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                orderId = orderId.Trim();
                if (string.IsNullOrEmpty(orderId) || amount <= 0m || units <= 0)
                {
                    return RailwaySignals.Red("INVALID_ORDER", "order payload is invalid");
                }

                return RailwaySignals.Green(new { orderId, amount, units, currency });
            })
            .Station("Loyalty", (string orderId, decimal amount, int units, string currency, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return RailwaySignals.Green(new { orderId, amount = amount * 1.05m, units, currency });
            })
            .Station("Shipping", (string orderId, decimal amount, int units, string currency, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return RailwaySignals.Green(new { orderId, amount = amount + units * 2m, units, currency });
            })
            .Station("Stock", (string orderId, decimal amount, int units, string currency, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                if (units > 10)
                {
                    return RailwaySignals.Red("STOCK_LIMIT", "requested units exceed stock");
                }

                return RailwaySignals.Green(new { orderId, amount, units, currency });
            })
            .Station("AntiFraud", (string orderId, decimal amount, int units, string currency, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                if (amount >= 500m)
                {
                    return RailwaySignals.Red("FRAUD_REVIEW", "amount exceeds auto-approval limit");
                }

                return RailwaySignals.Green(new { orderId, amount, units, currency });
            })
            .ServiceStation("Recover", (ref int units, RedSignal red) =>
            {
                if (red.Issue.Code == "STOCK_LIMIT")
                {
                    units = 10;
                    return RailwaySignals.Pass;
                }

                return RailwaySignals.Red("UNRECOVERABLE", "cannot recover from " + red.Issue.Code);
            })
            .Station("Round", (string orderId, decimal amount, int units, string currency, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return RailwaySignals.Green(new { orderId, amount = decimal.Round(amount, 2), units, currency });
            });
    }
}
