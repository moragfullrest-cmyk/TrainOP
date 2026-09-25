namespace TrainOP.Samples;

/// <summary>
/// Demonstrates seed wagons created through out parameters and a ref readonly wagon that stays in the manifest.
/// </summary>
internal sealed class OutAndRefReadonlyExample : IExample
{
    public string Title => "12. out и ref readonly";

    /// <summary>
    /// Seeds paymentId and amount via out, keeps paymentId, and adds status via out.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", (out string paymentId, out decimal amount) =>
            {
                paymentId = "pay-1";
                amount = 100m;
            })
            .Station("Discount", (ref readonly string paymentId, decimal amount, out string status) =>
            {
                status = "discounted";
                return new { amount = amount * 0.9m };
            });

        var report = route.Travel();
        var keptId = report.Get<string>("paymentId");
        var amount = report.Get<decimal>("amount");
        var status = report.Get<string>("status");

        Console.WriteLine($"paymentId={keptId}, amount={amount}, status={status}");
        ExampleOutput.WriteReport(report);
    }
}
