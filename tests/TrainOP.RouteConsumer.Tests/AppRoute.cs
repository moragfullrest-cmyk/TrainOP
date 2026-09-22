using TrainOP.RouteLib.Tests;

namespace TrainOP.RouteConsumer.Tests;

/// <summary>
/// Extends a route module from a referenced assembly.
/// </summary>
public static class AppRoute
{
    /// <summary>
    /// Adds a finalize station to the shared payment route (fluent).
    /// </summary>
    public static TrainRoute Build() =>
        PaymentModule.Build()
            .Station("Finalize", (decimal amount, string paymentId) =>
                new { paymentId, amount, status = "completed" });

    /// <summary>
    /// Adds a finalize station via statement-local extension after the public factory.
    /// </summary>
    public static TrainRoute BuildStatementLocal()
    {
        var route = PaymentModule.Build();
        route.Station("Finalize", (decimal amount, string paymentId) =>
            new { paymentId, amount, status = "completed" });
        return route;
    }
}
