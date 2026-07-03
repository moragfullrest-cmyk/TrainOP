using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Красный сигнал останавливает поезд; следующие станции не выполняются.
/// </summary>
internal sealed class RedSignalStopExample : IExample
{
    public string Title => "2. Остановка на красном сигнале";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { requestId = "" })
            .Station("Validation", (string requestId) =>
                !string.IsNullOrEmpty(requestId)
                    ? RailwaySignals.Green(new { requestId })
                    : RailwaySignals.Red("REQ_MISSING", "request-id is required"))
            .Station("MustNotRun", (string requestId) => new { forbidden = true });

        var report = route.DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }
}
