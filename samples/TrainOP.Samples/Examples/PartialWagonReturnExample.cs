using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Станция может вернуть только часть вагонов — остальные сохраняются в манифесте.
/// </summary>
internal sealed class PartialWagonReturnExample : IExample
{
    public string Title => "5. Частичный возврат вагонов (merge в манифест)";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
            .Station("PartialUpdate", (CargoManifest manifest, string paymentId, decimal amount) =>
                new { paymentId = paymentId + "-" + manifest.PullCar<string>("traceId") });

        var report = route.DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }
}
