namespace TrainOP.Samples;

/// <summary>
/// Demonstrates nested sub-routes and a branching station that dispatches one of them.
/// </summary>
internal sealed class NestedBranchingRouteExample : IExample
{
    public string Title => "8. Вложенные маршруты и ветвление";

    /// <summary>
    /// Runs the parent route for premium and standard tiers and prints both reports.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        RunTier("premium");
        Console.WriteLine();
        RunTier("standard");
    }

    private static void RunTier(string tier)
    {
        Console.WriteLine($"Tier: {tier}");

        var report = BuildRoute(tier).DispatchTrain().Travel();
        ExampleOutput.WriteReport(report);
    }

    private static TrainRoute BuildRoute(string tier)
    {
        return new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-branch", amount = 100m, channel = tier })
            .Station("Branch", (string paymentId, decimal amount, string channel) =>
                DispatchBranch(paymentId, amount, channel))
            .Station("Finalize", (string paymentId, decimal amount, string channel) =>
                new { paymentId, amount, channel, status = "completed" });
    }

    private static object DispatchBranch(string paymentId, decimal amount, string channel)
    {
        var subRoute = channel == "premium"
            ? PremiumBranchRoute.Build(paymentId, amount)
            : StandardBranchRoute.Build(paymentId, amount);

        var subReport = subRoute.DispatchTrain().Travel();
        if (!subReport.ReachedDestination)
        {
            return RailwaySignals.Red("BRANCH_FAILED", $"branch '{channel}' did not complete");
        }

        return new
        {
            paymentId = subReport.Get<string>("paymentId"),
            amount = subReport.Get<decimal>("amount"),
            channel = subReport.Get<string>("channel"),
        };
    }

    private static class PremiumBranchRoute
    {
        public static TrainRoute Build(string paymentId, decimal amount)
        {
            return new TrainRoute()
                .Station("Seed", () => new { paymentId, amount })
                .Station("ApplyPremiumDiscount", (string paymentId, decimal amount) =>
                    new { paymentId = paymentId + "-premium", amount = amount * 0.8m, channel = "premium" });
        }
    }

    private static class StandardBranchRoute
    {
        public static TrainRoute Build(string paymentId, decimal amount)
        {
            return new TrainRoute()
                .Station("Seed", () => new { paymentId, amount })
                .Station("ApplyStandardFee", (string paymentId, decimal amount) =>
                    new { paymentId = paymentId + "-standard", amount = amount + 2m, channel = "standard" });
        }
    }
}
