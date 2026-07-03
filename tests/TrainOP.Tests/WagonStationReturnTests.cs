using Xunit;

namespace TrainOP.Tests
{
    /// <summary>
    /// Tests reading tuple elements and anonymous type members from station return values.
    /// </summary>
    public sealed class WagonStationReturnTests
    {
        /// <summary>
        /// Verifies that tuple elements are read by ordinal position.
        /// </summary>
        [Fact]
        public void TryGetTupleElement_ReadsByOrdinal()
        {
            var tuple = (paymentId: "pay-1", amount: 2m);

            Assert.True(WagonStationReturn.TryGetTupleElement(tuple, 0, out var paymentId));
            Assert.True(WagonStationReturn.TryGetTupleElement(tuple, 1, out var amount));

            Assert.Equal("pay-1", paymentId);
            Assert.Equal(2m, amount);
        }

        /// <summary>
        /// Verifies that anonymous type properties are read by member name.
        /// </summary>
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
