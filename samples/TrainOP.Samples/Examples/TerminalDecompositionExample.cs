using System.Runtime.CompilerServices;

namespace TrainOP.Samples;

/// <summary>
/// Two user-written ways to decompose a chain's terminal wagons.
/// <see cref="RouteReport"/> stays shared and has no <c>Deconstruct</c>.
/// </summary>
internal sealed class TerminalDecompositionExample : IExample
{
    public string Title => "11. Декомпозиция терминала";

    /// <summary>
    /// Runs inheritance-based <c>Travel</c> and a chain-specific local function.
    /// </summary>
    public void Run()
    {
        ExampleOutput.WriteHeader(Title);

        RunInherited();
        Console.WriteLine();
        RunLocalFunction();
    }

    /// <summary>
    /// Keeps the variable typed as <see cref="PaymentRoute"/> so the hidden <c>Travel</c> returns that chain's tuple.
    /// </summary>
    private static void RunInherited()
    {
        Console.WriteLine("Потомок TrainRoute:");

        var route = new PaymentRoute();
        route.Station("Seed", () => new { paymentId = "pay-inherit", amount = 100m });
        route.Station("Discount", (string paymentId, decimal amount) =>
            new { paymentId, amount = amount * 0.9m });

        var (paymentId, amount) = route.Travel();
        Console.WriteLine($"paymentId={paymentId}, amount={amount}");
    }

    /// <summary>
    /// Builds an ordinary <see cref="TrainRoute"/> and reads it through a local function bound to that chain.
    /// </summary>
    private static void RunLocalFunction()
    {
        Console.WriteLine("Локальная функция на одну цепочку:");

        var route = new TrainRoute()
            .Station("Seed", () => new { orderId = "ord-1", total = 40m })
            .Station("Tax", (string orderId, decimal total) =>
                new { orderId, total = total * 1.2m });

        var (orderId, total) = ReadOrder(route);
        Console.WriteLine($"orderId={orderId}, total={total}");

        (string orderId, decimal total) ReadOrder(TrainRoute chain)
        {
            var report = chain.Travel();
            return (report.Get<string>("orderId"), report.Get<decimal>("total"));
        }
    }

    /// <summary>
    /// One payment chain. Caller attributes are forwarded so chain-dispatch matches <c>new PaymentRoute()</c>.
    /// </summary>
    private sealed class PaymentRoute : TrainRoute
    {
        public PaymentRoute(
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = null)
            : base(filePath, lineNumber, memberName)
        {
        }

        public new (string PaymentId, decimal Amount) Travel()
        {
            var report = base.Travel();
            return (report.Get<string>("paymentId"), report.Get<decimal>("amount"));
        }
    }
}
