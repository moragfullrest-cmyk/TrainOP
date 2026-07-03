namespace TrainOP.Samples;

/// <summary>
/// Demonstrates that a red signal stops the train and skips downstream stations.
/// </summary>
internal sealed class RedSignalStopExample : IExample
{
    public string Title => "2. Остановка на красном сигнале";

    /// <summary>
    /// Runs a route where validation fails and a later station must not execute.
    /// </summary>
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
