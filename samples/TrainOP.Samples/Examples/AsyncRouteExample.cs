using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Асинхронные станции требуют TravelAsync.
/// </summary>
internal sealed class AsyncRouteExample : IExample
{
    public string Title => "4. Асинхронный маршрут (TravelAsync)";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .AttachStation("Seed", manifest =>
                manifest.LoadCar("counter", 10))
            .AttachStation("Multiply", async (manifest, token) =>
            {
                await Task.Delay(50, token);
                var counter = manifest.PullCar<int>("counter");
                return manifest.LoadCar("counter", counter * 2);
            });

        var report = route.DispatchTrain().TravelAsync().GetAwaiter().GetResult();

        ExampleOutput.WriteReport(report);
    }
}
