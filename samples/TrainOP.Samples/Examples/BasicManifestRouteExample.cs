using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Минимальный маршрут: валидация манифеста и обогащение данных.
/// См. docs/getting-started.md
/// </summary>
internal sealed class BasicManifestRouteExample : IExample
{
    public string Title => "1. Базовый маршрут (AttachStation + CargoManifest)";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .AttachStation("Validation", (Func<CargoManifest, Signal>)(manifest =>
            {
                if (!manifest.HasCar("request-id"))
                {
                    return RailwaySignals.Red(
                        manifest,
                        new SignalIssue("REQ_MISSING", "request-id is required", "Validation"));
                }

                return RailwaySignals.Green(manifest);
            }))
            .AttachStation("Enrichment", manifest =>
                manifest.LoadCar("processed-at", DateTime.UtcNow));

        var start = new CargoManifest().LoadCar("request-id", "abc-123");
        var report = route.DispatchTrain().Travel(start);

        ExampleOutput.WriteReport(report);
    }
}
