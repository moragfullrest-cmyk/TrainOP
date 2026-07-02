using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// AttachRedSignalStation позволяет обработать красный сигнал и продолжить маршрут.
/// </summary>
internal sealed class RedSignalRecoveryExample : IExample
{
    public string Title => "6. Восстановление после красного сигнала";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .AttachStation("Validation", manifest =>
                RailwaySignals.Red(
                    manifest,
                    new SignalIssue("REQ_MISSING", "request-id is required", "Validation")))
            .AttachRedSignalStation("SignalControl", red =>
            {
                var recovered = red.Manifest.LoadCar("recovered", true);
                return RailwaySignals.Green(recovered);
            })
            .AttachStation("AfterRecovery", manifest =>
                manifest.LoadCar("after", "ok"));

        var report = route.DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }
}
