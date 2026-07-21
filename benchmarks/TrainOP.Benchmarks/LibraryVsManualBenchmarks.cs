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
        private Train _paymentTrain;
        private Train _longPaymentTrain;
        private Train _checkoutTrain;

        /// <summary>
        /// Pre-builds TrainOP trains so travel-only categories exclude route registration.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            _paymentTrain = LibraryChains.CreatePaymentTrain();
            _longPaymentTrain = LibraryChains.CreateLongPaymentTrain();
            _checkoutTrain = Library.CreateCheckoutTrain();
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Payment")]
        public decimal Manual_Payment() => ManualPipelineScenarios.TravelPayment();

        [Benchmark]
        [BenchmarkCategory("Payment")]
        public decimal TrainOP_BuildAndTravel_Payment() => LibraryChains.BuildAndTravelPayment();

        [Benchmark]
        [BenchmarkCategory("Payment")]
        public decimal TrainOP_TravelOnly_Payment() => LibraryChains.Travel(_paymentTrain);

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("LongPayment")]
        public decimal Manual_LongPayment() => ManualPipelineScenarios.TravelLongPayment();

        [Benchmark]
        [BenchmarkCategory("LongPayment")]
        public decimal TrainOP_BuildAndTravel_LongPayment() => LibraryChains.BuildAndTravelLongPayment();

        [Benchmark]
        [BenchmarkCategory("LongPayment")]
        public decimal TrainOP_TravelOnly_LongPayment() => LibraryChains.Travel(_longPaymentTrain);

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Checkout")]
        public decimal Manual_Checkout() => ManualPipelineScenarios.TravelCheckout();

        [Benchmark]
        [BenchmarkCategory("Checkout")]
        public decimal TrainOP_BuildAndTravel_Checkout() => Library.BuildAndTravelCheckout();

        [Benchmark]
        [BenchmarkCategory("Checkout")]
        public decimal TrainOP_TravelOnly_Checkout() => Library.Travel(_checkoutTrain);
    }
}
