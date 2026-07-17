using BenchmarkDotNet.Running;
using TrainOP.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ChainDispatchBenchmarks).Assembly).Run(args);
