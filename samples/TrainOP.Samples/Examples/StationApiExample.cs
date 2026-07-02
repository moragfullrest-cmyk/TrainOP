using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// TrainRoute.Station — синоним AttachStation для единого железнодорожного стиля API.
/// Паттерн static Build() — якорь для будущего analyzer (data-oriented handlers).
/// </summary>
internal sealed class StationApiExample : IExample
{
    public string Title => "7. TrainRoute.Station и static Build()";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var report = PaymentRoute.Build().DispatchTrain().Travel();

        ExampleOutput.WriteReport(report);
    }

    private static class PaymentRoute
    {
        public static TrainRoute Build()
        {
            return new TrainRoute()
                .AttachStation("Seed", manifest =>
                    manifest
                        .LoadCar("paymentId", "station-api")
                        .LoadCar("amount", 50m))
                .AttachStation("Double", manifest =>
                    manifest.LoadCar("amount", manifest.PullCar<decimal>("amount") * 2m));
        }
    }
}
