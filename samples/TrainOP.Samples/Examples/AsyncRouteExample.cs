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
            .Station("Seed", () => new { counter = 10 })
            .Station("Multiply", async (int counter, CancellationToken token) =>
            {
                await Task.Delay(50, token);
                return new { counter = counter * 2 };
            });

        var report = route.DispatchTrain().TravelAsync().GetAwaiter().GetResult();

        ExampleOutput.WriteReport(report);
    }
}
