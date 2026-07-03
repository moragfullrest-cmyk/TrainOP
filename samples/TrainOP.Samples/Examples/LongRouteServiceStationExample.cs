using TrainOP;

namespace TrainOP.Samples;

/// <summary>
/// Длинный маршрут: при красном сигнале срабатывает ServiceStation в конце цепочки,
/// пишет детали ошибки в консоль и восстанавливает поезд.
/// </summary>
internal sealed class LongRouteServiceStationExample : IExample
{
    public string Title => "7. Длинный маршрут + ServiceStation";

    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        var route = new TrainRoute()
            .Station("Seed", () => new { orderId = "ORD-7710", amount = 200m, units = 12 })
            .Station("NormalizeOrder", (string orderId, decimal amount, int units) =>
                new { orderId = orderId.Trim(), amount, units })
            .Station("ApplyCurrency", (string orderId, decimal amount, int units) =>
                new { orderId, amount, units, currency = "USD" })
            .Station("ApplyLoyaltyBonus", (string orderId, decimal amount, int units, string currency) =>
                new { orderId, amount = amount * 1.05m, units, currency })
            .Station("CalculateShipping", (string orderId, decimal amount, int units, string currency) =>
                new { orderId, amount = amount + units * 2m, units, currency })
            .Station("CheckStock", (string orderId, decimal amount, int units, string currency) =>
                units <= 10
                    ? RailwaySignals.Green(new { orderId, amount, units, currency })
                    : RailwaySignals.Red("STOCK_LIMIT", $"requested {units} units, max 10"))
            .Station("AntiFraud", (string orderId, decimal amount, int units, string currency) =>
                amount < 500m
                    ? RailwaySignals.Green(new { orderId, amount, units, currency })
                    : RailwaySignals.Red("FRAUD_REVIEW", "amount exceeds auto-approval limit"))
            .ServiceStation("TerminalLogger", red =>
            {
                var issue = red.Issue;
                Console.WriteLine($"[TerminalLogger] failure at station '{issue.StationName}'");
                Console.WriteLine($"[TerminalLogger] code={issue.Code}, message={issue.Message}");

                var orderId = red.Manifest.PullWagon<string>("orderId");
                var amount = red.Manifest.PullWagon<decimal>("amount");
                var units = red.Manifest.PullWagon<int>("units");
                var currency = red.Manifest.PullWagon<string>("currency");
                Console.WriteLine(
                    $"[TerminalLogger] snapshot: orderId={orderId}, units={units}, amount={amount:F2} {currency}");

                if (issue.Code == "STOCK_LIMIT")
                {
                    return RailwaySignals.Green(red.Manifest.LoadWagon("units", 10));
                }

                return RailwaySignals.Red(
                    red.Manifest,
                    new SignalIssue("UNRECOVERABLE", $"cannot recover from {issue.Code}", "TerminalLogger"));
            })
            .Station("CapturePayment", (string orderId, decimal amount, int units, string currency) =>
                new { orderId, amount, units, currency, status = "captured" })
            .Station("Dispatch", (string orderId, decimal amount, int units, string currency, string status) =>
                new { orderId, status, dispatched = true });

        var report = route.DispatchTrain().Travel();
        ExampleOutput.WriteReport(report);
    }
}
