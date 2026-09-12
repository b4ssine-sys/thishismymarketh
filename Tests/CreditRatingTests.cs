using Xunit;

namespace MyFirstMod.Tests
{
    // Pins the rating table in BondPricing.CalculateRating. P1-1 will fix the
    // 15x unit error in how the ENGINE feeds debtBurden/dscr into this table,
    // but the table thresholds themselves are the textbook definition and
    // should not move. These tests assert the table as written.
    public class CreditRatingTests
    {
        [Theory]
        // debtBurden, dscr, expected
        [InlineData(0.04f, 3.5f, CreditRating.AAA)] // burden<0.05 & dscr>3.0
        [InlineData(0.08f, 2.5f, CreditRating.AA)]  // burden<0.10 & dscr>2.0
        [InlineData(0.12f, 1.7f, CreditRating.A)]   // burden<0.15 & dscr>1.5
        [InlineData(0.20f, 1.3f, CreditRating.BBB)] // burden<0.25 & dscr>1.2
        [InlineData(0.30f, 1.0f, CreditRating.BB)]  // burden<0.35 & dscr>0.9
        [InlineData(0.90f, 0.85f, CreditRating.B)]  // dscr>0.8 (burden excludes higher tiers)
        [InlineData(0.90f, 0.60f, CreditRating.CCC)]// dscr>0.5
        [InlineData(0.90f, 0.30f, CreditRating.D)]  // fallthrough
        public void CalculateRating_MapsRatiosToExpectedRating(
            float debtBurden, float dscr, CreditRating expected)
        {
            Assert.Equal(expected, BondPricing.CalculateRating(debtBurden, dscr));
        }

        [Fact]
        public void CalculateRating_HighBurdenBlocksTopRatingsEvenWithStrongCoverage()
        {
            // Strong DSCR but heavy burden cannot reach investment grade.
            CreditRating r = BondPricing.CalculateRating(0.50f, 5.0f);
            Assert.Equal(CreditRating.B, r); // only the dscr>0.8 gate catches it
        }

        [Fact]
        public void CalculateRating_WeakCoverageForcesDefault()
        {
            Assert.Equal(CreditRating.D, BondPricing.CalculateRating(0.02f, 0.1f));
        }

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
