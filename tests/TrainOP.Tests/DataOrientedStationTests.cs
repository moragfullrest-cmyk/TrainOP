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

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-seed", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, report.Manifest.PullWagon<decimal>("amount"));
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

            var report = route.Travel();

            Assert.Equal("pay-1", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(90m, report.Manifest.PullWagon<decimal>("amount"));
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

            var manifest = route.Travel().Manifest;

            Assert.Equal("pay-partial-merged", manifest.PullWagon<string>("paymentId"));
            Assert.False(manifest.HasWagon("amount"));
            Assert.Equal("keep", manifest.PullWagon<string>("traceId"));
        }

        /// <summary>
        /// Verifies that a named tuple return maps values to manifest wagons by member name.
        /// </summary>
        [Fact]
        public void Station_ReturnsNamedTuple_MergesByName()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-tuple", amount = 4m })
                .Station("ByTuple", (string paymentId, decimal amount) =>
                    (paymentId: paymentId + "-tuple", amount: amount + 2m));

            var manifest = route.Travel().Manifest;

            Assert.Equal("pay-tuple-tuple", manifest.PullWagon<string>("paymentId"));
            Assert.Equal(6m, manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that an unnamed tuple return allocates ItemN wagons and unloads omitted inputs.
        /// </summary>
        [Fact]
        public void Station_ReturnsUnnamedTuple_AllocatesItemNWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-tuple", amount = 4m })
                .Station("ByTuple", (string paymentId, decimal amount) =>
                    (paymentId + "-tuple", amount + 2m));

            var manifest = route.Travel().Manifest;

            Assert.Equal("pay-tuple-tuple", manifest.PullWagon<string>("Item1"));
            Assert.Equal(6m, manifest.PullWagon<decimal>("Item2"));
            Assert.False(manifest.HasWagon("paymentId"));
            Assert.False(manifest.HasWagon("amount"));
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

            var report = route.Travel();

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

            var report = await route.TravelAsync();

            Assert.False(report.ReachedDestination);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("INVALID_TOTAL", red.Issue.Code);
            Assert.Equal("Validate", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies that a White signal leaves the manifest unchanged.
        /// </summary>
        [Fact]
        public void Station_DataSkip_LeavesManifestUnchanged()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-skip", amount = 12m })
                .Station("NoOp", (string paymentId, decimal amount) => RailwaySignals.White);

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-skip", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(12m, report.Manifest.PullWagon<decimal>("amount"));
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
                .ServiceStation("Recovery", (int value, RedSignal red) =>
                    RailwaySignals.Green(new { value = 1 }))
                .Station("Double", (int value) => new { value = value * 2 });

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.Equal(2, report.Manifest.PullWagon<int>("value"));
        }

        /// <summary>
        /// Verifies that a service station returning a red signal leaves the route red so later regular stations are skipped.
        /// </summary>
        [Fact]
        public void ServiceStation_DataFail_StopsRouteAfterRecoveryAttempt()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) =>
                    RailwaySignals.Red("NON_POSITIVE", "value must be positive"))
                .ServiceStation("Recovery", (int value, RedSignal red) =>
                    RailwaySignals.Red("CANNOT_RECOVER", "recovery declined: " + red.Issue.Code))
                .Station("MustNotRun", (int value) => new { value });

            var report = route.Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(3, report.Visits.Count);
            Assert.DoesNotContain(report.Visits, visit => visit.StationName == "MustNotRun");
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("CANNOT_RECOVER", red.Issue.Code);
            Assert.Equal("Recovery", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies consecutive service stations after a failure: declining by returning the same red
        /// lets the next recovery still see the original issue code.
        /// </summary>
        [Fact]
        public void MultipleServiceStations_LaterHandler_RecoversOriginalFailure()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) =>
                    RailwaySignals.Red("STOCK_LIMIT", "out of stock"))
                .ServiceStation("First", (int value, SignalIssue issue) =>
                    issue.Code == "NON_POSITIVE"
                        ? RailwaySignals.Green(new { value = 1 })
                        : RailwaySignals.Red(issue.Code, issue.Message))
                .ServiceStation("Second", (int value, SignalIssue issue) =>
                    issue.Code == "STOCK_LIMIT"
                        ? RailwaySignals.Green(new { value = 4 })
                        : RailwaySignals.Red("NOPE", "still unsupported"))
                .Station("Double", (int value) => new { value = value * 2 });

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(5, report.Visits.Count);
            Assert.Equal("First", report.Visits[2].StationName);
            Assert.False(report.Visits[2].IsGreen);
            Assert.Equal("Second", report.Visits[3].StationName);
            Assert.True(report.Visits[3].IsGreen);
            Assert.Equal(8, report.Manifest.PullWagon<int>("value"));
        }

        /// <summary>
        /// Verifies that when every service station after a failure declines, the terminal red is from the last hop.
        /// </summary>
        [Fact]
        public void MultipleServiceStations_AllDecline_UsesLastRed()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) =>
                    RailwaySignals.Red("NON_POSITIVE", "value must be positive"))
                .ServiceStation("First", (int value, SignalIssue issue) =>
                    RailwaySignals.Red("SKIP_FIRST", "declined by first"))
                .ServiceStation("Second", (int value, SignalIssue issue) =>
                    RailwaySignals.Red("SKIP_SECOND", "declined by second"))
                .Station("MustNotRun", (int value) => new { value });

            var report = route.Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.DoesNotContain(report.Visits, visit => visit.StationName == "MustNotRun");
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("SKIP_SECOND", red.Issue.Code);
            Assert.Equal("Second", red.Issue.StationName);
        }

        /// <summary>
        /// Verifies positional recovery: each failure enters only the service station(s) that follow it.
        /// </summary>
        [Fact]
        public void MultipleServiceStations_Recover_Twice_AlongRoute()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("FailOnce", (int value) =>
                    RailwaySignals.Red("FIRST", "first failure"))
                .ServiceStation("RecoverFirst", (int value, SignalIssue issue) =>
                    issue.Code == "FIRST"
                        ? RailwaySignals.Green(new { value = 1 })
                        : RailwaySignals.Red("SKIP", "not first"))
                .Station("FailTwice", (int value) =>
                    RailwaySignals.Red("SECOND", "second failure"))
                .ServiceStation("RecoverSecond", (int value, SignalIssue issue) =>
                    issue.Code == "SECOND"
                        ? RailwaySignals.Green(new { value = 3 })
                        : RailwaySignals.Red("SKIP", "not second"))
                .Station("Double", (int value) => new { value = value * 2 });

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(6, report.Manifest.PullWagon<int>("value"));
            Assert.Contains(report.Visits, visit => visit.StationName == "RecoverFirst" && visit.IsGreen);
            Assert.Contains(report.Visits, visit => visit.StationName == "RecoverSecond" && visit.IsGreen);
            Assert.Equal(1, System.Linq.Enumerable.Count(report.Visits, visit => visit.StationName == "RecoverFirst"));
            Assert.Equal(1, System.Linq.Enumerable.Count(report.Visits, visit => visit.StationName == "RecoverSecond"));
        }

        /// <summary>
        /// Verifies that a service station after green is skipped, and a later failure reaches its own recovery.
        /// </summary>
        [Fact]
        public void ServiceStation_Skipped_AfterGreen_UntilLaterFailure()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 1 })
                .ServiceStation("TooEarly", (int value, RedSignal red) =>
                    RailwaySignals.Green(new { value = 99 }))
                .Station("Fail", (int value) =>
                    RailwaySignals.Red("LATER", "fail after skip"))
                .ServiceStation("Recover", (int value, SignalIssue issue) =>
                    RailwaySignals.Green(new { value = 5 }))
                .Station("Double", (int value) => new { value = value * 2 });

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.DoesNotContain(report.Visits, visit => visit.StationName == "TooEarly");
            Assert.Equal(10, report.Manifest.PullWagon<int>("value"));
        }

        /// <summary>
        /// Verifies that ServiceStation overlay updates existing wagons from a green payload
        /// without unloading omitted inputs (composition stays intact; TOP015 forbids new keys).
        /// </summary>
        [Fact]
        public void ServiceStation_GreenPayload_OverlaysExistingWagonsOnly()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 0m, traceId = "keep" })
                .Station("Validate", (string paymentId, decimal amount) =>
                    RailwaySignals.Red("INVALID", "amount must be positive"))
                .ServiceStation("Recovery", (decimal amount, SignalIssue issue) =>
                    RailwaySignals.Green(new { amount = 10m, traceId = "changed" }))
                .Station("After", (string paymentId, decimal amount, string traceId) =>
                    new { paymentId, amount, traceId });

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-1", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, report.Manifest.PullWagon<decimal>("amount"));
            Assert.Equal("changed", report.Manifest.PullWagon<string>("traceId"));
        }

        /// <summary>
        /// Verifies that White on ServiceStation does not write ref mutations back to the manifest.
        /// </summary>
        [Fact]
        public void ServiceStation_Pass_DoesNotWritebackRefMutations()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) => RailwaySignals.Red("NON_POSITIVE", "value must be positive"))
                .ServiceStation("Recovery", (ref int value, RedSignal red) =>
                {
                    value = 1;
                    return RailwaySignals.White;
                })
                .Station("Double", (int value) => new { value = value * 2 });

            var report = route.Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(0, report.Manifest.PullWagon<int>("value"));
        }

        /// <summary>
        /// Verifies that a red ServiceStation return leaves the live manifest unchanged.
        /// </summary>
        [Fact]
        public void ServiceStation_Red_DoesNotChangeWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 7 })
                .Station("Validate", (int value) => RailwaySignals.Red("NOPE", "stop"))
                .ServiceStation("Recovery", (int value, RedSignal red) =>
                    RailwaySignals.Red("CANNOT_RECOVER", "declined"));

            var report = route.Travel();

            Assert.False(report.ReachedDestination);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal(7, report.Manifest.PullWagon<int>("value"));
        }

        /// <summary>
        /// Verifies that async data-oriented ServiceStation can recover by returning overlay values.
        /// </summary>
        [Fact]
        public async Task ServiceStation_AsyncByValue_OverlaysExistingWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) => RailwaySignals.Red("NON_POSITIVE", "value must be positive"))
                .ServiceStation("Recovery", async (int value, RedSignal red, CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return new { value = 3 };
                })
                .Station("Double", (int value) => new { value = value * 2 });

            var report = await route.TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal(6, report.Manifest.PullWagon<int>("value"));
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

            var report = await route.TravelAsync();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-async-async", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, report.Manifest.PullWagon<decimal>("amount"));
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

            var manifest = route.Travel().Manifest;

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

            var report = route.Travel();

            Assert.Equal("external", report.Manifest.PullWagon<string>("paymentId"));
            Assert.Equal(10m, report.Manifest.PullWagon<decimal>("amount"));
        }

        /// <summary>
        /// Verifies that a static Build method route pattern is recognized as an analysis anchor.
        /// </summary>
        [Fact]
        public void Station_StaticBuildMethod_IsAnalysisAnchorPattern()
        {
            var report = PaymentRoute.Build().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("anchored", report.Manifest.PullWagon<string>("paymentId"));
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

            var manifest = route.Travel().Manifest;

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
