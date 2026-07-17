using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using TrainOP;
using InterceptorScenarios = TrainOP.Benchmarks.Interceptors.ChainDispatchScenarios;
using ReflectionScenarios = TrainOP.Benchmarks.Reflection.ChainDispatchScenarios;

namespace TrainOP.Benchmarks
{
    /// <summary>
    /// Compares chain-dispatch registration + travel cost for reflection vs Roslyn interceptors.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class ChainDispatchBenchmarks
    {
        private Train _reflectionPaymentTrain;
        private Train _interceptorPaymentTrain;
        private Train _reflectionLongPaymentTrain;
        private Train _interceptorLongPaymentTrain;

        /// <summary>
        /// Pre-builds trains so travel-only benchmarks exclude Station registration.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            _reflectionPaymentTrain = ReflectionScenarios.CreatePaymentTrain();
            _interceptorPaymentTrain = InterceptorScenarios.CreatePaymentTrain();
            _reflectionLongPaymentTrain = ReflectionScenarios.CreateLongPaymentTrain();
            _interceptorLongPaymentTrain = InterceptorScenarios.CreateLongPaymentTrain();
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("BuildAndTravel_Payment")]
        public decimal Reflection_BuildAndTravel_Payment() =>
            ReflectionScenarios.BuildAndTravelPayment();

        [Benchmark]
        [BenchmarkCategory("BuildAndTravel_Payment")]
        public decimal Interceptors_BuildAndTravel_Payment() =>
            InterceptorScenarios.BuildAndTravelPayment();

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("BuildAndTravel_BothChains")]
        public decimal Reflection_BuildAndTravel_BothChains() =>
            ReflectionScenarios.BuildAndTravelBothChains();

        [Benchmark]
        [BenchmarkCategory("BuildAndTravel_BothChains")]
        public decimal Interceptors_BuildAndTravel_BothChains() =>
            InterceptorScenarios.BuildAndTravelBothChains();

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("BuildAndTravel_LongPayment")]
        public decimal Reflection_BuildAndTravel_LongPayment() =>
            ReflectionScenarios.BuildAndTravelLongPayment();

        [Benchmark]
        [BenchmarkCategory("BuildAndTravel_LongPayment")]
        public decimal Interceptors_BuildAndTravel_LongPayment() =>
            InterceptorScenarios.BuildAndTravelLongPayment();

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("TravelOnly_Payment")]
        public decimal Reflection_TravelOnly_Payment() =>
            ReflectionScenarios.Travel(_reflectionPaymentTrain);

        [Benchmark]
        [BenchmarkCategory("TravelOnly_Payment")]
        public decimal Interceptors_TravelOnly_Payment() =>
            InterceptorScenarios.Travel(_interceptorPaymentTrain);

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("TravelOnly_LongPayment")]
        public decimal Reflection_TravelOnly_LongPayment() =>
            ReflectionScenarios.Travel(_reflectionLongPaymentTrain);

        [Benchmark]
        [BenchmarkCategory("TravelOnly_LongPayment")]
        public decimal Interceptors_TravelOnly_LongPayment() =>
            InterceptorScenarios.Travel(_interceptorLongPaymentTrain);
    }
}
