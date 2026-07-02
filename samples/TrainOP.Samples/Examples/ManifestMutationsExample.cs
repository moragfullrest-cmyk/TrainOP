using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Неизменяемый манифест: LoadCar заменяет вагон, UnloadCar удаляет.
/// </summary>
internal sealed class ManifestMutationsExample : IExample
{
    public string Title => "3. Мутации манифеста между станциями";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .AttachStation("Seed", manifest =>
                manifest
                    .LoadCar("counter", 1)
                    .LoadCar("temporary", "keep"))
            .AttachStation("Mutate", manifest =>
            {
                var counter = manifest.PullCar<int>("counter");
                return manifest
                    .LoadCar("counter", counter + 41)
                    .UnloadCar("temporary");
            });

        var report = route.DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }
}
