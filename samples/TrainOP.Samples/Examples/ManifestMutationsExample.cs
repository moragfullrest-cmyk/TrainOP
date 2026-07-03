using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Частичный возврат из handler'а удаляет невозвращённые вагоны из манифеста.
/// </summary>
internal sealed class ManifestMutationsExample : IExample
{
    public string Title => "3. Мутации данных между станциями";

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
