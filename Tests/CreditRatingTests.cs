// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    public class CreditRatingTests
    {
        [Theory]
        [InlineData(CreditRating.AAA, "AAA")]
        [InlineData(CreditRating.AA, "AA")]
        [InlineData(CreditRating.A, "A")]
        [InlineData(CreditRating.BBB, "BBB")]
        [InlineData(CreditRating.BB, "BB")]
        [InlineData(CreditRating.B, "B")]
        [InlineData(CreditRating.CCC, "CCC")]
        [InlineData(CreditRating.D, "D")]
        public void RatingLabel_ReturnsExpectedString(CreditRating rating, string expected)
        {
            Assert.Equal(expected, BondPricing.RatingLabel(rating));
        }
    }
}

#endif
