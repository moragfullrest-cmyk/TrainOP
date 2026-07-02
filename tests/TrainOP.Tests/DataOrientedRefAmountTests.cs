using TrainOP;
using Xunit;

namespace TrainOP.Tests.DataOriented
{
    public sealed class DataOrientedRefAmountTests
    {
        [Fact]
        public void Station_RefInputNotReturned_IsUpdatedFromRefValue()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-ref", amount = 4m })
                .Station("UpdateRef", (string paymentId, ref decimal amount) =>
                {
                    amount = amount + 6m;
                    return new { paymentId = paymentId + "-ref" };
                });

            var report = route.DispatchTrain().Travel();
            var manifest = report.TerminalSignal.Manifest;

            Assert.Equal("pay-ref-ref", manifest.PullCar<string>("paymentId"));
            Assert.Equal(10m, manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_RefInputWithManifest_CanReadExtraCars()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-ref-manifest", amount = 2m, note = "from-manifest" })
                .Station("UpdateRefWithManifest", (CargoManifest manifest, string paymentId, ref decimal amount, string note) =>
                {
                    amount = amount + manifest.PullCar<decimal>("amount");
                    return new { paymentId = paymentId + "-" + note };
                });

            var report = route.DispatchTrain().Travel();
            var manifest = report.TerminalSignal.Manifest;

            Assert.Equal("pay-ref-manifest-from-manifest", manifest.PullCar<string>("paymentId"));
            Assert.Equal(4m, manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_OptionalWagon_MissingFromManifest_UsesDefault()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-optional" })
                .Station("WithOptional", (string paymentId, decimal? amount) =>
                    new { paymentId, amount = amount ?? 7m });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-optional", manifest.PullCar<string>("paymentId"));
            Assert.Equal(7m, manifest.PullCar<decimal>("amount"));
        }

        [Fact]
        public void Station_OptionalWagon_PresentInManifest_UsesValue()
        {
            var route = new TrainRoute()
                .Station("Seed", () => new { paymentId = "pay-optional", amount = 3m })
                .Station("WithOptional", (string paymentId, decimal? amount) =>
                    new { paymentId, amount = amount ?? 7m });

            var manifest = route.DispatchTrain().Travel().TerminalSignal.Manifest;

            Assert.Equal("pay-optional", manifest.PullCar<string>("paymentId"));
            Assert.Equal(3m, manifest.PullCar<decimal>("amount"));
        }
    }
}
