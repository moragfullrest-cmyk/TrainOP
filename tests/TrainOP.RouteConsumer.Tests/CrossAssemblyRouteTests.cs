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
        var report = AppRoute.Build().Travel();

        Assert.True(report.ReachedDestination);
        Assert.Equal("pay-1", report.Get<string>("paymentId"));
        Assert.Equal("completed", report.Get<string>("status"));
        Assert.Equal(90m, report.Get<decimal>("amount"));
    }

    /// <summary>
    /// Verifies factory-extension chain-dispatch across assemblies keeps alpha/beta wagon names distinct.
    /// </summary>
    [Fact]
    public void Travel_FactoryExtension_ConflictingIntSignatures_ReadsCorrectWagons()
    {
        var alpha = ConflictAppRoute.Alpha().Travel();
        var beta = ConflictAppRoute.Beta().Travel();

        Assert.True(alpha.ReachedDestination);
        Assert.True(beta.ReachedDestination);
        Assert.Equal(1, alpha.Get<int>("alpha"));
        Assert.Equal(2, beta.Get<int>("beta"));
        Assert.Equal(2, beta.Visits.Count);
        Assert.Equal("Use", beta.Visits[1].StationName);
        Assert.False(alpha.Manifest.HasWagon("beta"));
        Assert.False(beta.Manifest.HasWagon("alpha"));
    }
}
