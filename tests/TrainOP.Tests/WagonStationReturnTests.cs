using System.Collections.Generic;
using TrainOP;
using Xunit;

namespace TrainOP.Tests
{
    public sealed class WagonStationReturnTests
    {
        [Fact]
        public void TryGetTupleElementMatchingManifestWagon_FindsReorderedNamedTupleElements()
        {
            var manifest = new CargoManifest()
                .LoadCar("paymentId", "pay-1")
                .LoadCar("amount", 2m);
            var tuple = (amount: 45m, paymentId: "pay-1");
            var used = new HashSet<int>();

            Assert.True(WagonStationReturn.TryGetTupleElementMatchingManifestWagon(
                tuple, manifest, "paymentId", used, out var paymentId));
            Assert.True(WagonStationReturn.TryGetTupleElementMatchingManifestWagon(
                tuple, manifest, "amount", used, out var amount));

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(45m, amount);
        }

        [Fact]
        public void TryGetTupleElement_ReadsByOrdinal()
        {
            var tuple = (paymentId: "pay-1", amount: 2m);

            Assert.True(WagonStationReturn.TryGetTupleElement(tuple, 0, out var paymentId));
            Assert.True(WagonStationReturn.TryGetTupleElement(tuple, 1, out var amount));

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(2m, amount);
        }

        [Fact]
        public void TryGetMemberValue_ReadsAnonymousTypeProperties()
        {
            var value = new { paymentId = "pay-2", amount = 3m };

            Assert.True(WagonStationReturn.TryGetMemberValue(value, "paymentId", out var paymentId));
            Assert.True(WagonStationReturn.TryGetMemberValue(value, "amount", out var amount));

            Assert.Equal("pay-2", paymentId);
            Assert.Equal(3m, amount);
        }
    }
}
