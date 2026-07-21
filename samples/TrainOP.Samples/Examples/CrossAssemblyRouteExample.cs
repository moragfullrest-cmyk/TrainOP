namespace TrainOP.Samples;

/// <summary>
/// Demonstrates extending a public exported route factory with local stations.
/// Same pattern as cross-assembly composition documented in <c>docs/cross-assembly-routes.md</c>.
/// </summary>
internal sealed class CrossAssemblyRouteExample : IExample
{
    public string Title => "10. Exported factory (RouteSchemas)";

    /// <summary>
    /// Extends <see cref="ExportedPaymentRoute"/> and runs the composed route.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = ExportedPaymentRoute.Build()
            .Station("Finalize", (string paymentId, decimal amount) =>
                new { paymentId, amount, status = "completed" });

        var report = route.DispatchTrain().Travel();
        var paymentId = report.Get<string>("paymentId");
        var amount = report.Get<decimal>("amount");
        var status = report.Get<string>("status");

        Console.WriteLine($"paymentId={paymentId}, amount={amount}, status={status}");
        ExampleOutput.WriteReport(report);
    }
}
