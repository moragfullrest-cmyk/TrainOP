namespace TrainOP.RouteLib.Tests;

/// <summary>
/// Sample route module exported from a class library.
/// </summary>
public static class PaymentModule
{
    /// <summary>
    /// Builds a payment route seed and discount chain.
    /// </summary>
    public static TrainRoute Build() => new TrainRoute()
        .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
        .Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });
}
