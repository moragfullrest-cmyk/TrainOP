using TrainOP;
using Xunit;

namespace TrainOP.Tests
{
    public sealed class TrainRouteStationTests
    {
        [Fact]
        public void Station_FirstStationSeedsManifest_FromEmptyStart()
        {
            var report = new TrainRoute()
                .AttachStation("Seed", manifest =>
                    manifest
                        .LoadCar("paymentId", "pay-seed")
                        .LoadCar("amount", 10m))
                .AttachStation("Double", manifest =>
                    manifest.LoadCar("amount", manifest.PullCar<decimal>("amount") * 2m))
                .DispatchTrain()
                .Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("pay-seed", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
            Assert.Equal(20m, report.TerminalSignal.Manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_TravelWithManifest_UsesExternalSeed()
        {
            var route = new TrainRoute()
                .AttachStation("Double", manifest =>
                    manifest.LoadCar("amount", manifest.PullCar<decimal>("amount") * 2m));

            var start = new CargoManifest()
                .LoadCar("paymentId", "external")
                .LoadCar("amount", 5m);

            var report = route.DispatchTrain().Travel(start);

            Assert.Equal("external", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
            Assert.Equal(10m, report.TerminalSignal.Manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_StaticBuildMethod_IsAnalysisAnchorPattern()
        {
            var report = PaymentRoute.Build().DispatchTrain().Travel();

            Assert.True(report.ReachedDestination);
            Assert.Equal("anchored", report.TerminalSignal.Manifest.PullCar<string>("paymentId"));
        }

        private static class PaymentRoute
        {
            public static TrainRoute Build()
            {
                return new TrainRoute()
                    .AttachStation("Seed", m => m.LoadCar("paymentId", "anchored"))
                    .AttachStation("Pass", m => m);
            }
        }
    }
}
