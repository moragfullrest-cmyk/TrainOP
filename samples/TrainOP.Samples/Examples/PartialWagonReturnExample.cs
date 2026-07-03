namespace TrainOP.Samples;

/// <summary>
/// Demonstrates partial wagon returns where unreturned wagons remain in the manifest.
/// </summary>
internal sealed class PartialWagonReturnExample : IExample
{
    public string Title => "5. Частичный возврат вагонов (merge в манифест)";

    /// <summary>
    /// Runs a route where a station updates some wagons while others are preserved via manifest merge.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
            .Station("PartialUpdate", (CargoManifest manifest, string paymentId, decimal amount) =>
                new { paymentId = paymentId + "-" + manifest.PullWagon<string>("traceId") });

        var report = route.DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }
}
