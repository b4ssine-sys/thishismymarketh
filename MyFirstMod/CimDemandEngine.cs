using System;

namespace MyFirstMod
{
    public static class CimDemandEngine
    {
        private const float W_FINANCIAL = 0.35f;
        private const float W_CONFIDENCE = 0.30f;
        private const float W_APPEAL = 0.35f;
        private const float WEALTH_PER_CAPITA = 500f;
        private const float TRADE_PER_CITIZEN = 12f;
        public const float MIN_ISSUABLE_DEMAND = 0.10f;

        public static float RatingToScore(CreditRating rating)
        {
            switch (rating)
            {
                case CreditRating.AAA: return 1.00f;
                case CreditRating.AA:  return 0.88f;
                case CreditRating.A:   return 0.76f;
                case CreditRating.BBB: return 0.62f;
                case CreditRating.BB:  return 0.45f;
                case CreditRating.B:   return 0.28f;
                case CreditRating.CCC: return 0.12f;
                case CreditRating.D:   return 0.00f;
                default: return 0.30f;
            }
        }

        public static float CalculateFinancialHealth(CreditRating rating, float dscr, float debtBurden)
        {
            float ratingScore = RatingToScore(rating);

            float dscrScore = dscr / 3.0f;
            if (dscrScore < 0f) dscrScore = 0f;
            if (dscrScore > 1f) dscrScore = 1f;

            float burdenScore = 1f - debtBurden;
            if (burdenScore < 0f) burdenScore = 0f;
            if (burdenScore > 1f) burdenScore = 1f;

            return ratingScore * 0.50f + dscrScore * 0.30f + burdenScore * 0.20f;
        }

        public static float CalculateCitizenConfidence(float happiness, float employmentRate, float populationGrowth)
        {
            float hScore = happiness;
            if (hScore < 0f) hScore = 0f;
            if (hScore > 1f) hScore = 1f;

            float eScore = employmentRate;
            if (eScore < 0f) eScore = 0f;
            if (eScore > 1f) eScore = 1f;

            float gScore = (populationGrowth + 0.05f) / 0.10f;
            if (gScore < 0f) gScore = 0f;
            if (gScore > 1f) gScore = 1f;

            return hScore * 0.40f + eScore * 0.35f + gScore * 0.25f;
        }

        public static float CalculateDefaultProbability(
            float debtBurden, float dscr, int defaultPenalty, float revenueVolatility)
        {
            float basePr = debtBurden * 0.8f;

            if (dscr < 1.0f)
                basePr += (1.0f - dscr) * 0.4f;
            else
                basePr -= Math.Min(dscr - 1.0f, 2.0f) * 0.1f;

            float penaltyFactor = defaultPenalty * 0.03f;
            float volFactor = revenueVolatility * 0.15f;

            float prob = basePr + penaltyFactor + volFactor;
            if (prob < 0f) prob = 0f;
            if (prob > 1f) prob = 1f;
            return prob;
        }

        public static float CalculateBondAppeal(
            float couponRate, float benchmarkRate, float defaultProbability)
        {
            float spread = couponRate - benchmarkRate;

            float spreadScore = 0.5f + spread * 10f;
            if (spreadScore < 0f) spreadScore = 0f;
            if (spreadScore > 1f) spreadScore = 1f;

            float riskMultiplier = 1f - defaultProbability * 0.8f;
            if (riskMultiplier < 0.1f) riskMultiplier = 0.1f;

            return spreadScore * riskMultiplier;
        }

        public static float CalculateDemandScore(
            float financialHealth, float citizenConfidence, float bondAppeal)
        {
            float raw = W_FINANCIAL * financialHealth
                      + W_CONFIDENCE * citizenConfidence
                      + W_APPEAL * bondAppeal;
            if (raw < 0f) raw = 0f;
            if (raw > 1f) raw = 1f;
            return raw;
        }

        public static float AdjustYieldForDemand(float baseYield, float demandScore)
        {
            float multiplier = 1f + (0.5f - demandScore) * 0.40f;
            if (multiplier < 0.90f) multiplier = 0.90f;
            if (multiplier > 1.15f) multiplier = 1.15f;
            return baseYield * multiplier;
        }

        public static float CalculateAbsorptionCapacity(
            int population, float avgIncomePerTick, float demandScore)
        {
            float popCapacity = population * WEALTH_PER_CAPITA;

            float incomeMultiplier = 1f;
            if (avgIncomePerTick > 0f)
            {
                incomeMultiplier = avgIncomePerTick / 1000f;
                if (incomeMultiplier < 0.2f) incomeMultiplier = 0.2f;
                if (incomeMultiplier > 10f) incomeMultiplier = 10f;
            }

            float demandMultiplier = 0.1f + demandScore * 0.9f;
            return popCapacity * incomeMultiplier * demandMultiplier;
        }

        public static string DemandLabel(float demandScore)
        {
            if (demandScore >= 0.80f) return "STRONG";
            if (demandScore >= 0.60f) return "HEALTHY";
            if (demandScore >= 0.40f) return "MODERATE";
            if (demandScore >= 0.20f) return "WEAK";
            if (demandScore >= 0.10f) return "VERY WEAK";
            return "NO DEMAND";
        }

        public static void CalculateCitizenActivity(
            int population, float demandScore, float bondAppeal,
            float defaultProbability, System.Random rng,
            out float buyVolume, out float sellVolume)
        {
            float participationRate = 0.01f + demandScore * 0.04f;
            float activePopulation = population * participationRate;
            if (activePopulation < 1f) activePopulation = 1f;

            float buyBias = 0.5f + (bondAppeal - 0.5f) * 0.6f;
            buyBias -= defaultProbability * 0.3f;
            if (buyBias < 0.1f) buyBias = 0.1f;
            if (buyBias > 0.9f) buyBias = 0.9f;

            float noise = (float)(rng.NextDouble() * 0.2 - 0.1);
            float adjustedBias = buyBias + noise;
            if (adjustedBias < 0.05f) adjustedBias = 0.05f;
            if (adjustedBias > 0.95f) adjustedBias = 0.95f;

            buyVolume = activePopulation * adjustedBias * TRADE_PER_CITIZEN;
            sellVolume = activePopulation * (1f - adjustedBias) * TRADE_PER_CITIZEN;
        }

        public static float CalculateMarketPressure(float buyVolume, float sellVolume)
        {
            float total = buyVolume + sellVolume;
            if (total <= 0f) return 0f;
            float pressure = (buyVolume - sellVolume) / total;
            if (pressure < -1f) pressure = -1f;
            if (pressure > 1f) pressure = 1f;
            return pressure;
        }

        public static float AdjustYieldForPressure(float baseYield, float pressure)
        {
            float multiplier = 1f + pressure * 0.10f;
            if (multiplier < 0.93f) multiplier = 0.93f;
            if (multiplier > 1.10f) multiplier = 1.10f;
            return baseYield * multiplier;
        }

        public static string PressureLabel(float pressure)
        {
            if (pressure > 0.3f) return "STRONG BUY";
            if (pressure > 0.1f) return "BUYING";
            if (pressure < -0.3f) return "STRONG SELL";
            if (pressure < -0.1f) return "SELLING";
            return "BALANCED";
        }
    }
}
