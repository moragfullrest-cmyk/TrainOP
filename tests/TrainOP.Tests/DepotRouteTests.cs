using System;
using System.Threading;
using System.Threading.Tasks;
using TrainOP;
using Xunit;

namespace TrainOP.Tests
{
    public sealed class TrainRouteTests
    {
        [Fact]
        public void Train_ReachesDestination_WhenAllStationsAreGreen()
        {
            var route = new TrainRoute()
                .AttachStation("Validation", manifest =>
                {
                    if (!manifest.HasCar("request-id"))
                    {
                        return RailwaySignals.Red(
                            manifest,
                            new SignalIssue("REQ_MISSING", "request-id is required", "Validation"));
                    }

                    return RailwaySignals.Green(manifest);
                })
                .AttachStation("Enrichment", manifest =>
                {
                    return manifest.LoadCar("processed-at", DateTime.UtcNow);
                });

            var start = new CargoManifest().LoadCar("request-id", "abc-123");
            var report = route.DispatchTrain().Travel(start);

            Assert.True(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            Assert.Equal("Validation", report.Visits[0].StationName);
            Assert.Equal("Enrichment", report.Visits[1].StationName);
            Assert.True(report.TerminalSignal.Manifest.HasCar("processed-at"));
        }

        [Fact]
        public void Train_StopsAtFirstRedSignal_AndSkipsFollowingStations()
        {
            var route = new TrainRoute()
                .AttachStation("Validation", manifest =>
                {
                    return RailwaySignals.Red(
                        manifest,
                        new SignalIssue("REQ_MISSING", "request-id is required", "Validation"));
                })
                .AttachStation("MustNotRun", manifest =>
                {
                    return manifest.LoadCar("forbidden", true);
                });

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Single(report.Visits);
            Assert.Equal("Validation", report.Visits[0].StationName);

            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("REQ_MISSING", red.Issue.Code);
            Assert.False(red.Manifest.HasCar("forbidden"));
        }

        [Fact]
        public void CargoManifest_CanAddReplaceAndRemoveCarsAcrossStations()
        {
            var route = new TrainRoute()
                .AttachStation("Seed", manifest =>
                {
                    return manifest
                        .LoadCar("counter", 1)
                        .LoadCar("temporary", "keep");
                })
                .AttachStation("Mutate", manifest =>
                {
                    var counter = manifest.PullCar<int>("counter");
                    return manifest
                        .LoadCar("counter", counter + 41)
                        .UnloadCar("temporary");
                });

            var report = route.DispatchTrain().Travel();
            var finalManifest = report.TerminalSignal.Manifest;

            Assert.True(report.ReachedDestination);
            Assert.Equal(42, finalManifest.PullCar<int>("counter"));
            Assert.False(finalManifest.HasCar("temporary"));
        }

        [Fact]
        public async Task Train_TravelAsync_HandlesAsyncStations()
        {
            var route = new TrainRoute()
                .AttachStation("Seed", manifest =>
                {
                    return manifest.LoadCar("counter", 10);
                })
                .AttachStation("Multiply", async (manifest, token) =>
                {
                    await Task.Delay(10, token);
                    var counter = manifest.PullCar<int>("counter");
                    return manifest.LoadCar("counter", counter * 2);
                });

            var report = await route.DispatchTrain().TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(20, report.TerminalSignal.Manifest.PullCar<int>("counter"));
        }

        [Fact]
        public void Train_Travel_ThrowsWhenRouteContainsAsyncStation()
        {
            var route = new TrainRoute()
                .AttachStation("AsyncOnly", (manifest, token) =>
                {
                    return Task.FromResult<Signal>(RailwaySignals.Green(manifest));
                });

            var exception = Assert.Throws<InvalidOperationException>(() =>
                route.DispatchTrain().Travel());

            Assert.Contains("Use TravelAsync", exception.Message);
        }

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

        [Fact]
        public void Train_Travel_ConvertsStationExceptionToRedSignal()
        {
            var route = new TrainRoute()
                .AttachStation("Boom", (Func<CargoManifest, Signal>)(manifest =>
                {
                    throw new InvalidOperationException("sync exploded");
                }))
                .AttachStation("MustNotRun", manifest => manifest.LoadCar("after-boom", true));

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Single(report.Visits);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("Boom", red.Issue.StationName);
            Assert.Contains("sync exploded", red.Issue.Message);
            Assert.False(red.Manifest.HasCar("after-boom"));
        }

        [Fact]
        public async Task Train_TravelAsync_ConvertsStationExceptionToRedSignal()
        {
            var route = new TrainRoute()
                .AttachStation("BoomAsync", (Func<CargoManifest, CancellationToken, Task<Signal>>)(async (manifest, token) =>
                {
                    await Task.Delay(1, token);
                    throw new InvalidOperationException("async exploded");
                }))
                .AttachStation("MustNotRun", manifest => manifest.LoadCar("after-boom", true));

            var report = await route.DispatchTrain().TravelAsync();

            Assert.False(report.ReachedDestination);
            Assert.Single(report.Visits);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("STATION_EXCEPTION", red.Issue.Code);
            Assert.Equal("BoomAsync", red.Issue.StationName);
            Assert.Contains("async exploded", red.Issue.Message);
            Assert.False(red.Manifest.HasCar("after-boom"));
        }

        [Fact]
        public void Train_RedSignalStation_CanRecoverAndContinueRoute()
        {
            var route = new TrainRoute()
                .AttachStation("Validation", manifest =>
                    RailwaySignals.Red(manifest, new SignalIssue("REQ_MISSING", "request-id is required", "Validation")))
                .AttachRedSignalStation("SignalControl", red =>
                {
                    var recovered = red.Manifest.LoadCar("recovered", true);
                    return RailwaySignals.Green(recovered);
                })
                .AttachStation("AfterRecovery", manifest => manifest.LoadCar("after", "ok"));

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
            Assert.Equal("Validation", report.Visits[0].StationName);
            Assert.Equal("SignalControl", report.Visits[1].StationName);
            Assert.Equal("AfterRecovery", report.Visits[2].StationName);
            Assert.True(report.TerminalSignal.Manifest.HasCar("recovered"));
            Assert.Equal("ok", report.TerminalSignal.Manifest.PullCar<string>("after"));
        }

        [Fact]
        public async Task Train_RedSignalStation_HandlesAsyncRedSignals()
        {
            var route = new TrainRoute()
                .AttachStation("BoomAsync", (Func<CargoManifest, CancellationToken, Task<Signal>>)(async (manifest, token) =>
                {
                    await Task.Delay(1, token);
                    throw new InvalidOperationException("async exploded");
                }))
                .AttachRedSignalStation("SignalControlAsync", async (red, token) =>
                {
                    await Task.Delay(1, token);
                    return RailwaySignals.Green(red.Manifest.LoadCar("handled-async", true));
                })
                .AttachStation("AfterRecovery", manifest => manifest.LoadCar("after", "ok"));

            var report = await route.DispatchTrain().TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
            Assert.Equal("BoomAsync", report.Visits[0].StationName);
            Assert.Equal("SignalControlAsync", report.Visits[1].StationName);
            Assert.Equal("AfterRecovery", report.Visits[2].StationName);
            Assert.True(report.TerminalSignal.Manifest.HasCar("handled-async"));
            Assert.Equal("ok", report.TerminalSignal.Manifest.PullCar<string>("after"));
        }

    }
}
