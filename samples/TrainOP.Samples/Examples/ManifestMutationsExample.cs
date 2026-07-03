namespace TrainOP.Samples;

/// <summary>
/// Demonstrates that returning only part of the manifest removes omitted wagons from the train.
/// </summary>
internal sealed class ManifestMutationsExample : IExample
{
    public string Title => "3. Мутации данных между станциями";

    /// <summary>
    /// Runs a route where a station returns a subset of wagons and drops the rest from the manifest.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { counter = 1, temporary = "keep" })
            .Station("Mutate", (int counter, string temporary) => new { counter = counter + 41 });

        var report = route.DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }
}
