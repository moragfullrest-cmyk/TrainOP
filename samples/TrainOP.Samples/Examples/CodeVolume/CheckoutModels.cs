namespace TrainOP.Samples.CodeVolume
{
    /// <summary>
    /// Shared input for the manual vs TrainOP checkout comparison.
    /// </summary>
    internal sealed class CheckoutRequest
    {
        public CheckoutRequest(string orderId, decimal amount, int units, string customerId)
        {
            OrderId = orderId;
            Amount = amount;
            Units = units;
            CustomerId = customerId;
        }

        public string OrderId { get; }

        public decimal Amount { get; }

        public int Units { get; }

        public string CustomerId { get; }
    }

    /// <summary>
    /// Shared outcome shape for the manual vs TrainOP checkout comparison.
    /// </summary>
    internal sealed class CheckoutResult
    {
        private CheckoutResult(
            bool success,
            string orderId,
            decimal amount,
            int units,
            string status,
            string failureCode,
            string failureStation,
            string failureMessage)
        {
            Success = success;
            OrderId = orderId;
            Amount = amount;
            Units = units;
            Status = status;
            FailureCode = failureCode;
            FailureStation = failureStation;
            FailureMessage = failureMessage;
        }

        public bool Success { get; }

        public string OrderId { get; }

        public decimal Amount { get; }

        public int Units { get; }

        public string Status { get; }

        public string FailureCode { get; }

        public string FailureStation { get; }

        public string FailureMessage { get; }

        public static CheckoutResult Ok(string orderId, decimal amount, int units, string status) =>
            new CheckoutResult(true, orderId, amount, units, status, null, null, null);

        public static CheckoutResult Fail(string code, string station, string message) =>
            new CheckoutResult(false, null, 0m, 0, null, code, station, message);
    }
}
