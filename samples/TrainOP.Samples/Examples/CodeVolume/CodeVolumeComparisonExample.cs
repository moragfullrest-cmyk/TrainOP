using System;
using System.Threading;
using System.Threading.Tasks;
using TrainOP.Samples.CodeVolume;

namespace TrainOP.Samples
{
    /// <summary>
    /// Side-by-side run of the same checkout rules: imperative code vs TrainOP.
    /// </summary>
    internal sealed class CodeVolumeComparisonExample : IExample
    {
        public string Title => "8. Объём кода: manual vs TrainOP";

        /// <summary>
        /// Executes happy-path and recoverable-failure scenarios for both implementations.
        /// </summary>
        public void Run()
        {
            ExampleOutput.WriteHeader(Title);

            Console.WriteLine(
                $"Logic lines (approx): manual={ManualCheckoutPipeline.ApproximateLogicLines}, " +
                $"TrainOP={TrainOpCheckoutPipeline.ApproximateLogicLines}");
            Console.WriteLine(
                "Saved ≈ " +
                (ManualCheckoutPipeline.ApproximateLogicLines - TrainOpCheckoutPipeline.ApproximateLogicLines) +
                " non-blank lines (StepResult / token checks / nested failure control-flow).");
            Console.WriteLine();

            var happy = new CheckoutRequest("ORD-42", 200m, 4, "VIP-7");
            var stockPressure = new CheckoutRequest("ORD-99", 120m, 15, "CUST-1");

            RunCase("happy path", happy).GetAwaiter().GetResult();
            Console.WriteLine();
            RunCase("stock overflow → recovery", stockPressure).GetAwaiter().GetResult();
        }

        private static async Task RunCase(string label, CheckoutRequest request)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var manual = await ManualCheckoutPipeline.RunAsync(request, cts.Token).ConfigureAwait(false);
            var trainOp = await TrainOpCheckoutPipeline.RunAsync(request, cts.Token).ConfigureAwait(false);

            Console.WriteLine($"[{label}]");
            WriteResult("manual ", manual);
            WriteResult("TrainOP", trainOp);
        }

        private static void WriteResult(string tag, CheckoutResult result)
        {
            if (result.Success)
            {
                Console.WriteLine(
                    $"  {tag}: OK orderId={result.OrderId}, amount={result.Amount:F2}, " +
                    $"units={result.Units}, status={result.Status}");
                return;
            }

            Console.WriteLine(
                $"  {tag}: FAIL [{result.FailureCode}] at {result.FailureStation}: {result.FailureMessage}");
        }
    }
}
