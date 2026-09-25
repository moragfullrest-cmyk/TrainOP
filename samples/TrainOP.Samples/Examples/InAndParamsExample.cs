namespace TrainOP.Samples;

/// <summary>
/// Demonstrates an <c>in</c> wagon that stays in the manifest and a <c>params</c> collection wagon.
/// </summary>
internal sealed class InAndParamsExample : IExample
{
    public string Title => "13. in и params";

    /// <summary>
    /// Keeps paymentId via <c>in</c> and replaces the tags collection.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-1", amount = 100m, tags = new[] { "rail" } })
            .Station("Keep", Keep);

        var report = route.Travel();
        Console.WriteLine(
            $"paymentId={report.Get<string>("paymentId")}, amount={report.Get<decimal>("amount")}, tags={string.Join(",", report.Get<string[]>("tags"))}");
        ExampleOutput.WriteReport(report);

        static object Keep(in string paymentId, decimal amount, params string[] tags) =>
            new { amount = amount * 0.9m, tags = new[] { tags[0], "yard" } };
    }
}
