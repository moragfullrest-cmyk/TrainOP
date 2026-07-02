using TrainOP;
using Xunit;

namespace TrainOP.Tests
{
    public sealed class StationMergeTests
    {
        [Fact]
        public void Apply_UsesTupleElementOrdinals_ForReorderedNamedTuple()
        {
            var manifest = new CargoManifest()
                .LoadCar("paymentId", "pay-recover")
                .LoadCar("amount", 50m);

            var stationReturn = (amount: 45m, paymentId: "pay-recover");

            var merged = StationMerge.Apply(
                manifest,
                stationReturn,
                new[] { "paymentId", "amount" },
                removeOmittedRegularInputs: true);

            Assert.Equal("pay-recover", merged.PullCar<string>("paymentId"));
            Assert.Equal(45m, merged.PullCar<decimal>("amount"));
        }
    }
}
