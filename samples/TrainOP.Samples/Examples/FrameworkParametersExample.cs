using System.Collections.Generic;

namespace TrainOP.Samples;

/// <summary>
/// Demonstrates every framework (non-wagon) parameter accepted by Station and ServiceStation handlers.
/// </summary>
internal sealed class FrameworkParametersExample : IExample
{
    public string Title => "9. Framework-параметры станций";

    /// <summary>
    /// Runs short routes that each highlight one or more framework parameters.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        RunStationCargoManifest();
        Console.WriteLine();
        RunStationCancellationToken();
        Console.WriteLine();
        RunServiceStationSignalIssue();
        Console.WriteLine();
        RunServiceStationIssueChain();
        Console.WriteLine();
        RunServiceStationRedSignal();
        Console.WriteLine();
        RunServiceStationCancellationToken();
    }

    /// <summary>
    /// Station: <c>CargoManifest</c> — read wagons that are not formal handler inputs.
    /// </summary>
    private static void RunStationCargoManifest()
    {
        Console.WriteLine("Station → CargoManifest");

        var report = new TrainRoute()
            .Station("Seed", () => new { paymentId = "pay-fw", amount = 10m, note = "via-manifest" })
            .Station("Annotate", (CargoManifest manifest, string paymentId) =>
                new { paymentId = paymentId + "/" + manifest.PullWagon<string>("note") })
            .Travel();

        ExampleOutput.WriteReport(report);
    }

    /// <summary>
    /// Station: <c>CancellationToken</c> — cooperative cancel for sync/async handlers.
    /// </summary>
    private static void RunStationCancellationToken()
    {
        Console.WriteLine("Station → CancellationToken");

        var report = new TrainRoute()
            .Station("Seed", () => new { counter = 3 })
            .Station("Tick", async (int counter, CancellationToken token) =>
            {
                await Task.Delay(20, token);
                return new { counter = counter + 1 };
            })
            .TravelAsync()
            .GetAwaiter()
            .GetResult();

        ExampleOutput.WriteReport(report);
    }

    /// <summary>
    /// ServiceStation: <c>SignalIssue</c> — last / immediate stop (no <c>RedSignal</c> required).
    /// </summary>
    private static void RunServiceStationSignalIssue()
    {
        Console.WriteLine("ServiceStation → SignalIssue (last)");

        var report = new TrainRoute()
            .Station("Seed", () => new { amount = -1m })
            .Station("Validate", (decimal amount) =>
                amount > 0
                    ? RailwaySignals.Green(new { amount })
                    : RailwaySignals.Red("INVALID", "amount must be positive"))
            .ServiceStation("Recovery", (decimal amount, SignalIssue issue) =>
            {
                Console.WriteLine($"  recovered from [{issue.Code}] at {issue.StationName}");
                return RailwaySignals.Green(new { amount = 1m });
            })
            .Station("Double", (decimal amount) => new { amount = amount * 2m })
            .Travel();

        ExampleOutput.WriteReport(report);
    }

    /// <summary>
    /// ServiceStation: <c>IReadOnlyList&lt;SignalIssue&gt;</c> — full nested issue chain.
    /// </summary>
    private static void RunServiceStationIssueChain()
    {
        Console.WriteLine("ServiceStation → IReadOnlyList<SignalIssue> (chain)");

        var report = new TrainRoute()
            .Station("Seed", () => new { channel = "nested" })
            .Station("Branch", (string channel) => DispatchFailingBranch(channel))
            .ServiceStation("Recovery", (string channel, SignalIssue issue, IReadOnlyList<SignalIssue> issues) =>
            {
                Console.WriteLine($"  issue (last) = [{issue.Code}] @{issue.StationName}");
                for (var i = 0; i < issues.Count; i++)
                {
                    Console.WriteLine($"  issues[{i}] = [{issues[i].Code}] @{issues[i].StationName}");
                }

                return RailwaySignals.Red("CANNOT_RECOVER", "chain preserved for report");
            })
            .Travel();

        ExampleOutput.WriteReport(report);
    }

    /// <summary>
    /// ServiceStation: <c>RedSignal</c> — escape hatch for manifest + issue surface.
    /// </summary>
    private static void RunServiceStationRedSignal()
    {
        Console.WriteLine("ServiceStation → RedSignal (escape hatch)");

        var report = new TrainRoute()
            .Station("Seed", () => new { units = 99 })
            .Station("CheckStock", (int units) =>
                units <= 10
                    ? RailwaySignals.Green(new { units })
                    : RailwaySignals.Red("STOCK_LIMIT", "too many"))
            .ServiceStation("Recover", (int units, RedSignal red, CargoManifest manifest) =>
            {
                Console.WriteLine($"  red.Issue = [{red.Issue.Code}], wagons = {manifest.InspectWagons().Count}");
                return RailwaySignals.Green(new { units = 10 });
            })
            .Station("Ship", (int units) => new { units, status = "ok" })
            .Travel();

        ExampleOutput.WriteReport(report);
    }

    /// <summary>
    /// ServiceStation: <c>CancellationToken</c> on an async data-oriented recovery handler.
    /// </summary>
    private static void RunServiceStationCancellationToken()
    {
        Console.WriteLine("ServiceStation → CancellationToken (async)");

        var report = new TrainRoute()
            .Station("Seed", () => new { amount = -5m })
            .Station("Validate", (decimal amount) =>
                RailwaySignals.Red("INVALID", "negative"))
            .ServiceStation("Recovery", async (decimal amount, RedSignal red, CancellationToken token) =>
            {
                await Task.Delay(20, token);
                Console.WriteLine($"  async recover from [{red.Issue.Code}]");
                return new { amount = 5m };
            })
            .Station("Done", (decimal amount) => new { amount })
            .TravelAsync()
            .GetAwaiter()
            .GetResult();

        ExampleOutput.WriteReport(report);
    }

    private static object DispatchFailingBranch(string channel)
    {
        var subReport = new TrainRoute()
            .Station("Seed", () => new { channel })
            .Station("SubValidate", (string channel) =>
                RailwaySignals.Red("SUB_INVALID", "sub-route stop"))
            .Travel();

        if (!subReport.ReachedDestination)
        {
            return RailwaySignals.Red(
                "BRANCH_FAILED",
                "branch did not complete",
                subReport.FailureIssues);
        }

        return new { channel };
    }
}
