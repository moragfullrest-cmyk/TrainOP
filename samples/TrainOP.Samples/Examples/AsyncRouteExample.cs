namespace TrainOP.Samples;

/// <summary>
/// Demonstrates asynchronous station handlers that require TravelAsync.
/// </summary>
internal sealed class AsyncRouteExample : IExample
{
    public string Title => "4. Асинхронный маршрут (TravelAsync)";

    /// <summary>
    /// Runs a route with an async station and blocks on TravelAsync to obtain the report.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { counter = 10 })
            .StationAsync("Multiply", async (int counter, CancellationToken token) =>
            {
                await Task.Delay(50, token);
                return new { counter = counter * 2 };
            });

        var report = route.DispatchTrain().TravelAsync().GetAwaiter().GetResult();

        ExampleOutput.WriteReport(report);
    }
}
