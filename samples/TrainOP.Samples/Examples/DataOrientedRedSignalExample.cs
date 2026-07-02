using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Data.Fail in a handler (no RailwaySignals) plus AttachRedSignalStation recovery.
/// </summary>
internal sealed class DataOrientedRedSignalExample : IExample
{
    public string Title => "9. Data.Fail + восстановление (без RailwaySignals в handler)";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-recover", amount = -10m })
            .Station("Validate", (string paymentId, decimal amount) =>
                amount > 0
                    ? Data.Ok(new { paymentId, amount })
                    : Data.Fail("INVALID_TOTAL", "amount must be positive"))
            .AttachRedSignalStation("Recovery", red =>
                RailwaySignals.Green(red.Manifest.LoadCar("amount", 50m)))
            .Station("ApplyDiscount", (string paymentId, decimal amount) =>
                (amount: amount * 0.9m, paymentId));

        var report = route.DispatchTrain().Travel();
        var (paymentId, amount) = report;

        Console.WriteLine($"Recovered: paymentId={paymentId}, amount={amount}");
        ExampleOutput.WriteReport(report);
    }
}
