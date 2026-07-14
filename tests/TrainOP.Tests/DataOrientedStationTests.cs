using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    /// <summary>
    /// Tests data-oriented Station and ServiceStation API behavior at runtime.
    /// </summary>
    public sealed class DataOrientedStationTests
    {
        /// <summary>
        /// Verifies that a seed station produces the initial wagon values in the manifest.
        /// </summary>
        [Fact]
        public void Station_Seed_ProducesInitialWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-seed", amount = 10m });

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-seed", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that named handler parameters merge return values into the manifest.
        /// </summary>
        [Fact]
        public void Station_NamedParameters_MergeReturnIntoManifest()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("Discount", (string paymentId, decimal amount) =>
                    new { paymentId, amount = amount * 0.9m });

            var report = route.DispatchTrain().Travel();

            Assert.Equal("pay-1", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(90m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a partial return removes omitted wagons from the manifest.
        /// </summary>
        [Fact]
        public void Station_PartialReturn_RemovesOmittedWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
                .Station("Partial", (string paymentId, decimal amount) =>
                    new { paymentId = paymentId + "-merged" });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-partial-merged", manifest.PullWagon<string>("paymentId"));
            Assert.False(manifest.HasWagon("amount"));
            Assert.Equal("keep", manifest.PullWagon<string>("traceId"));
        }

        /// <summary>
        /// Verifies that a tuple return maps values to manifest wagons by parameter order.
        /// </summary>
        [Fact]
        public void Station_ReturnsTuple_MapsByParameterOrder()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-tuple", amount = 4m })
                .Station("ByTuple", (string paymentId, decimal amount) =>
                    (paymentId: paymentId + "-tuple", amount: amount + 2m));

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-tuple-tuple", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(6m, manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that an unnamed tuple return maps values to manifest wagons by parameter order.
        /// </summary>
        [Fact]
        public void Station_ReturnsUnnamedTuple_MapsByParameterOrder()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-tuple", amount = 4m })
                .Station("ByTuple", (string paymentId, decimal amount) =>
                    (paymentId + "-tuple", amount + 2m));

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-tuple-tuple", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(6m, manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a data validation failure stops the route with a red signal.
        /// </summary>
        [Fact]
        public void Station_DataFail_StopsRouteWithRedSignal()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-fail", amount = -1m })
                .Station("Validate", (string paymentId, decimal amount) =>
                    amount > 0
                        ? RailwaySignals.Green(new { paymentId, amount })
                        : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive"))
                .Station("MustNotRun", (string paymentId, decimal amount) =>
                    new { paymentId = "nope", amount });

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("INVALID_TOTAL", red.Issue.Code);
            Assert.Equal("Validate", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies that an async data validation failure stops the route with a red signal.
        /// </summary>
        [Fact]
        public async Task Station_DataFail_Async_StopsRouteWithRedSignal()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-async-fail", amount = -5m })
                .Station("Validate", async (string paymentId, decimal amount, CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return amount > 0
                        ? (object)RailwaySignals.Green(new { paymentId, amount })
                        : RailwaySignals.Red("INVALID_TOTAL", "amount must be positive");
                });

            var report = await route.DispatchTrain().TravelAsync();

            Assert.False(report.ReachedDestination);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("INVALID_TOTAL", red.Issue.Code);
            Assert.Equal("Validate", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies that a Pass signal leaves the manifest unchanged.
        /// </summary>
        [Fact]
        public void Station_DataSkip_LeavesManifestUnchanged()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-skip", amount = 12m })
                .Station("NoOp", (string paymentId, decimal amount) => RailwaySignals.Pass);

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-skip", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(12m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a service station recovers from data failure and the route continues.
        /// </summary>
        [Fact]
        public void Station_DataFail_WithServiceStation_ContinuesRoute()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) =>
                    value > 0
                        ? RailwaySignals.Green(new { value })
                        : RailwaySignals.Red("NON_POSITIVE", "value must be positive"))
                .ServiceStation("Recovery", (ref int value, RedSignal red) =>
                {
                    value = 1;
                    return RailwaySignals.Pass;
                })
                .Station("Double", (int value) => new { value = value * 2 });

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.Equal(2, report.TerminalSignal.Manifest.PullWagon<int>("value"));
        }

        /// <summary>
        /// Verifies that a service station returning a red signal stops the route after recovery is attempted.
        /// </summary>
        [Fact]
        public void ServiceStation_DataFail_StopsRouteAfterRecoveryAttempt()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) =>
                    RailwaySignals.Red("NON_POSITIVE", "value must be positive"))
                .ServiceStation("Recovery", (ref int value, RedSignal red) =>
                    RailwaySignals.Red("CANNOT_RECOVER", "recovery declined: " + red.Issue.Code))
                .Station("MustNotRun", (int value) => new { value });

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("CANNOT_RECOVER", red.Issue.Code);
            Assert.Equal("Recovery", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies that an async station handler works correctly with TravelAsync.
        /// </summary>
        [Fact]
        public async Task Station_AsyncHandler_WorksWithTravelAsync()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-async", amount = 5m })
                .Station("Double", async (string paymentId, decimal amount, CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return new { paymentId = paymentId + "-async", amount = amount * 2m };
                });

            var report = await route.DispatchTrain().TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-async-async", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a handler with a manifest parameter can read extra wagons not in its signature.
        /// </summary>
        [Fact]
        public void Station_WithManifestParameter_CanReadExtraWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-manifest", amount = 8m, traceId = "trace-42" })
                .Station("WithManifest", (CargoManifest manifest, string paymentId, decimal amount) =>
                    new
                    {
                        paymentId = paymentId + "-" + manifest.PullWagon<string>("traceId"),
                        amount = amount + 2m,
                    });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-manifest-trace-42", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, manifest.PullWagon<decimal>("amount"));
            Assert.Equal("trace-42", manifest.PullWagon<string>("traceId"));
        }

        /// <summary>
        /// Verifies that a seed station closing over outer variables provides input wagons.
        /// </summary>
        [Fact]
        public void Station_SeedFromOuterScope_ProvidesInputWagons()
        {
            var paymentId = "external";
            var amount = 5m;

            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId, amount })
                .Station("Double", (string paymentId, decimal amount) =>
                    new { paymentId, amount = amount * 2m });

            var report = route.DispatchTrain().Travel();

            Assert.Equal("external", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, report.TerminalSignal.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a static Build method route pattern is recognized as an analysis anchor.
        /// </summary>
        [Fact]
        public void Station_StaticBuildMethod_IsAnalysisAnchorPattern()
        {
            var report = PaymentRoute.Build().DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("anchored", report.TerminalSignal.Manifest.PullWagon<string>("paymentId"));
        }

        /// <summary>
        /// Verifies that a partial return removes temporary wagons that are not returned.
        /// </summary>
        [Fact]
        public void Station_PartialReturn_RemovesTemporaryWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 1, temporary = "keep" })
                .Station("Mutate", (int value, string temporary) => new { value = value + 41 });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal(42, manifest.PullWagon<int>("value"));
            Assert.False(manifest.HasWagon("temporary"));
        }

        private static class PaymentRoute
        {
            public static TrainRoute Build()
            {
                return new TrainRoute()
                    .Station("Seed", () => new { paymentId = "anchored", amount = 1m })
                    .Station("Pass", (string paymentId, decimal amount) => new { paymentId, amount });
            }
        }
    }
}
