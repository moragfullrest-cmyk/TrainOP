using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Tests
{
    /// <summary>
    /// Runtime engine behavior: async travel, cancellation, exceptions, low-level AttachStation.
    /// </summary>
    public sealed class TrainRuntimeTests
    {
        /// <summary>
        /// Verifies that AttachStation with a manifest handler executes the route and propagates wagon values.
        /// </summary>
        [Fact]
        public void AttachStation_ManifestHandler_ExecutesRoute()
        {
            var report = new TrainRoute()
                .AttachStation("Seed", manifest => manifest.LoadWagon("id", "ok"))
                .DispatchTrain()
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("ok", report.TerminalSignal.Manifest.PullWagon<string>("id"));
        }

        /// <summary>
        /// Verifies that TravelAsync executes async stations and propagates updated wagon values.
        /// </summary>
        [Fact]
        public async Task Train_TravelAsync_HandlesAsyncStations()
        {
            var route = new TrainRoute()
                .AttachStation("Seed", manifest => manifest.LoadWagon("counter", 10))
                .AttachStation("Multiply", async (manifest, token) =>
                {
                    await Task.Delay(10, token);
                    var counter = manifest.PullWagon<int>("counter");
                    return manifest.LoadWagon("counter", counter * 2);
                });

            var report = await route.DispatchTrain().TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(20, report.TerminalSignal.Manifest.PullWagon<int>("counter"));
        }

        /// <summary>
        /// Verifies that Travel throws when the route contains an async-only station.
        /// </summary>
        [Fact]
        public void Train_Travel_ThrowsWhenRouteContainsAsyncStation()
        {
            var route = new TrainRoute()
                .AttachStation("AsyncOnly", (manifest, token) =>
                    Task.FromResult<Signal>(RailwaySignals.Green(manifest)));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                route.DispatchTrain().Travel());

            Assert.Contains("Use TravelAsync", exception.Message);
        }

        /// <summary>
        /// Verifies that TravelAsync honors cancellation tokens on async stations.
        /// </summary>
        [Fact]
        public async Task Train_TravelAsync_RespectsCancellation()
        {
            var route = new TrainRoute()
                .AttachStation("Wait", async (manifest, token) =>
                {
                    await Task.Delay(200, token);
                    return manifest;
                });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                route.DispatchTrain().TravelAsync(cts.Token));
        }

        /// <summary>
        /// Verifies that synchronous Travel honors cancellation tokens on cancelable sync stations.
        /// </summary>
        [Fact]
        public void Train_Travel_RespectsCancellation()
        {
            var route = new TrainRoute()
                .AttachStation("CancelableSync", (manifest, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return manifest;
                });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                route.DispatchTrain().Travel(cts.Token));
        }

        /// <summary>
        /// Verifies that synchronous station exceptions become red signals and halt further stations.
        /// </summary>
        [Fact]
        public void Train_Travel_ConvertsStationExceptionToRedSignal()
        {
            var route = new TrainRoute()
                .AttachStation("Boom", (Func<CargoManifest, Signal>)(manifest =>
                {
                    throw new InvalidOperationException("sync exploded");
                }))
                .AttachStation("MustNotRun", manifest => manifest.LoadWagon("after-boom", true));

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Single(report.Visits);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("Boom", red.Issue.StationName);
            Assert.Contains("sync exploded", red.Issue.Message);
            var exception = Assert.IsType<InvalidOperationException>(red.Issue.Exception);
            Assert.Equal("sync exploded", exception.Message);
            Assert.False(red.Manifest.HasWagon("after-boom"));
        }

        /// <summary>
        /// Verifies that async station exceptions become red signals and halt further stations.
        /// </summary>
        [Fact]
        public async Task Train_TravelAsync_ConvertsStationExceptionToRedSignal()
        {
            var route = new TrainRoute()
                .AttachStation("BoomAsync", (Func<CargoManifest, CancellationToken, Task<Signal>>)(async (manifest, token) =>
                {
                    await Task.Delay(1, token);
                    throw new InvalidOperationException("async exploded");
                }))
                .AttachStation("MustNotRun", manifest => manifest.LoadWagon("after-boom", true));

            var report = await route.DispatchTrain().TravelAsync();

            Assert.False(report.ReachedDestination);
            Assert.Single(report.Visits);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("BoomAsync", red.Issue.StationName);
            Assert.Contains("async exploded", red.Issue.Message);
            var exception = Assert.IsType<InvalidOperationException>(red.Issue.Exception);
            Assert.Equal("async exploded", exception.Message);
            Assert.False(red.Manifest.HasWagon("after-boom"));
        }

        /// <summary>
        /// Verifies that async service stations recover from async station exceptions and continue the route.
        /// </summary>
        [Fact]
        public async Task Train_ServiceStation_HandlesAsyncRedSignals()
        {
            var route = new TrainRoute()
                .AttachStation("BoomAsync", (Func<CargoManifest, CancellationToken, Task<Signal>>)(async (manifest, token) =>
                {
                    await Task.Delay(1, token);
                    throw new InvalidOperationException("async exploded");
                }))
                .ServiceStation("SignalControlAsync", async (red, token) =>
                {
                    await Task.Delay(1, token);
                    return RailwaySignals.Green(red.Manifest.LoadWagon("handled-async", true));
                })
                .AttachStation("AfterRecovery", manifest => manifest.LoadWagon("after", "ok"));

            var report = await route.DispatchTrain().TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
            Assert.Equal("BoomAsync", report.Visits[0].StationName);
            Assert.Equal("SignalControlAsync", report.Visits[1].StationName);
            Assert.Equal("AfterRecovery", report.Visits[2].StationName);
            Assert.True(report.TerminalSignal.Manifest.HasWagon("handled-async"));
            Assert.Equal("ok", report.TerminalSignal.Manifest.PullWagon<string>("after"));
        }
    }
}
