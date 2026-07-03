using TrainOP;
using Xunit;

namespace TrainOP.Tests
{
    public sealed class WagonStationReturnTests
    {
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
