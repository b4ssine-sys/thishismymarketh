using System;

namespace MyFirstMod
{
    // Phase 5 (P1-8): secondary-market friction so round-tripping isn't free and
    // large orders move the price. Half-spread scales with issuer risk and duration
    // (C-2); square-root price impact against the issue's own depth (C-3); depth is
    // a fraction of outstanding face per period (C-4). Plus the city's own
    // underwriting fee on issuance (C-1). Pure.
    public static class Friction
    {
        public const float UnderwritingFeeRate = 0.0075f; // C-1: 75bp of par
        public const float ImpactK = 50f;                 // C-3: bp at full-depth order
        public const float DepthFraction = 0.20f;         // C-4: 20% of outstanding face / period

        // C-1: underwriting fee deducted from issuance proceeds.
        public static float UnderwritingFee(float par)
        {
            return par * UnderwritingFeeRate;
        }

        // C-2: base half-spread (as a fraction of price) by issuer rating.
        public static float BaseHalfSpread(CreditRating rating)
        {
            switch (rating)
            {
                case CreditRating.AAA: return 0.0010f;
                case CreditRating.AA:  return 0.0015f;
                case CreditRating.A:   return 0.0025f;
                case CreditRating.BBB: return 0.0040f;
                case CreditRating.BB:  return 0.0080f;
                case CreditRating.B:   return 0.0150f;
                case CreditRating.CCC: return 0.0300f;
                default: return 0.0300f;
            }
        }

        // C-2: half-spread widened by duration.
        public static float HalfSpread(CreditRating rating, float durationYears)
        {
            float dur = durationYears < 0f ? 0f : durationYears;
            return BaseHalfSpread(rating) * (1f + dur / 10f);
        }

        // C-4: tradeable depth this period.
        public static float DepthPerPeriod(float outstandingFace)
        {
            return outstandingFace * DepthFraction;
        }

        // C-3: square-root price impact (fraction of price) for an order against depth.
        public static float ImpactFraction(float orderSize, float depth)
        {
            if (depth <= 1f || orderSize <= 0f) return 0f;
            float ratio = orderSize / depth;
            return (ImpactK / 10000f) * (float)Math.Sqrt(ratio);
        }

        // Execution price for a trade off a fair mid. Buys pay up (half-spread +
        // impact); sells receive less. Result is floored at zero.
        public static float ExecutionPrice(
            float mid, bool isBuy, CreditRating rating, float durationYears, float orderSize, float depth)
        {
            float cost = HalfSpread(rating, durationYears) + ImpactFraction(orderSize, depth);
            float px = isBuy ? mid * (1f + cost) : mid * (1f - cost);
            return px < 0f ? 0f : px;
        }
    }
}
