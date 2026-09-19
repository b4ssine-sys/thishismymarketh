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
