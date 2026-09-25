// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    // WO-41: the monthly curve table gives exactly what evaluating the curve
    // gives, so switching to it leaves every price unchanged.
    public class CurveTableTests
    {
        private static readonly YieldCurve Curve = YieldCurve.FromShortRate(0.035f, 0.048f, 0.004f, 2.0f);

        [Fact]
        public void SpotAndDiscountFactor_MatchTheCurveExactly()
        {
            var t = CurveTable.Build(Curve);
            for (int m = 0; m <= CurveTable.MaxMonths; m++)
            {
                Assert.Equal(Curve.SpotRate((float)m / 12), t.Spot(m));
                if (m > 0) Assert.Equal(Curve.DiscountFactor((float)m / 12), t.DiscountFactor(m));
            }
        }

        [Fact]
        public void Annuity_MatchesSwapPricingExactly()
        {
            var t = CurveTable.Build(Curve);
            for (int m = 1; m <= CurveTable.MaxMonths; m++)
                Assert.Equal(SwapPricing.Annuity(Curve, m, 12), t.Annuity(m));
        }

        [Theory]
        [InlineData(6, 0.04f, true)]
        [InlineData(60, 0.045f, true)]
        [InlineData(120, 0.05f, false)]
        public void SwapValueAndParRate_MatchExactly(int months, float fixedRate, bool payFixed)
        {
            var t = CurveTable.Build(Curve);
            Assert.Equal(SwapPricing.ParSwapRate(Curve, months, 12), SwapPricing.ParSwapRate(t, months));
            Assert.Equal(SwapPricing.SwapValue(Curve, 250000f, fixedRate, months, 12, payFixed),
                SwapPricing.SwapValue(t, 250000f, fixedRate, months, payFixed));
        }

        [Fact]
        public void BeyondTheTable_FallsBackToTheCurve()
        {
            var t = CurveTable.Build(Curve);
            Assert.Equal(Curve.SpotRate(150f / 12), t.Spot(150));
            Assert.Equal(SwapPricing.Annuity(Curve, 150, 12), t.Annuity(150), 5);
        }

        [Fact]
        public void FairYield_FromTableEqualsFromCurve()
        {
            var t = CurveTable.Build(Curve);
            Assert.Equal(AuctionPricing.FairYield(Curve, 0.03f, 24, 12), AuctionPricing.FairYield(t, 0.03f, 24));
            Assert.Equal(0.03f, AuctionPricing.FairYield((CurveTable)null, 0.03f, 24));
        }
    }
}

#endif
