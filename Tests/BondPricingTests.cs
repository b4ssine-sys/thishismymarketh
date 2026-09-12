using System;
using Xunit;

namespace MyFirstMod.Tests
{
    // Pins the pure bond-pricing math in BondPricing. These functions are
    // mathematically correct today; the engine's misuse of them (unit errors,
    // mispricing) is addressed in later phases and lives in BondMarketEngine,
    // not here. Locking this behavior first lets the Phase 1 fixes land safely.
    public class BondPricingTests
    {
        private const float Tol = 1.0f; // currency units of slack for float accumulation

        [Fact]
        public void PresentValue_WithNoRemainingPeriods_ReturnsFaceValue()
        {
            var bond = new Bond("B1", "Matured", 100000f, 0.05f, 12);
            bond.RemainingPeriods = 0;

            float pv = BondPricing.PresentValue(bond, 0.08f);

            Assert.Equal(100000f, pv, 3);
        }

        [Fact]
        public void PresentValue_ParBond_PricesAtFace()
        {
            // Coupon rate == yield => the bond prices exactly at face value.
            var bond = new Bond("B1", "Par", 100000f, 0.05f, 12);

            float pv = BondPricing.PresentValue(bond, 0.05f);

            Assert.InRange(pv, 100000f - Tol, 100000f + Tol);
        }

        [Fact]
        public void PresentValue_DiscountBond_PricesBelowFace()
        {
            // Yield above coupon => price below par.
            var bond = new Bond("B1", "Discount", 100000f, 0.04f, 24);

            float pv = BondPricing.PresentValue(bond, 0.09f);

            Assert.True(pv < 100000f, $"Expected discount price < face, got {pv}");
        }

        [Fact]
        public void PresentValue_PremiumBond_PricesAboveFace()
        {
            // Yield below coupon => price above par.
            var bond = new Bond("B1", "Premium", 100000f, 0.08f, 24);

            float pv = BondPricing.PresentValue(bond, 0.04f);

            Assert.True(pv > 100000f, $"Expected premium price > face, got {pv}");
        }

        [Fact]
        public void PresentValue_MatchesClosedForm()
        {
            // The iterative loop must agree with the annuity closed form
            // (which is what P2-1 will replace it with). This test guards
            // that refactor: PV = C*(1-d)/r + F*d, d = (1+r)^-n.
            var bond = new Bond("B1", "Note", 75000f, 0.061f, 37);
            float annualYield = 0.085f;

            float r = annualYield / BondPricing.PeriodsPerYear;
            float coupon = (bond.FaceValue * bond.CouponRate) / BondPricing.PeriodsPerYear;
            double d = Math.Pow(1.0 + r, -bond.RemainingPeriods);
            double expected = coupon * (1.0 - d) / r + bond.FaceValue * d;

            float pv = BondPricing.PresentValue(bond, annualYield);

            // Slightly wider slack here: the current implementation accumulates
            // the discount factor in float across many periods, so it drifts a
            // little from the double-precision closed form.
            const float closedFormTol = 3.0f;
            Assert.InRange(pv, (float)expected - closedFormTol, (float)expected + closedFormTol);
        }

        [Theory]
        [InlineData(CreditRating.AAA, 0.0020f)]
        [InlineData(CreditRating.AA, 0.0045f)]
        [InlineData(CreditRating.A, 0.0090f)]
        [InlineData(CreditRating.BBB, 0.0160f)]
        [InlineData(CreditRating.BB, 0.0275f)]
        [InlineData(CreditRating.B, 0.0450f)]
        [InlineData(CreditRating.CCC, 0.0800f)]
        [InlineData(CreditRating.D, 0.1500f)]
        public void GetRequiredYield_AddsRatingSpreadToBenchmark(CreditRating rating, float expectedSpread)
        {
            float benchmark = 0.04f;

            float y = BondPricing.GetRequiredYield(benchmark, rating);

            Assert.Equal(benchmark + expectedSpread, y, 5);
        }

        [Fact]
        public void GetRequiredYield_SpreadWidensAsCreditWorsens()
        {
            float benchmark = 0.04f;
            float aaa = BondPricing.GetRequiredYield(benchmark, CreditRating.AAA);
            float bbb = BondPricing.GetRequiredYield(benchmark, CreditRating.BBB);
            float d = BondPricing.GetRequiredYield(benchmark, CreditRating.D);

            Assert.True(aaa < bbb, "AAA should yield less than BBB");
            Assert.True(bbb < d, "BBB should yield less than D");
        }
    }
}
