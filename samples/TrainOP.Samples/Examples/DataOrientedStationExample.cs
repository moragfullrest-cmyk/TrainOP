namespace TrainOP.Samples;

/// <summary>
/// Demonstrates data-oriented handlers wired through TrainRoute.Station.
/// </summary>
internal sealed class DataOrientedStationExample : IExample
{
    public string Title => "1. Data-oriented маршрут";

    /// <summary>
    /// Runs a multi-station route with anonymous-type wagons and reads terminal values from the report.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "data-route", amount = 100m })
            .Station("Discount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m })
            .Station("Validate", (string paymentId, decimal amount) =>
                amount > 0
                    ? RailwaySignals.Green(new { paymentId, amount })
                    : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"));

        var report = route.DispatchTrain().Travel();
        var paymentId = report.Get<string>("paymentId");
        var amount = report.Get<decimal>("amount");

        Console.WriteLine($"paymentId={paymentId}, amount={amount}");
        ExampleOutput.WriteReport(report);
    }
}
