using Xunit;

namespace TrainOP.RouteConsumer.Tests;

/// <summary>
/// End-to-end checks for cross-assembly route composition.
/// </summary>
public sealed class CrossAssemblyRouteTests
{
    /// <summary>
    /// Verifies that composed routes execute successfully at runtime.
    /// </summary>
    [Fact]
    public void Travel_ComposedRoute_CompletesSuccessfully()
    {
        var report = AppRoute.Build().DispatchTrain().Travel();

        Assert.True(report.ReachedDestination);
        Assert.Equal("pay-1", report.Get<string>("paymentId"));
        Assert.Equal("completed", report.Get<string>("status"));
        Assert.Equal(90m, report.Get<decimal>("amount"));
    }
}
