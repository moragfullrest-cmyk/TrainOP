using System;
using Xunit;

namespace TrainOP.Tests
{
    /// <summary>
    /// Unit tests for <see cref="StationHandlerParameterNames"/>.
    /// </summary>
    public sealed class StationHandlerParameterNamesTests
    {
        /// <summary>
        /// Verifies wagon names are taken from parameter metadata.
        /// </summary>
        [Fact]
        public void GetWagonInputNames_ReturnsParameterNames_SkippingCancellationToken()
        {
            Func<string, decimal, System.Threading.CancellationToken, object> handler =
                (paymentId, amount, token) => new { paymentId, amount };

            var names = StationHandlerParameterNames.GetWagonInputNames(handler);

            Assert.Equal(new[] { "paymentId", "amount" }, names);
        }

        /// <summary>
        /// Verifies by-ref flags follow wagon parameter order.
        /// </summary>
        [Fact]
        public void GetWagonRefFlags_ReturnsByRefFlags_ForWagonParameters()
        {
            VoidRefHandler handler = (ref string paymentId, ref decimal amount) =>
            {
                paymentId = paymentId + "-x";
                amount = amount + 1m;
            };

            var flags = StationHandlerParameterNames.GetWagonRefFlags(handler);

            Assert.Equal(new[] { true, true }, flags);
        }

        private delegate void VoidRefHandler(ref string paymentId, ref decimal amount);
    }
}
