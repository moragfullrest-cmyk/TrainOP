namespace TrainOP.Samples.CodeVolume
{
    /// <summary>
    /// Imperative checkout pipeline without TrainOP: tokens, failures, recovery, and result plumbing by hand.
    /// </summary>
    internal static class ManualCheckoutPipeline
    {
        /// <summary>
        /// Approximate non-blank, non-comment lines in this type (kept in sync for the comparison sample).
        /// </summary>
        public const int ApproximateLogicLines = 122;

        /// <summary>
        /// Runs the manual checkout pipeline.
        /// </summary>
        public static async Task<CheckoutResult> RunAsync(
            CheckoutRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var orderId = request.OrderId == null ? string.Empty : request.OrderId.Trim();
                var amount = request.Amount;
                var units = request.Units;
                var customerId = request.CustomerId;

                if (string.IsNullOrEmpty(orderId) || amount <= 0m || units <= 0 || string.IsNullOrEmpty(customerId))
                {
                    return CheckoutResult.Fail("INVALID_ORDER", "Validate", "order payload is invalid");
                }

                cancellationToken.ThrowIfCancellationRequested();
                amount = await ApplyLoyaltyAsync(amount, customerId, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();
                amount = amount + units * 2m;

                cancellationToken.ThrowIfCancellationRequested();
                var stock = await ReserveStockAsync(orderId, units, cancellationToken).ConfigureAwait(false);
                if (!stock.Ok)
                {
                    if (stock.Code == "STOCK_LIMIT")
                    {
                        units = 10;
                    }
                    else
                    {
                        return CheckoutResult.Fail(stock.Code, "ReserveStock", stock.Message);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (amount >= 500m)
                {
                    return CheckoutResult.Fail("FRAUD_REVIEW", "AntiFraud", "amount exceeds auto-approval limit");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var charge = await ChargeAsync(orderId, amount, cancellationToken).ConfigureAwait(false);
                if (!charge.Ok)
                {
                    return CheckoutResult.Fail(charge.Code, "Charge", charge.Message);
                }

                cancellationToken.ThrowIfCancellationRequested();
                amount = decimal.Round(amount, 2);
                return CheckoutResult.Ok(orderId, amount, units, "captured");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return CheckoutResult.Fail(
                    "UNHANDLED",
                    "ManualCheckout",
                    "Unhandled pipeline exception: " + exception.Message);
            }
        }

        private static async Task<decimal> ApplyLoyaltyAsync(
            decimal amount,
            string customerId,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return customerId.StartsWith("VIP", StringComparison.Ordinal)
                ? amount * 1.10m
                : amount * 1.05m;
        }

        private static async Task<StepResult> ReserveStockAsync(
            string orderId,
            int units,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            _ = orderId;
            if (units > 10)
            {
                return StepResult.Fail("STOCK_LIMIT", "requested units exceed stock");
            }

            return StepResult.Success();
        }

        private static async Task<StepResult> ChargeAsync(
            string orderId,
            decimal amount,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            _ = orderId;
            if (amount <= 0m)
            {
                return StepResult.Fail("PAYMENT_REJECTED", "amount must be positive");
            }

            return StepResult.Success();
        }

        private readonly struct StepResult
        {
            private StepResult(bool ok, string code, string message)
            {
                Ok = ok;
                Code = code;
                Message = message;
            }

            public bool Ok { get; }

            public string Code { get; }

            public string Message { get; }

            public static StepResult Success() => new StepResult(true, null, null);

            public static StepResult Fail(string code, string message) => new StepResult(false, code, message);
        }
    }
}
