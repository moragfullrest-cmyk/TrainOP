using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Library = TrainOP.Benchmarks.Caller.LibraryWorkloadScenarios;
using LibraryChains = TrainOP.Benchmarks.Caller.ChainDispatchScenarios;

namespace TrainOP.Benchmarks
{
    /// <summary>
    /// Compares TrainOP (caller mode) against hand-written pipelines with the same transforms.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class LibraryVsManualBenchmarks
    {
        private TrainRoute _paymentRoute;
        private TrainRoute _longPaymentRoute;
        private TrainRoute _checkoutRoute;

        /// <summary>
        /// Pre-builds TrainOP routes so travel-only categories exclude route registration.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            _paymentRoute = LibraryChains.CreatePaymentRoute();
            _longPaymentRoute = LibraryChains.CreateLongPaymentRoute();
            _checkoutRoute = Library.CreateCheckoutRoute();
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Payment")]
        public decimal Manual_Payment() => ManualPipelineScenarios.TravelPayment();

        [Benchmark]
        [BenchmarkCategory("Payment")]
        public decimal TrainOP_BuildAndTravel_Payment() => LibraryChains.BuildAndTravelPayment();

        [Benchmark]
        [BenchmarkCategory("Payment")]
        public decimal TrainOP_TravelOnly_Payment() => LibraryChains.Travel(_paymentRoute);

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("LongPayment")]
        public decimal Manual_LongPayment() => ManualPipelineScenarios.TravelLongPayment();

        [Benchmark]
        [BenchmarkCategory("LongPayment")]
        public decimal TrainOP_BuildAndTravel_LongPayment() => LibraryChains.BuildAndTravelLongPayment();

        [Benchmark]
        [BenchmarkCategory("LongPayment")]
        public decimal TrainOP_TravelOnly_LongPayment() => LibraryChains.Travel(_longPaymentRoute);

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Checkout")]
        public decimal Manual_Checkout() => ManualPipelineScenarios.TravelCheckout();

        [Benchmark]
        [BenchmarkCategory("Checkout")]
        public decimal TrainOP_BuildAndTravel_Checkout() => Library.BuildAndTravelCheckout();

        [Benchmark]
        [BenchmarkCategory("Checkout")]
        public decimal TrainOP_TravelOnly_Checkout() => Library.Travel(_checkoutRoute);
    }
}
