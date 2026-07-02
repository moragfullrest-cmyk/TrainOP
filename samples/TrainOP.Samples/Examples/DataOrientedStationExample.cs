using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Data-oriented handlers via TrainRoute.Station.
/// </summary>
internal sealed class DataOrientedStationExample : IExample
{
    public string Title => "8. Data-oriented Station";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "data-route", amount = 100m })
            .Station("Discount", (string paymentId, decimal amount) =>
                new { paymentId, amount = amount * 0.9m })
            .Station("Validate", (string paymentId, decimal amount) =>
                amount > 0
                    ? Data.Ok(new { paymentId, amount })
                    : Data.Fail("INVALID_TOTAL", "amount must be positive"));

        var report = route.DispatchTrain().Travel();
        var (paymentId, amount) = report;

        Console.WriteLine($"paymentId={paymentId}, amount={amount}");
        ExampleOutput.WriteReport(report);
    }
}
