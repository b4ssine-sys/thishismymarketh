using System;
using Xunit;

namespace MyFirstMod.Tests
{
    // Pins the pure demand / pressure / vitals chain in CimDemandEngine.
    // P1-11 will change how CalculateAbsorptionCapacity is driven, and P1-1
    // removes the engine-side fudge that the vitalFloor term in
    // CalculateDemandScore papers over, but the component functions below are
    // pure and their shape (bounds, monotonicity, clamps) is what these tests
    // lock down.
    public class CimDemandEngineTests
    {
        [Theory]
        [InlineData(CreditRating.AAA, 1.00f)]
        [InlineData(CreditRating.AA, 0.88f)]
        [InlineData(CreditRating.A, 0.76f)]
        [InlineData(CreditRating.BBB, 0.62f)]
        [InlineData(CreditRating.BB, 0.45f)]
        [InlineData(CreditRating.B, 0.28f)]
        [InlineData(CreditRating.CCC, 0.12f)]
        [InlineData(CreditRating.D, 0.00f)]
        public void RatingToScore_MapsEachRating(CreditRating rating, float expected)
        {
            Assert.Equal(expected, CimDemandEngine.RatingToScore(rating), 4);
        }

        [Fact]
        public void CalculateCityVitals_StaysWithinUnitInterval()
        {
            float low = CimDemandEngine.CalculateCityVitals(10, 0f, 0f, 0f, 0f, 1f);
            float high = CimDemandEngine.CalculateCityVitals(1_000_000, 1f, 1f, 1f, 1f, 0f);

            Assert.InRange(low, 0f, 1f);
            Assert.InRange(high, 0f, 1f);
            Assert.True(high > low, "Healthier city should score higher vitals");
        }

        [Fact]
        public void CalculateCityVitals_ClampsOutOfRangeInputs()
        {
            // Out-of-range inputs must not push the result outside [0,1].
            float v = CimDemandEngine.CalculateCityVitals(500, 5f, -3f, 2f, -1f, -4f);
            Assert.InRange(v, 0f, 1f);
        }

        [Fact]
        public void CalculateDefaultProbability_IncreasesWithBurdenAndPenalty()
        {
            float baseline = CimDemandEngine.CalculateDefaultProbability(0.10f, 2.0f, 0, 0.1f);
            float heavier = CimDemandEngine.CalculateDefaultProbability(0.30f, 2.0f, 0, 0.1f);
            float penalised = CimDemandEngine.CalculateDefaultProbability(0.10f, 2.0f, 5, 0.1f);

            Assert.InRange(baseline, 0f, 1f);
            Assert.True(heavier > baseline, "Higher burden should raise default probability");
            Assert.True(penalised > baseline, "Default penalty should raise default probability");
        }

        [Fact]
        public void CalculateDefaultProbability_LowCoverageRaisesRisk()
        {
            float covered = CimDemandEngine.CalculateDefaultProbability(0.10f, 1.5f, 0, 0.1f);
            float uncovered = CimDemandEngine.CalculateDefaultProbability(0.10f, 0.5f, 0, 0.1f);
            Assert.True(uncovered > covered, "DSCR below 1.0 should raise default probability");
        }

        [Theory]
        [InlineData(100f, 0f, 1f)]    // all buyers
        [InlineData(0f, 100f, -1f)]   // all sellers
        [InlineData(50f, 50f, 0f)]    // balanced
        [InlineData(0f, 0f, 0f)]      // no activity
        public void CalculateMarketPressure_ReturnsNormalisedImbalance(
            float buy, float sell, float expected)
        {
            Assert.Equal(expected, CimDemandEngine.CalculateMarketPressure(buy, sell), 4);
        }

        [Fact]
        public void AdjustYieldForPressure_StaysWithinMultiplierBand()
        {
            float baseYield = 0.06f;
            float strongBuy = CimDemandEngine.AdjustYieldForPressure(baseYield, 1f);
            float strongSell = CimDemandEngine.AdjustYieldForPressure(baseYield, -1f);

            // Multiplier clamped to [0.90, 1.15].
            Assert.InRange(strongBuy, baseYield * 0.90f, baseYield * 1.15f);
            Assert.InRange(strongSell, baseYield * 0.90f, baseYield * 1.15f);
            Assert.True(strongBuy > strongSell, "Buy pressure should lift yield above sell pressure");
        }

        [Fact]
        public void AdjustYieldForDemand_StrongDemandLowersYield()
        {
            float baseYield = 0.06f;
            float weak = CimDemandEngine.AdjustYieldForDemand(baseYield, 0.0f);
            float strong = CimDemandEngine.AdjustYieldForDemand(baseYield, 1.0f);

            Assert.True(strong < weak, "Stronger demand should lower required yield");
            Assert.InRange(strong, baseYield * 0.85f, baseYield * 1.20f);
            Assert.InRange(weak, baseYield * 0.85f, baseYield * 1.20f);
        }

        [Fact]
        public void CalculateDemandScore_StaysWithinUnitInterval()
        {
            var strong = new MarketState
            {
                FinancialHealth = 1f, CitizenConfidence = 1f, BondAppeal = 1f, CityVitals = 1f
            };
            var weak = new MarketState
            {
                FinancialHealth = 0f, CitizenConfidence = 0f, BondAppeal = 0f, CityVitals = 0f
            };

            float s = CimDemandEngine.CalculateDemandScore(strong, weak);
            float w = CimDemandEngine.CalculateDemandScore(weak, strong);

            Assert.InRange(s, 0f, 1f);
            Assert.InRange(w, 0f, 1f);
        }

        [Fact]
        public void CalculateAbsorptionCapacity_IsPositiveAndScalesWithPopulation()
        {
            float small = CimDemandEngine.CalculateAbsorptionCapacity(1_000, 100_000f, 0.5f);
            float large = CimDemandEngine.CalculateAbsorptionCapacity(100_000, 100_000f, 0.5f);

            Assert.True(small > 0f);
            Assert.True(large > small, "Larger population should raise absorption capacity");
        }

        [Theory]
        [InlineData(0.85f, "STRONG")]
        [InlineData(0.65f, "HEALTHY")]
        [InlineData(0.45f, "MODERATE")]
        [InlineData(0.25f, "WEAK")]
        [InlineData(0.12f, "VERY WEAK")]
        [InlineData(0.05f, "NO DEMAND")]
        public void DemandLabel_MapsScoreToBand(float score, string expected)
        {
            Assert.Equal(expected, CimDemandEngine.DemandLabel(score));
        }

        [Theory]
        [InlineData(0.4f, "STRONG BUY")]
        [InlineData(0.2f, "BUYING")]
        [InlineData(0.0f, "BALANCED")]
        [InlineData(-0.2f, "SELLING")]
        [InlineData(-0.4f, "STRONG SELL")]
        public void PressureLabel_MapsPressureToBand(float pressure, string expected)
        {
            Assert.Equal(expected, CimDemandEngine.PressureLabel(pressure));
        }

        [Fact]
        public void CalculateCitizenActivity_ProducesNonNegativeVolumes()
        {
            var rng = new Random(12345);
            CimDemandEngine.CalculateCitizenActivity(
                50_000, 0.6f, 0.7f, 0.1f, rng,
                out float buy, out float sell);

            Assert.True(buy >= 0f, "Buy volume must be non-negative");
            Assert.True(sell >= 0f, "Sell volume must be non-negative");
            Assert.True(buy + sell > 0f, "Some activity expected for a healthy city");
        }
    }
}
