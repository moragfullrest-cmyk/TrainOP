using BenchmarkDotNet.Running;
using TrainOP.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(LibraryVsManualBenchmarks).Assembly).Run(args);
