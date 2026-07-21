namespace TrainOP.Samples.CodeVolume
{
    /// <summary>
    /// Same checkout business rules as <see cref="ManualCheckoutPipeline"/> expressed with TrainOP.
    /// Between-station cancellation is handled by <c>TravelAsync(token)</c>.
    /// </summary>
    internal static class TrainOpCheckoutPipeline
    {
        /// <summary>
        /// Approximate non-blank, non-comment lines in this type (kept in sync for the comparison sample).
        /// </summary>
        public const int ApproximateLogicLines = 95;

        /// <summary>
        /// Runs the TrainOP checkout pipeline.
        /// </summary>
        public static async Task<CheckoutResult> RunAsync(
            CheckoutRequest request,
            CancellationToken cancellationToken)
        {
            var report = await BuildRoute(request)
                .DispatchTrain()
                .TravelAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!report.ReachedDestination)
            {
                var issueStation = report.TerminalSignal is RedSignal red
                    ? red.Issue.StationName
                    : "Unknown";
                return CheckoutResult.Fail(
                    report.FailureCode ?? "FAILED",
                    issueStation ?? "Unknown",
                    report.FailureMessage ?? "route stopped");
            }

            return CheckoutResult.Ok(
                report.Get<string>("orderId"),
                report.Get<decimal>("amount"),
                report.Get<int>("units"),
                report.Get<string>("status"));
        }

        private static TrainRoute BuildRoute(CheckoutRequest request) => new TrainRoute()
            .Station("Seed", () => new
            {
                orderId = request.OrderId,
                amount = request.Amount,
                units = request.Units,
                customerId = request.CustomerId,
            })
            .Station("Validate", (string orderId, decimal amount, int units, string customerId) =>
            {
                orderId = orderId == null ? string.Empty : orderId.Trim();
                if (string.IsNullOrEmpty(orderId) || amount <= 0m || units <= 0 || string.IsNullOrEmpty(customerId))
                {
                    return RailwaySignals.Red("INVALID_ORDER", "order payload is invalid");
                }

                return RailwaySignals.Green(new { orderId, amount, units, customerId });
            })
            .Station("Loyalty", (string orderId, decimal amount, int units, string customerId) =>
            {
                var factor = customerId.StartsWith("VIP", StringComparison.Ordinal) ? 1.10m : 1.05m;
                return RailwaySignals.Green(new { orderId, amount = amount * factor, units, customerId });
            })
            .Station("Shipping", (string orderId, decimal amount, int units, string customerId) =>
                RailwaySignals.Green(new { orderId, amount = amount + units * 2m, units, customerId }))
            .Station("ReserveStock", (string orderId, decimal amount, int units, string customerId) =>
                units > 10
                    ? RailwaySignals.Red("STOCK_LIMIT", "requested units exceed stock")
                    : RailwaySignals.Green(new { orderId, amount, units, customerId }))
            .Station("AntiFraud", (string orderId, decimal amount, int units, string customerId) =>
                amount >= 500m
                    ? RailwaySignals.Red("FRAUD_REVIEW", "amount exceeds auto-approval limit")
                    : RailwaySignals.Green(new { orderId, amount, units, customerId }))
            .Station("Charge", (string orderId, decimal amount, int units, string customerId) =>
                amount <= 0m
                    ? RailwaySignals.Red("PAYMENT_REJECTED", "amount must be positive")
                    : RailwaySignals.Green(new { orderId, amount, units, customerId }))
            .ServiceStation("Recover", (ref int units, RedSignal red) =>
            {
                if (red.Issue.Code == "STOCK_LIMIT")
                {
                    units = 10;
                    return RailwaySignals.Pass;
                }

                return RailwaySignals.Red("UNRECOVERABLE", "cannot recover from " + red.Issue.Code);
            })
            .Station("Confirm", (string orderId, decimal amount, int units, string customerId) =>
                RailwaySignals.Green(new
                {
                    orderId,
                    amount = decimal.Round(amount, 2),
                    units,
                    customerId,
                    status = "captured",
                }));
    }
}
