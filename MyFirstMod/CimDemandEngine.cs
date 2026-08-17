using System;

namespace MyFirstMod
{
    public struct MarketState
    {
        public float FinancialHealth;
        public float CitizenConfidence;
        public float BondAppeal;
        public float CityVitals;
    }

    public static class CimDemandEngine
    {
        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

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

        public static float CalculateCityVitals(
            int population, float happiness, float health,
            float education, float landValue, float crimeRate)
        {
            float popScore = Clamp((float)Math.Log10(Math.Max(population, 10)) / 5f, 0f, 1f);
            float safetyScore = Clamp(1f - crimeRate, 0f, 1f);
            happiness = Clamp(happiness, 0f, 1f);
            health = Clamp(health, 0f, 1f);
            education = Clamp(education, 0f, 1f);
            landValue = Clamp(landValue, 0f, 1f);

            return popScore * 0.15f
                 + happiness * 0.25f
                 + health * 0.20f
                 + education * 0.15f
                 + landValue * 0.10f
                 + safetyScore * 0.15f;
        }

        public static float CalculateFinancialHealth(
            float cashReserves, float debtBurden, float dscr, CreditRating rating)
        {
            float ratingScore = RatingToScore(rating);
            float dscrScore = Clamp(dscr / 3.0f, 0f, 1f);
            float burdenScore = Clamp(1f - debtBurden, 0f, 1f);
            float cashScore = Clamp((float)Math.Log10(Math.Max(cashReserves, 1f)) / 8f, 0f, 1f);

            return ratingScore * 0.20f + dscrScore * 0.15f + burdenScore * 0.30f + cashScore * 0.35f;
        }

        public static float CalculateCitizenConfidence(float happiness, float employmentRate, float populationGrowth)
        {
            float hScore = Clamp(happiness, 0f, 1f);
            float eScore = Clamp(employmentRate, 0f, 1f);
            float gScore = Clamp((populationGrowth + 0.05f) / 0.10f, 0f, 1f);

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

            return Clamp(basePr + penaltyFactor + volFactor, 0f, 1f);
        }

        public static float CalculateBondAppeal(
            float couponRate, float benchmarkRate, float defaultProbability)
        {
            float spread = couponRate - benchmarkRate;
            float spreadScore = Clamp(0.5f + spread * 10f, 0f, 1f);
            float riskMultiplier = Math.Max(1f - defaultProbability * 0.8f, 0.1f);

            return spreadScore * riskMultiplier;
        }

        public static float CalculateMomentumMultiplier(
            MarketState current, MarketState previous, float sensitivity)
        {
            float deltaF = current.FinancialHealth - previous.FinancialHealth;
            float deltaC = current.CitizenConfidence - previous.CitizenConfidence;
            float deltaA = current.BondAppeal - previous.BondAppeal;

            float rawMomentum = (deltaF + deltaC + deltaA) * sensitivity;

            if (rawMomentum < 0f)
            {
                rawMomentum *= 0.6f;
            }

            return Clamp(1.0f + rawMomentum, 0.5f, 1.5f);
        }

        public static float CalculateDemandScore(
            MarketState current, MarketState previous)
        {
            float baseDemand = (W_FINANCIAL * current.FinancialHealth)
                             + (W_CONFIDENCE * current.CitizenConfidence)
                             + (W_APPEAL * current.BondAppeal);

            float momentum = CalculateMomentumMultiplier(current, previous, 1.5f);
            float raw = baseDemand * momentum;

            float vitalFloor = current.CityVitals * 0.15f;

            return Clamp(Math.Max(raw, vitalFloor), 0f, 1f);
        }

        public static float AdjustYieldForDemand(float baseYield, float demandScore)
        {
            float multiplier = Clamp(1f + (0.5f - demandScore) * 0.70f, 0.85f, 1.20f);
            return baseYield * multiplier;
        }

        public static float CalculateAbsorptionCapacity(
            int population, float cashReserves, float demandScore)
        {
            float popCapacity = population * WEALTH_PER_CAPITA;
            float cashFactor = Clamp(1f + (float)Math.Log10(Math.Max(cashReserves, 1000f)) / 5f, 0.5f, 3f);
            float demandMultiplier = 0.1f + demandScore * 0.9f;
            return popCapacity * cashFactor * demandMultiplier;
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
            float activePopulation = Math.Max(population * participationRate, 1f);

            float buyBias = Clamp(0.5f + (bondAppeal - 0.5f) * 0.6f - defaultProbability * 0.3f, 0.1f, 0.9f);

            float noise = (float)(rng.NextDouble() * 0.2 - 0.1);
            float adjustedBias = Clamp(buyBias + noise, 0.05f, 0.95f);

            buyVolume = activePopulation * adjustedBias * TRADE_PER_CITIZEN;
            sellVolume = activePopulation * (1f - adjustedBias) * TRADE_PER_CITIZEN;
        }

        public static float CalculateMarketPressure(float buyVolume, float sellVolume)
        {
            float total = buyVolume + sellVolume;
            if (total <= 0f) return 0f;
            return Clamp((buyVolume - sellVolume) / total, -1f, 1f);
        }

        public static float AdjustYieldForPressure(float baseYield, float pressure)
        {
            float multiplier = Clamp(1f + pressure * 0.15f, 0.90f, 1.15f);
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
