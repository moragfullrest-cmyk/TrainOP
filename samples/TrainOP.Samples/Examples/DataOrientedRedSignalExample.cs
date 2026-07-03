using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// RailwaySignals.Red in a handler plus ServiceStation recovery.
/// </summary>
internal sealed class DataOrientedRedSignalExample : IExample
{
    public string Title => "6. Green/Red + восстановление";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-recover", amount = -10m })
            .Station("Validate", (string paymentId, decimal amount) =>
                amount > 0
                    ? RailwaySignals.Green(new { paymentId, amount })
                    : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
            .ServiceStation("Recovery", (string paymentId, decimal amount) =>
                RailwaySignals.Green(new { paymentId, amount = 50m }))
            .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m });

        var report = route.DispatchTrain().Travel();
        ExampleOutput.WriteReport(report);
    }
}
