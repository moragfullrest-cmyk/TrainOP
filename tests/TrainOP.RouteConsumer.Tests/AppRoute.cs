using TrainOP;
using TrainOP.RouteLib.Tests;

namespace TrainOP.RouteConsumer.Tests;

/// <summary>
/// Extends a route module from a referenced assembly.
/// </summary>
public static class AppRoute
{
    /// <summary>
    /// Adds a finalize station to the shared payment route.
    /// </summary>
    public static TrainRoute Build() =>
        PaymentModule.Build()
            .Station("Finalize", (decimal amount, string paymentId) =>
                new { paymentId, amount, status = "completed" });
}
