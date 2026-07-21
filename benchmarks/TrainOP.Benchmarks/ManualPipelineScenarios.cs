namespace TrainOP.Benchmarks
{
    /// <summary>
    /// Hand-written equivalents of the caller-dispatch benchmark workloads (no TrainOP types).
    /// </summary>
    public static class ManualPipelineScenarios
    {
        /// <summary>
        /// Same transforms as the short payment chain without route/manifest overhead.
        /// </summary>
        public static decimal TravelPayment()
        {
            const string paymentId = "pay-1";
            var amount = 100m;
            amount = amount * 0.9m;
            _ = paymentId;
            return amount;
        }

        /// <summary>
        /// Same transforms as the long payment pipeline without route/manifest overhead.
        /// </summary>
        public static decimal TravelLongPayment()
        {
            const string paymentId = "pay-1";
            var amount = 100m;
            amount = amount * 0.9m;
            amount = amount + 1.5m;
            amount = amount * 1.2m;
            amount = decimal.Round(amount, 2);
            _ = paymentId;
            return amount;
        }

        /// <summary>
        /// Imperative checkout happy-path with per-step cancellation checks and failure branching,
        /// mirroring <see cref="TrainOP.Benchmarks.Caller.LibraryWorkloadScenarios.BuildAndTravelCheckout"/>.
        /// </summary>
        public static decimal TravelCheckout(System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var orderId = "ORD-1001";
            var amount = 200m;
            var units = 8;
            var currency = "USD";

            cancellationToken.ThrowIfCancellationRequested();
            orderId = orderId.Trim();
            if (string.IsNullOrEmpty(orderId) || amount <= 0m || units <= 0)
            {
                throw new System.InvalidOperationException("INVALID_ORDER");
            }

            cancellationToken.ThrowIfCancellationRequested();
            amount = amount * 1.05m;

            cancellationToken.ThrowIfCancellationRequested();
            amount = amount + units * 2m;

            cancellationToken.ThrowIfCancellationRequested();
            if (units > 10)
            {
                units = 10;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (amount >= 500m)
            {
                throw new System.InvalidOperationException("FRAUD_REVIEW");
            }

            cancellationToken.ThrowIfCancellationRequested();
            amount = decimal.Round(amount, 2);
            _ = currency;
            return amount;
        }
    }
}
