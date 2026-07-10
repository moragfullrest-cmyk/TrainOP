using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Tests
{
    /// <summary>
    /// Runtime engine behavior: async travel, cancellation, exceptions.
    /// </summary>
    public sealed class TrainRuntimeTests
    {
        /// <summary>
        /// Verifies that a data-oriented route executes and propagates wagon values.
        /// </summary>
        [Fact]
        public void Train_Travel_ExecutesDataOrientedRoute()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { id = "ok" })
                .DispatchTrain()
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("ok", report.TerminalSignal.Manifest.PullWagon<string>("id"));
            Assert.Equal("ok", report["id"]);
            Assert.Equal("ok", report.Get<string>("id"));
        }

        /// <summary>
        /// Verifies that RouteReport indexer throws when wagon is missing.
        /// </summary>
        [Fact]
        public void RouteReport_Indexer_ThrowsForMissingWagon()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { id = "ok" })
                .DispatchTrain()
                .Travel();

            var exception = Assert.Throws<KeyNotFoundException>(() => _ = report["missing"]);
            Assert.Contains("missing", exception.Message);
        }

        /// <summary>
        /// Verifies that TravelAsync executes async stations and propagates updated wagon values.
        /// </summary>
        [Fact]
        public async Task Train_TravelAsync_HandlesAsyncStations()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { counter = 10 })
                .StationAsync("Multiply", async (int counter, CancellationToken token) =>
                {
                    await Task.Delay(10, token);
                    return new { counter = counter * 2 };
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
                .StationAsync("AsyncOnly", async (CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return RailwaySignals.Pass;
                });

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
                .Station("Seed", () => new { })
                .StationAsync("Wait", async (CancellationToken token) =>
                {
                    await Task.Delay(200, token);
                    return RailwaySignals.Pass;
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
                .Station("Seed", () => new { })
                .Station("CancelableSync", (CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return RailwaySignals.Pass;
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
                .Station("Seed", () => new { })
                .Station("Boom", (CancellationToken _) =>
                {
                    throw new InvalidOperationException("sync exploded");
                })
                .Station("MustNotRun", () => new { afterBoom = true });

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("Boom", red.Issue.StationName);
            Assert.Contains("sync exploded", red.Issue.Message);
            var exception = Assert.IsType<InvalidOperationException>(red.Issue.Exception);
            Assert.Equal("sync exploded", exception.Message);
            Assert.False(red.Manifest.HasWagon("afterBoom"));
        }

        /// <summary>
        /// Verifies that async station exceptions become red signals and halt further stations.
        /// </summary>
        [Fact]
        public async Task Train_TravelAsync_ConvertsStationExceptionToRedSignal()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { })
                .StationAsync("BoomAsync", async (CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    throw new InvalidOperationException("async exploded");
                })
                .Station("MustNotRun", () => new { afterBoom = true });

            var report = await route.DispatchTrain().TravelAsync();

            Assert.False(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("BoomAsync", red.Issue.StationName);
            Assert.Contains("async exploded", red.Issue.Message);
            var exception = Assert.IsType<InvalidOperationException>(red.Issue.Exception);
            Assert.Equal("async exploded", exception.Message);
            Assert.False(red.Manifest.HasWagon("afterBoom"));
        }

        /// <summary>
        /// Verifies that service stations recover from red signals and the route continues with TravelAsync.
        /// </summary>
        [Fact]
        public async Task Train_ServiceStation_HandlesAsyncRedSignals()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { marker = true })
                .Station("Boom", (bool marker) =>
                    RailwaySignals.Red("BOOM", "simulated failure"))
                .ServiceStation("SignalControlAsync", (ref bool marker, RedSignal red) =>
                {
                    marker = true;
                    return RailwaySignals.Pass;
                })
                .Station("AfterRecovery", (bool marker) => new { after = "ok", marker });

            var report = await route.DispatchTrain().TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.Equal("Seed", report.Visits[0].StationName);
            Assert.Equal("Boom", report.Visits[1].StationName);
            Assert.Equal("SignalControlAsync", report.Visits[2].StationName);
            Assert.Equal("AfterRecovery", report.Visits[3].StationName);
            Assert.True(report.TerminalSignal.Manifest.PullWagon<bool>("marker"));
            Assert.Equal("ok", report.TerminalSignal.Manifest.PullWagon<string>("after"));
        }
    }
}
