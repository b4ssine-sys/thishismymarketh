using System;

namespace MyFirstMod
{
    public struct MarketState
    {
        public float CityVitals;
        public float FiscalStrength;
        public float CitizenConfidence;
        public float BondAppeal;
    }

    public static class CimDemandEngine
    {
        private const float W_CITY_VITALS = 0.40f;
        private const float W_FISCAL = 0.20f;
        private const float W_CONFIDENCE = 0.20f;
        private const float W_APPEAL = 0.20f;
        private const float WEALTH_PER_CAPITA = 500f;
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
            float popScore = (float)Math.Log10(Math.Max(population, 10)) / 5f;
            if (popScore < 0f) popScore = 0f;
            if (popScore > 1f) popScore = 1f;

            float safetyScore = 1f - crimeRate;
            if (safetyScore < 0f) safetyScore = 0f;
            if (safetyScore > 1f) safetyScore = 1f;

            if (happiness < 0f) happiness = 0f;
            if (happiness > 1f) happiness = 1f;
            if (health < 0f) health = 0f;
            if (health > 1f) health = 1f;
            if (education < 0f) education = 0f;
            if (education > 1f) education = 1f;
            if (landValue < 0f) landValue = 0f;
            if (landValue > 1f) landValue = 1f;

            return popScore * 0.15f
                 + happiness * 0.25f
                 + health * 0.20f
                 + education * 0.15f
                 + landValue * 0.10f
                 + safetyScore * 0.15f;
        }

        public static float CalculateFiscalStrength(
            float cashReserves, float debtBurden, float dscr, CreditRating rating)
        {
            float ratingScore = RatingToScore(rating);

            float dscrScore = dscr / 3.0f;
            if (dscrScore < 0f) dscrScore = 0f;
            if (dscrScore > 1f) dscrScore = 1f;

            float burdenScore = 1f - debtBurden;
            if (burdenScore < 0f) burdenScore = 0f;
            if (burdenScore > 1f) burdenScore = 1f;

            float cashScore = (float)Math.Log10(Math.Max(cashReserves, 1f)) / 8f;
            if (cashScore < 0f) cashScore = 0f;
            if (cashScore > 1f) cashScore = 1f;

            return ratingScore * 0.20f + dscrScore * 0.15f + burdenScore * 0.30f + cashScore * 0.35f;
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

        public static float CalculateMomentumMultiplier(
            MarketState current, MarketState previous, float sensitivity)
        {
            float deltaV = current.CityVitals - previous.CityVitals;
            float deltaF = current.FiscalStrength - previous.FiscalStrength;
            float deltaC = current.CitizenConfidence - previous.CitizenConfidence;
            float deltaA = current.BondAppeal - previous.BondAppeal;

            float rawMomentum = (deltaV + deltaF + deltaC + deltaA) * sensitivity;

            // Asymmetric damping: downward momentum is harder to sustain than upward
            // This prevents runaway death spirals while still allowing recovery rallies
            if (rawMomentum < 0f)
                rawMomentum *= 0.6f;

            float result = 1.0f + rawMomentum;
            if (result < 0.5f) result = 0.5f;
            if (result > 1.5f) result = 1.5f;
            return result;
        }

        public static float CalculateDemandScore(
            MarketState current, MarketState previous)
        {
            float baseDemand = W_CITY_VITALS * current.CityVitals
                             + W_FISCAL * current.FiscalStrength
                             + W_CONFIDENCE * current.CitizenConfidence
                             + W_APPEAL * current.BondAppeal;

            float momentum = CalculateMomentumMultiplier(current, previous, 1.5f);

            float raw = baseDemand * momentum;

            // Anti-spiral floor: even in worst conditions, a city with real population
            // and game vitals retains some base demand from city vitals alone.
            // This prevents the financial feedback loop from zeroing out demand
            // when the city itself is still functional.
            float vitalFloor = current.CityVitals * 0.15f;
            if (raw < vitalFloor) raw = vitalFloor;

            if (raw < 0f) raw = 0f;
            if (raw > 1f) raw = 1f;
            return raw;
        }

        public static float AdjustYieldForDemand(float baseYield, float demandScore)
        {
            float multiplier = 1f + (0.5f - demandScore) * 0.70f;
            if (multiplier < 0.85f) multiplier = 0.85f;
            if (multiplier > 1.20f) multiplier = 1.20f;
            return baseYield * multiplier;
        }

        public static float CalculateAbsorptionCapacity(
            int population, float cashReserves, float demandScore)
        {
            float popCapacity = population * WEALTH_PER_CAPITA;

            float cashFactor = 1f + (float)Math.Log10(Math.Max(cashReserves, 1000f)) / 5f;
            if (cashFactor < 0.5f) cashFactor = 0.5f;
            if (cashFactor > 3f) cashFactor = 3f;

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

            buyVolume = activePopulation * adjustedBias * WEALTH_PER_CAPITA;
            sellVolume = activePopulation * (1f - adjustedBias) * WEALTH_PER_CAPITA;
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
            float multiplier = 1f + pressure * 0.15f;
            if (multiplier < 0.90f) multiplier = 0.90f;
            if (multiplier > 1.15f) multiplier = 1.15f;
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
