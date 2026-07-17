using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using TrainOP.Generators.Models;
using Xunit;

namespace TrainOP.Generators.Tests
{
    /// <summary>
    /// Tests for <see cref="TerminalWagonsComparer"/>.
    /// </summary>
    public sealed class TerminalWagonsComparerTests
    {
        private static readonly CSharpCompilation SharedCompilation =
            CSharpCompilation.Create("TerminalWagonsComparerTests");

        /// <summary>
        /// Verifies that wagon sets with different order but same names/types are equivalent.
        /// </summary>
        [Fact]
        public void AreEquivalent_DifferentOrder_ReturnsTrue()
        {
            var left = ImmutableArray.Create(
                CreateBinding("amount", SpecialType.System_Decimal),
                CreateBinding("paymentId", SpecialType.System_String));
            var right = ImmutableArray.Create(
                CreateBinding("paymentId", SpecialType.System_String),
                CreateBinding("amount", SpecialType.System_Decimal));

            Assert.True(TerminalWagonsComparer.AreEquivalent(left, right));
        }

        /// <summary>
        /// Verifies that missing wagons are detected as not equivalent.
        /// </summary>
        [Fact]
        public void AreEquivalent_MissingWagon_ReturnsFalse()
        {
            var left = ImmutableArray.Create(
                CreateBinding("paymentId", SpecialType.System_String),
                CreateBinding("amount", SpecialType.System_Decimal));
            var right = ImmutableArray.Create(
                CreateBinding("paymentId", SpecialType.System_String));

            Assert.False(TerminalWagonsComparer.AreEquivalent(left, right));
        }

        /// <summary>
        /// Verifies that type conflicts are detected as not equivalent.
        /// </summary>
        [Fact]
        public void AreEquivalent_TypeConflict_ReturnsFalse()
        {
            var left = ImmutableArray.Create(CreateBinding("value", SpecialType.System_Int32));
            var right = ImmutableArray.Create(CreateBinding("value", SpecialType.System_String));

            Assert.False(TerminalWagonsComparer.AreEquivalent(left, right));
        }

        private static WagonBinding CreateBinding(string name, SpecialType specialType)
        {
            var typeSymbol = SharedCompilation.GetSpecialType(specialType);
            return new WagonBinding(
                name,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                typeSymbol,
                Location.None);
        }
    }
}
