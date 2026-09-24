// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using System;
using Xunit;

namespace MyFirstMod.Tests
{
    // Phase 4: yield curve, swap pricing, and the stochastic short-rate process.
    public class RatesMarketTests
    {
        private const int PPY = 12;

        // ---- YieldCurve (Nelson-Siegel) ----

        [Fact]
        public void SpotRate_ShortEndApproachesLevelPlusSlope()
        {
            var c = YieldCurve.FromShortRate(0.03f, 0.05f, 0f, 2f); // short 3%, long 5%
            float shortEnd = c.SpotRate(0.01f);
            Assert.InRange(shortEnd, 0.028f, 0.032f); // ~ Level + Slope = 0.03
        }

        [Fact]
        public void SpotRate_LongEndApproachesLevel()
        {
            var c = YieldCurve.FromShortRate(0.03f, 0.05f, 0f, 2f);
            float longEnd = c.SpotRate(50f);
            Assert.InRange(longEnd, 0.048f, 0.052f); // -> Level = 0.05
        }

        [Fact]
        public void DiscountFactor_IsBetweenZeroAndOne_AndDecreasing()
        {
            var c = YieldCurve.FromShortRate(0.04f, 0.05f, 0f, 2f);
            float df1 = c.DiscountFactor(1f);
            float df10 = c.DiscountFactor(10f);
            Assert.InRange(df1, 0f, 1f);
            Assert.InRange(df10, 0f, 1f);
            Assert.True(df10 < df1); // longer horizon discounts more
            Assert.Equal(1f, c.DiscountFactor(0f), 5);
        }

        [Fact]
        public void UpwardCurve_LongYieldExceedsShort()
        {
            var c = YieldCurve.FromShortRate(0.02f, 0.06f, 0f, 2f); // steep upward
            Assert.True(c.SpotRate(10f) > c.SpotRate(0.5f));
        }

        // ---- SwapPricing ----

        [Fact]
        public void AtMarketSwap_HasNearZeroValue()
        {
            var c = YieldCurve.FromShortRate(0.04f, 0.05f, 0f, 2f);
            int n = 24;
            float par = SwapPricing.ParSwapRate(c, n, PPY);
            float value = SwapPricing.SwapValue(c, 1_000_000f, par, n, PPY, true);
            Assert.InRange(value, -50f, 50f); // priced at par -> ~0 MTM
        }

        [Fact]
        public void PayFixed_GainsWhenParExceedsFixed()
        {
            var c = YieldCurve.FromShortRate(0.06f, 0.06f, 0f, 2f);
            int n = 24;
            float par = SwapPricing.ParSwapRate(c, n, PPY);
            // Fixed well below par -> pay-fixed receiver is in the money.
            float value = SwapPricing.SwapValue(c, 1_000_000f, par - 0.02f, n, PPY, true);
            Assert.True(value > 0f);
            // Receive-fixed is the mirror image.
            float recv = SwapPricing.SwapValue(c, 1_000_000f, par - 0.02f, n, PPY, false);
            Assert.InRange(recv, -value - 1f, -value + 1f);
        }

        [Fact]
        public void ParSwapRate_TracksCurveLevel()
        {
            var low = YieldCurve.FromShortRate(0.02f, 0.02f, 0f, 2f);
            var high = YieldCurve.FromShortRate(0.06f, 0.06f, 0f, 2f);
            Assert.True(SwapPricing.ParSwapRate(high, 24, PPY) > SwapPricing.ParSwapRate(low, 24, PPY));
        }

        // ---- RateProcess (Vasicek/OU) ----

        [Fact]
        public void Step_WithNoShock_RevertsTowardTheta()
        {
            float r = 0.02f, theta = 0.05f;
            float next = RateProcess.Step(r, 0.3f, theta, 0.01f, 1f, 0f);
            Assert.True(next > r);        // moves up toward theta
            Assert.True(next < theta);   // but does not overshoot in one step
        }

        [Fact]
        public void Step_IsFlooredAndCapped()
        {
            float lowered = RateProcess.Step(0.001f, 0.3f, 0.05f, 0.5f, 1f, -100f);
            Assert.True(lowered >= RateProcess.RateFloor);
            float raised = RateProcess.Step(0.4f, 0.3f, 0.05f, 0.5f, 1f, 100f);
            Assert.True(raised <= 0.5f);
        }

        [Fact]
        public void CycleTheta_StaysWithinAmplitudeBand()
        {
            for (float phase = 0f; phase < 7f; phase += 0.5f)
            {
                float t = RateProcess.CycleTheta(0.04f, 0.015f, phase);
                Assert.InRange(t, 0.025f - 1e-4f, 0.055f + 1e-4f);
            }
        }

        [Fact]
        public void NextGaussian_IsApproximatelyStandardNormal()
        {
            var rng = new Random(12345);
            double sum = 0; int n = 20000;
            for (int i = 0; i < n; i++) sum += RateProcess.NextGaussian(rng);
            double mean = sum / n;
            Assert.InRange(mean, -0.05, 0.05); // ~ 0
        }

        [Fact]
        public void AdvancePhase_WrapsIntoTwoPi()
        {
            float p = RateProcess.AdvancePhase(6.2f, 0.5f);
            Assert.InRange(p, 0f, (float)(2 * Math.PI));
        }
    }
}

#endif
