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
            .AttachStation("Validation", manifest =>
                RailwaySignals.Red(
                    manifest,
                    new SignalIssue("REQ_MISSING", "request-id is required", "Validation")))
            .AttachStation("MustNotRun", manifest =>
                manifest.LoadCar("forbidden", true));

        var report = route.DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }
}
