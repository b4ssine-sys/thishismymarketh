using System;

namespace MyFirstMod
{
    // Phase 4 (P1-6): interest-rate-swap valuation off the term structure, replacing
    // the old linear (floating - fixed) x notional x years approximation. Pure.
    //
    // Textbook single-curve valuation:
    //   Annuity A   = Sum_{k=1..n} DF(k/ppy) / ppy
    //   PV(float)   = notional * (1 - DF(n/ppy))     (par-resetting float leg)
    //   PV(fixed)   = notional * fixedRate * A
    //   pay-fixed value = PV(float) - PV(fixed)
    //   par swap rate   = (1 - DF(n/ppy)) / A
    public static class SwapPricing
    {
        public static float Annuity(YieldCurve curve, int remainingPeriods, int periodsPerYear)
        {
            if (remainingPeriods <= 0 || periodsPerYear <= 0) return 0f;
            float a = 0f;
            for (int k = 1; k <= remainingPeriods; k++)
            {
                float tau = (float)k / periodsPerYear;
                a += curve.DiscountFactor(tau);
            }
            return a / periodsPerYear;
        }

        public static float ParSwapRate(YieldCurve curve, int remainingPeriods, int periodsPerYear)
        {
            float a = Annuity(curve, remainingPeriods, periodsPerYear);
            if (a <= 0f) return 0f;
            float dfN = curve.DiscountFactor((float)remainingPeriods / periodsPerYear);
            return (1f - dfN) / a;
        }

        // Mark-to-market of a swap. Positive = in the money for the position holder.
        public static float SwapValue(YieldCurve curve, float notional, float fixedRate,
            int remainingPeriods, int periodsPerYear, bool payFixed)
        {
            if (remainingPeriods <= 0 || periodsPerYear <= 0) return 0f;
            float a = Annuity(curve, remainingPeriods, periodsPerYear);
            float dfN = curve.DiscountFactor((float)remainingPeriods / periodsPerYear);
            float pvFloat = notional * (1f - dfN);
            float pvFixed = notional * fixedRate * a;
            float payFixedValue = pvFloat - pvFixed;
            return payFixed ? payFixedValue : -payFixedValue;
        }
    }
}
