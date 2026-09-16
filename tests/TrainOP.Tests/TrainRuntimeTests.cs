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
        /// Verifies that TravelLight returns terminal wagons with an empty visit journal.
        /// </summary>
        [Fact]
        public void Train_TravelLight_EmptyVisits_KeepsTerminalWagons()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { id = "ok", amount = 10m })
                .Station("Bump", (string id, decimal amount) => new { id, amount = amount + 1m })
                .TravelLight();

            Assert.True(report.ReachedDestination);
            Assert.Empty(report.Visits);
            Assert.Equal("ok", report.Get<string>("id"));
            Assert.Equal(11m, report.Get<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that TravelLightAsync returns terminal wagons with an empty visit journal.
        /// </summary>
        [Fact]
        public async Task Train_TravelLightAsync_EmptyVisits_KeepsTerminalWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { counter = 10 })
                .Station("Multiply", async (int counter, CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return new { counter = counter * 2 };
                });

            var report = await route.TravelLightAsync();

            Assert.True(report.ReachedDestination);
            Assert.Empty(report.Visits);
            Assert.Equal(20, report.Get<int>("counter"));
        }

        /// <summary>
        /// Verifies that a data-oriented route executes and propagates wagon values.
        /// </summary>
        [Fact]
        public void Train_Travel_ExecutesDataOrientedRoute()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { id = "ok" })
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("ok", report.Manifest.PullWagon<string>("id"));
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
                .Station("Multiply", async (int counter, CancellationToken token) =>
                {
                    await Task.Delay(10, token);
                    return new { counter = counter * 2 };
                });

            var report = await route.TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(20, report.Manifest.PullWagon<int>("counter"));
        }

        /// <summary>
        /// Verifies that Travel throws when the route contains an async-only station.
        /// </summary>
        [Fact]
        public void Train_Travel_ThrowsWhenRouteContainsAsyncStation()
        {
            var route = new TrainRoute()
                .Station("AsyncOnly", async (CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return RailwaySignals.White;
                });

            var exception = Assert.Throws<InvalidOperationException>(() =>
                route.Travel());

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
                .Station("Wait", async (CancellationToken token) =>
                {
                    await Task.Delay(200, token);
                    return RailwaySignals.White;
                });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                route.TravelAsync(cts.Token));
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
                    return RailwaySignals.White;
                });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                route.Travel(cts.Token));
        }

        /// <summary>
        /// Verifies that synchronous station exceptions become red signals and halt further stations.
        /// </summary>
        [Fact]
        public void Train_Travel_ConvertsStationExceptionToRedSignal()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { })
                .Station("Boom", (Func<CancellationToken, Signal>)((CancellationToken _) =>
                    throw new InvalidOperationException("sync exploded")))
                .Station("MustNotRun", () => new { afterBoom = true });

            var report = route.Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("Boom", red.Issue.StationName);
            Assert.Contains("sync exploded", red.Issue.Message);
            var exception = Assert.IsType<InvalidOperationException>(red.Issue.Exception);
            Assert.Equal("sync exploded", exception.Message);
            Assert.False(report.Manifest.HasWagon("afterBoom"));
        }

        /// <summary>
        /// Verifies that async station exceptions become red signals and halt further stations.
        /// </summary>
        [Fact]
        public async Task Train_TravelAsync_ConvertsStationExceptionToRedSignal()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { })
                .Station("BoomAsync", async (CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    throw new InvalidOperationException("async exploded");
                })
                .Station("MustNotRun", () => new { afterBoom = true });

            var report = await route.TravelAsync();

            Assert.False(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("BoomAsync", red.Issue.StationName);
            Assert.Contains("async exploded", red.Issue.Message);
            var exception = Assert.IsType<InvalidOperationException>(red.Issue.Exception);
            Assert.Equal("async exploded", exception.Message);
            Assert.False(report.Manifest.HasWagon("afterBoom"));
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
                    return RailwaySignals.White;
                })
                .Station("AfterRecovery", (bool marker) => new { after = "ok", marker });

            var report = await route.TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.Equal("Seed", report.Visits[0].StationName);
            Assert.True(report.Visits[0].IsGreen);
            Assert.Equal("Boom", report.Visits[1].StationName);
            Assert.False(report.Visits[1].IsGreen);
            Assert.Equal("SignalControlAsync", report.Visits[2].StationName);
            Assert.True(report.Visits[2].IsGreen);
            Assert.Equal("AfterRecovery", report.Visits[3].StationName);
            Assert.True(report.Visits[3].IsGreen);
            Assert.True(report.Manifest.PullWagon<bool>("marker"));
            Assert.Equal("ok", report.Manifest.PullWagon<string>("after"));
        }

        /// <summary>
        /// Verifies that builtin RegisterStation returning White preserves the current manifest.
        /// </summary>
        [Fact]
        public void RegisterStation_Pass_PreservesCargo()
        {
            var route = new TrainRoute()
                .RegisterStation("Seed", manifest => manifest.LoadWagon("id", "keep-me"))
                .RegisterStation("NoOp", _ => RailwaySignals.White);

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("keep-me", report.Manifest.PullWagon<string>("id"));
        }

        /// <summary>
        /// Verifies that builtin RegisterStation returning RedFailure sets FailureCode without InvalidCastException.
        /// </summary>
        [Fact]
        public void RegisterStation_RedFailure_SetsFailureCode()
        {
            var route = new TrainRoute()
                .RegisterStation("Seed", manifest => manifest.LoadWagon("id", "cargo"))
                .RegisterStation("Boom", _ => RailwaySignals.Red("STOP", "halted"));

            var report = route.Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal("STOP", report.FailureCode);
            Assert.Equal("halted", report.FailureMessage);
            Assert.Equal("cargo", report.Manifest.PullWagon<string>("id"));
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("Boom", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies that builtin ServiceStation returning White preserves cargo after recovery.
        /// </summary>
        [Fact]
        public void ServiceStation_Pass_PreservesCargo()
        {
            var route = new TrainRoute()
                .RegisterStation("Seed", manifest => manifest.LoadWagon("id", "recovered"))
                .RegisterStation("Boom", _ => RailwaySignals.Red("BOOM", "simulated"))
                .ServiceStation("Recover", red => RailwaySignals.White);

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("recovered", report.Manifest.PullWagon<string>("id"));
            Assert.Equal(3, report.Visits.Count);
            Assert.Equal("Recover", report.Visits[2].StationName);
            Assert.True(report.Visits[2].IsGreen);
        }

        /// <summary>
        /// Verifies that builtin ServiceStation returning RedFailure sets FailureCode without InvalidCastException.
        /// </summary>
        [Fact]
        public void ServiceStation_RedFailure_SetsFailureCode()
        {
            var route = new TrainRoute()
                .RegisterStation("Seed", manifest => manifest.LoadWagon("id", "cargo"))
                .RegisterStation("Boom", _ => RailwaySignals.Red("BOOM", "simulated"))
                .ServiceStation("Recover", red => RailwaySignals.Red("NOPE", "declined"));

            var report = route.Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal("NOPE", report.FailureCode);
            Assert.Equal("declined", report.FailureMessage);
            Assert.Equal("cargo", report.Manifest.PullWagon<string>("id"));
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("Recover", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies that PullWagon and RouteReport.Get accept null wagon values for null-compatible types.
        /// </summary>
        [Fact]
        public void NullWagon_PullWagonAndGet_AllowNullForReferenceTypes()
        {
            var manifest = new CargoManifest().LoadWagon("note", null);
            Assert.Null(manifest.PullWagon<string>("note"));

            var report = new TrainRoute()
                .RegisterStation("Seed", m => m.LoadWagon("note", null))
                .Travel();

            Assert.Null(report.Get<string>("note"));
            Assert.Null(report["note"]);
        }

        /// <summary>
        /// Verifies that PullWagon throws InvalidCastException for null into a non-nullable value type without NRE.
        /// </summary>
        [Fact]
        public void NullWagon_PullWagon_ThrowsInvalidCastForValueType()
        {
            var manifest = new CargoManifest().LoadWagon("count", null);

            var exception = Assert.Throws<InvalidCastException>(() => manifest.PullWagon<int>("count"));
            Assert.Contains("null", exception.Message);
            Assert.Contains(typeof(int).FullName, exception.Message);
        }

        /// <summary>
        /// Verifies that Travel snapshots the plan at start; later builder mutations affect only the next Travel.
        /// </summary>
        [Fact]
        public void Travel_SnapshotsRouteAtStart_LaterMutationsAffectNextTravelOnly()
        {
            var route = new TrainRoute()
                .RegisterStation("Only", manifest => manifest.LoadWagon("id", "ok"));

            var first = route.Travel();
            route.RegisterStation("Extra", manifest => manifest.LoadWagon("extra", "seen"));
            var second = route.Travel();

            Assert.True(first.ReachedDestination);
            Assert.Equal(1, first.Visits.Count);
            Assert.Equal("Only", first.Visits[0].StationName);
            Assert.Equal("ok", first.Manifest.PullWagon<string>("id"));
            Assert.False(first.Manifest.HasWagon("extra"));

            Assert.True(second.ReachedDestination);
            Assert.Equal(2, second.Visits.Count);
            Assert.Equal("Extra", second.Visits[1].StationName);
            Assert.Equal("seen", second.Manifest.PullWagon<string>("extra"));
        }

        /// <summary>
        /// Verifies that an unknown non-green Signal subtype is rejected instead of a blind RedSignal cast.
        /// </summary>
        [Fact]
        public void RegisterStation_UnknownNonGreenSignal_ThrowsInvalidOperationException()
        {
            var route = new TrainRoute()
                .RegisterStation("Alien", _ => new AlienRedSignal());

            var exception = Assert.Throws<InvalidOperationException>(() => route.Travel());
            Assert.Contains(nameof(AlienRedSignal), exception.Message);
            Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifies that RedFailure with a whitespace code fails via SignalIssue validation (not InvalidCastException).
        /// </summary>
        [Fact]
        public void RegisterStation_WhitespaceRedFailureCode_ThrowsArgumentException()
        {
            var route = new TrainRoute()
                .RegisterStation("Seed", manifest => manifest.LoadWagon("id", "cargo"))
                .RegisterStation("Boom", _ => RailwaySignals.Red(" ", "halted"));

            var exception = Assert.Throws<ArgumentException>(() => route.Travel());
            Assert.Contains("code", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Verifies White normalization on the async travel path preserves cargo.
        /// </summary>
        [Fact]
        public async Task RegisterStation_Pass_TravelAsync_PreservesCargo()
        {
            var route = new TrainRoute()
                .RegisterStation("Seed", manifest => manifest.LoadWagon("id", "async-keep"))
                .RegisterStation("NoOp", _ => RailwaySignals.White);

            var report = await route.TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal("async-keep", report.Manifest.PullWagon<string>("id"));
        }

        /// <summary>
        /// Verifies RedFailure normalization on the async travel path sets FailureCode.
        /// </summary>
        [Fact]
        public async Task RegisterStation_RedFailure_TravelAsync_SetsFailureCode()
        {
            var route = new TrainRoute()
                .RegisterStation("Seed", manifest => manifest.LoadWagon("id", "cargo"))
                .RegisterStation("Boom", _ => RailwaySignals.Red("ASYNC_STOP", "halted"));

            var report = await route.TravelAsync();

            Assert.False(report.ReachedDestination);
            Assert.Equal("ASYNC_STOP", report.FailureCode);
            Assert.Equal("halted", report.FailureMessage);
            Assert.Equal("cargo", report.Manifest.PullWagon<string>("id"));
        }

        /// <summary>
        /// Verifies RouteReport.Get throws InvalidCastException for null into a value type without NRE.
        /// </summary>
        [Fact]
        public void NullWagon_RouteReportGet_ThrowsInvalidCastForValueType()
        {
            var report = new TrainRoute()
                .RegisterStation("Seed", m => m.LoadWagon("count", null))
                .Travel();

            var exception = Assert.Throws<InvalidCastException>(() => report.Get<int>("count"));
            Assert.Contains("null", exception.Message);
        }

        /// <summary>
        /// Verifies data-oriented Station returning White keeps prior wagons (adapter + runtime normalize path).
        /// </summary>
        [Fact]
        public void DataOriented_Station_Pass_PreservesCargo()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { id = "keep" })
                .Station("NoOp", (string id) => RailwaySignals.White)
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("keep", report.Get<string>("id"));
        }

        /// <summary>
        /// Verifies data-oriented Station returning Red sets FailureCode and keeps cargo.
        /// </summary>
        [Fact]
        public void DataOriented_Station_Red_SetsFailureCode()
        {
            var report = new TrainRoute()
                .Station("Seed", () => new { id = "cargo" })
                .Station("Boom", (string id) => RailwaySignals.Red("DATA_STOP", "halted"))
                .Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal("DATA_STOP", report.FailureCode);
            Assert.Equal("halted", report.FailureMessage);
            Assert.Equal("cargo", report.Get<string>("id"));
        }

        /// <summary>
        /// Unsupported non-green Signal used to verify normalize/reject behavior.
        /// </summary>
        private sealed class AlienRedSignal : Signal
        {
            public override bool IsGreen => false;
        }
    }
}
