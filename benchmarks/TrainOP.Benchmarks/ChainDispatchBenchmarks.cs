using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using TrainOP;
using CallerScenarios = TrainOP.Benchmarks.Caller.ChainDispatchScenarios;
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
        private Train _callerPaymentTrain;
        private Train _reflectionLongPaymentTrain;
        private Train _callerLongPaymentTrain;

        /// <summary>
        /// Pre-builds trains so travel-only benchmarks exclude Station registration.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            _reflectionPaymentTrain = ReflectionScenarios.CreatePaymentTrain();
            _callerPaymentTrain = CallerScenarios.CreatePaymentTrain();
            _reflectionLongPaymentTrain = ReflectionScenarios.CreateLongPaymentTrain();
            _callerLongPaymentTrain = CallerScenarios.CreateLongPaymentTrain();
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("BuildAndTravel_Payment")]
        public decimal Reflection_BuildAndTravel_Payment() =>
            ReflectionScenarios.BuildAndTravelPayment();

        [Benchmark]
        [BenchmarkCategory("BuildAndTravel_Payment")]
        public decimal Caller_BuildAndTravel_Payment() =>
            CallerScenarios.BuildAndTravelPayment();

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("BuildAndTravel_BothChains")]
        public decimal Reflection_BuildAndTravel_BothChains() =>
            ReflectionScenarios.BuildAndTravelBothChains();

        [Benchmark]
        [BenchmarkCategory("BuildAndTravel_BothChains")]
        public decimal Caller_BuildAndTravel_BothChains() =>
            CallerScenarios.BuildAndTravelBothChains();

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("BuildAndTravel_LongPayment")]
        public decimal Reflection_BuildAndTravel_LongPayment() =>
            ReflectionScenarios.BuildAndTravelLongPayment();

        [Benchmark]
        [BenchmarkCategory("BuildAndTravel_LongPayment")]
        public decimal Caller_BuildAndTravel_LongPayment() =>
            CallerScenarios.BuildAndTravelLongPayment();

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("TravelOnly_Payment")]
        public decimal Reflection_TravelOnly_Payment() =>
            ReflectionScenarios.Travel(_reflectionPaymentTrain);

        [Benchmark]
        [BenchmarkCategory("TravelOnly_Payment")]
        public decimal Caller_TravelOnly_Payment() =>
            CallerScenarios.Travel(_callerPaymentTrain);

        [Benchmark(Baseline = true)]
        [BenchmarkCategory("TravelOnly_LongPayment")]
        public decimal Reflection_TravelOnly_LongPayment() =>
            ReflectionScenarios.Travel(_reflectionLongPaymentTrain);

        [Benchmark]
        [BenchmarkCategory("TravelOnly_LongPayment")]
        public decimal Caller_TravelOnly_LongPayment() =>
            CallerScenarios.Travel(_callerLongPaymentTrain);
    }
}
