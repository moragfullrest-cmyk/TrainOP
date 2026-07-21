namespace TrainOP.Samples;

/// <summary>
/// Public route factory exported from the samples assembly.
/// Triggers <c>TrainRoute.RouteSchemas.g.cs</c> emission for cross-assembly composition.
/// </summary>
public static class ExportedPaymentRoute
{
    /// <summary>
    /// Builds a payment route seed and discount chain.
    /// </summary>
    public static TrainRoute Build() => new TrainRoute()
        .Station("Seed", () => new { paymentId = "pay-exported", amount = 100m })
        .Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}
