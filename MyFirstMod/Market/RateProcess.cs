using System;

namespace MyFirstMod
{
    // Phase 4 (P1-4): a mean-reverting (Vasicek / Ornstein-Uhlenbeck) short-rate
    // process so the rate environment is exogenous and stochastic instead of a
    // hard-coded constant. theta itself drifts on a slow business cycle. Pure; the
    // random draw is supplied by the caller so it is deterministic under test.
    //
    //   dr = kappa * (theta - r) * dt + sigma * sqrt(dt) * Z
    public static class RateProcess
    {
        public const float RateFloor = 0.001f;

        // One discrete step. z is a standard-normal draw.
        public static float Step(float r, float kappa, float theta, float sigma, float dt, float z)
        {
            float dr = kappa * (theta - r) * dt + sigma * (float)Math.Sqrt(Math.Max(dt, 0f)) * z;
            float next = r + dr;
            if (next < RateFloor) next = RateFloor;
            if (next > 0.5f) next = 0.5f; // sanity ceiling
            return next;
        }

        // Long-run mean drifting on a business cycle of the given phase (radians).
        public static float CycleTheta(float baseTheta, float amplitude, float phase)
        {
            return baseTheta + amplitude * (float)Math.Sin(phase);
        }

        // Advance the cycle phase, wrapped into [0, 2pi).
        public static float AdvancePhase(float phase, float delta)
        {
            float p = phase + delta;
            float twoPi = (float)(2.0 * Math.PI);
            while (p >= twoPi) p -= twoPi;
            while (p < 0f) p += twoPi;
            return p;
        }

        // Standard-normal draw via Box-Muller.
        public static float NextGaussian(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
