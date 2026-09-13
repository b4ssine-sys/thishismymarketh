using System;

namespace MyFirstMod
{
    // Phase 4 (P1-3): a three-factor Nelson-Siegel term structure. Gives a spot
    // rate for any maturity, so duration finally has a price consequence and the
    // curve can be inverted, steepened, etc. Pure and allocation-free.
    //
    //   y(t) = Level
    //        + Slope     * (1 - e^-x) / x
    //        + Curvature * ((1 - e^-x) / x - e^-x),   x = t / Lambda
    //
    // t -> 0  => y -> Level + Slope   (short rate)
    // t -> inf => y -> Level          (long level)
    public struct YieldCurve
    {
        public float Level;      // beta0: long-run level
        public float Slope;      // beta1: short-minus-long (short end = Level + Slope)
        public float Curvature;  // beta2: mid-curve hump/dip
        public float Lambda;     // decay in years (> 0)

        public float SpotRate(float tenorYears)
        {
            float lambda = Lambda > 0.0001f ? Lambda : 1f;
            if (tenorYears <= 0f)
                return Level + Slope; // short-end limit

            float x = tenorYears / lambda;
            float e = (float)Math.Exp(-x);
            float term = (1f - e) / x;
            return Level + Slope * term + Curvature * (term - e);
        }

        public float DiscountFactor(float tenorYears)
        {
            if (tenorYears <= 0f) return 1f;
            float y = SpotRate(tenorYears);
            return (float)Math.Exp(-y * tenorYears);
        }

        // Build a curve from an evolving short rate anchored to a long-run level.
        // Short end = shortRate, long end = longLevel.
        public static YieldCurve FromShortRate(float shortRate, float longLevel, float curvature, float lambda)
        {
            YieldCurve c;
            c.Level = longLevel;
            c.Slope = shortRate - longLevel;
            c.Curvature = curvature;
            c.Lambda = lambda > 0.0001f ? lambda : 1f;
            return c;
        }
    }
}
