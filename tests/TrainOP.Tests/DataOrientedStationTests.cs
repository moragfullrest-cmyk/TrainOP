using System.Threading;
using System.Threading.Tasks;
using TrainOP;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    public sealed class DataOrientedStationTests
    {
        [Fact]
        public void Station_Seed_ProducesInitialWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-seed", amount = 10m });

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-seed", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
            Assert.Equal(10m, report.TerminalSignal.Manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_NamedParameters_MergeReturnIntoManifest()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-1", amount = 100m })
                .Station("Discount", (string paymentId, decimal amount) =>
                    new { paymentId, amount = amount * 0.9m });

            var report = route.DispatchTrain().Travel();

            Assert.Equal("pay-1", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
            Assert.Equal(90m, report.TerminalSignal.Manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_PartialReturn_RemovesOmittedWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-partial", amount = 3m, traceId = "keep" })
                .Station("Partial", (string paymentId, decimal amount) =>
                    new { paymentId = paymentId + "-merged" });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-partial-merged", manifest.PullCar<string>("paymentId"));
            Assert.False(manifest.HasCar("amount"));
            Assert.Equal("keep", manifest.PullCar<string>("traceId"));
        }

        [Fact]
        public void Station_ReturnsTuple_MapsByParameterOrder()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-tuple", amount = 4m })
                .Station("ByTuple", (string paymentId, decimal amount) =>
                    (paymentId: paymentId + "-tuple", amount: amount + 2m));

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-tuple-tuple", manifest.PullCar<string>("paymentId"));
            Assert.Equal(6m, manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_DataFail_StopsRouteWithRedSignal()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-fail", amount = -1m })
                .Station("Validate", (string paymentId, decimal amount) =>
                    amount > 0
                        ? Data.Ok(new { paymentId, amount })
                        : Data.Fail("INVALID_TOTAL", "amount must be positive"))
                .Station("MustNotRun", (string paymentId, decimal amount) =>
                    new { paymentId = "nope", amount });

            var report = route.DispatchTrain().Travel();

            Assert.False(report.ReachedDestination);
            Assert.Equal(2, report.Visits.Count);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("INVALID_TOTAL", red.Issue.Code);
            Assert.Equal("Validate", red.Issue.StationName);
        }

        [Fact]
        public async Task Station_DataFail_Async_StopsRouteWithRedSignal()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-async-fail", amount = -5m })
                .Station("Validate", async (string paymentId, decimal amount, CancellationToken token) =>
                {
                    await Task.Delay(1, token);
                    return amount > 0
                        ? (object)Data.Ok(new { paymentId, amount })
                        : Data.Fail("INVALID_TOTAL", "amount must be positive");
                });

            var report = await route.DispatchTrain().TravelAsync();

            Assert.False(report.ReachedDestination);
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("INVALID_TOTAL", red.Issue.Code);
            Assert.Equal("Validate", red.Issue.StationName);
        }

        [Fact]
        public void Station_DataSkip_LeavesManifestUnchanged()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-skip", amount = 12m })
                .Station("NoOp", (string paymentId, decimal amount) => Data.Skip());

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-skip", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
            Assert.Equal(12m, report.TerminalSignal.Manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_DataFail_WithAttachRedSignalStation_ContinuesRoute()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { value = 0 })
                .Station("Validate", (int value) =>
                    value > 0
                        ? Data.Ok(new { value })
                        : Data.Fail("NON_POSITIVE", "value must be positive"))
                .AttachRedSignalStation("Recovery", red =>
                    RailwaySignals.Green(red.Manifest.LoadCar("value", 1)))
                .Station("Double", (int value) => new { value = value * 2 });

            var report = route.DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal(4, report.Visits.Count);
            Assert.Equal(2, report.TerminalSignal.Manifest.PullCar<int>("value"));
        }

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
            Assert.Equal("pay-async-async", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
            Assert.Equal(10m, report.TerminalSignal.Manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_WithManifestParameter_CanReadExtraWagons()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-manifest", amount = 8m, traceId = "trace-42" })
                .Station("WithManifest", (CargoManifest manifest, string paymentId, decimal amount) =>
                    new
                    {
                        paymentId = paymentId + "-" + manifest.PullCar<string>("traceId"),
                        amount = amount + 2m,
                    });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-manifest-trace-42", manifest.PullCar<string>("paymentId"));
            Assert.Equal(10m, manifest.PullCar<decimal>("amount"));
            Assert.Equal("trace-42", manifest.PullCar<string>("traceId"));
        }

        [Fact]
        public void AttachStation_ManifestStyle_StillWorks()
        {
            var route = new TrainRoute()
                .AttachStation("Seed", manifest =>
                    manifest.LoadCar("paymentId", "manifest-style"));

            var report = route.DispatchTrain().Travel();

            Assert.Equal("manifest-style", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
        }
    }
}
