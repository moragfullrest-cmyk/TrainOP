namespace TrainOP.Samples;

/// <summary>
/// Demonstrates RailwaySignals.Red in a handler followed by ServiceStation recovery.
/// </summary>
internal sealed class DataOrientedRedSignalExample : IExample
{
    public string Title => "6. Green/Red + восстановление";

    /// <summary>
    /// Runs a route that fails validation, recovers in a service station, and completes downstream processing.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-recover", amount = -10m })
            .Station("Validate", (string paymentId, decimal amount) =>
                amount > 0
                    ? RailwaySignals.Green(new { paymentId, amount })
                    : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
            .ServiceStation("Recovery", (ref string paymentId, ref decimal amount, RedSignal red) =>
            {
                paymentId = "pay-recover";
                amount = 50m;
                return RailwaySignals.Pass;
            })
            .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });

        var report = route.DispatchTrain().Travel();
        ExampleOutput.WriteReport(report);
    }
}
