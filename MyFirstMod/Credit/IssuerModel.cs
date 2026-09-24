using System;

namespace MyFirstMod
{
    // Phase 5 (P1-2): market issuers have their own credit, independent of the
    // city's. A-1: static sector archetype + stochastic migration around a home
    // rating. A-2: annual one-notch Markov migration with mean reversion. A-3:
    // recovery-rate default (holder gets a fraction of par). A-4: default hazard
    // compressed by a documented multiplier so events land on a game timescale.
    // Pure and unit-tested.
    public enum IssuerArchetype
    {
        WaterDistrict, PowerGrid, TransitAuthority, PortAuthority, HealthSystem, SchoolBoard
    }

    public class MarketIssuer
    {
        public string Name;
        public IssuerArchetype Archetype;
        public CreditRating HomeRating;
        public CreditRating Rating;
        public bool Defaulted;
    }

    public static class IssuerModel
    {
        // Difficulty presets for the default-hazard multiplier (A-4). Exposed as a
        // settings control; Standard is the recommended default.
        public const float HAZARD_HISTORICAL = 1f;
        public const float HAZARD_STANDARD = 25f;
        public const float HAZARD_VOLATILE = 60f;

        // Credit spread over the benchmark by rating (bp as a fraction). Mirrors the
        // municipal grid used elsewhere.
        public static float IssuerSpread(CreditRating rating)
        {
            switch (rating)
            {
                case CreditRating.AAA: return 0.0020f;
                case CreditRating.AA:  return 0.0045f;
                case CreditRating.A:   return 0.0090f;
                case CreditRating.BBB: return 0.0160f;
                case CreditRating.BB:  return 0.0275f;
                case CreditRating.B:   return 0.0450f;
                case CreditRating.CCC: return 0.0800f;
                case CreditRating.D:   return 0.1500f;
                default: return 0.0200f;
            }
        }

        public static CreditRating HomeRatingFor(IssuerArchetype a)
        {
            switch (a)
            {
                case IssuerArchetype.WaterDistrict:    return CreditRating.AA;   // essential, rate-setting
                case IssuerArchetype.PowerGrid:        return CreditRating.A;
                case IssuerArchetype.SchoolBoard:      return CreditRating.AA;   // tax-backed, dull
                case IssuerArchetype.HealthSystem:     return CreditRating.A;
                case IssuerArchetype.TransitAuthority: return CreditRating.BBB;  // subsidy-dependent
                case IssuerArchetype.PortAuthority:    return CreditRating.BBB;  // trade-cyclical
                default: return CreditRating.A;
            }
        }

        // A-3: recovery fraction of par on default, by sector. Essential-service
        // pledges recover higher; discretionary ones lower.
        public static float RecoveryRateFor(IssuerArchetype a)
        {
            switch (a)
            {
                case IssuerArchetype.WaterDistrict: return 0.70f;
                case IssuerArchetype.PowerGrid:     return 0.70f;
                case IssuerArchetype.HealthSystem:  return 0.65f;
                case IssuerArchetype.SchoolBoard:   return 0.65f;
                case IssuerArchetype.TransitAuthority: return 0.50f;
                case IssuerArchetype.PortAuthority:    return 0.45f;
                default: return 0.60f;
            }
        }

        // Base annual default probability by rating (historical-ish). Scaled by the
        // hazard multiplier at use.
        public static float BaseAnnualDefaultProb(CreditRating rating)
        {
            switch (rating)
            {
                case CreditRating.AAA: return 0.00000f;
                case CreditRating.AA:  return 0.00010f;
                case CreditRating.A:   return 0.00020f;
                case CreditRating.BBB: return 0.00080f;
                case CreditRating.BB:  return 0.00800f;
                case CreditRating.B:   return 0.03000f;
                case CreditRating.CCC: return 0.10000f;
                default: return 0f;
            }
        }

        public static float DefaultHazard(CreditRating rating, float hazardMultiplier)
        {
            float p = BaseAnnualDefaultProb(rating) * hazardMultiplier;
            if (p < 0f) p = 0f;
            if (p > 0.9f) p = 0.9f;
            return p;
        }

        // A-2: one annual migration step. First test the (compressed) default
        // hazard; otherwise drift one notch with a mean-reversion bias toward home.
        // Returns the new rating and whether the issuer defaulted this step.
        public static CreditRating MigrateAnnual(
            CreditRating current, CreditRating home, float hazardMultiplier,
            Random rng, out bool defaulted)
        {
            defaulted = false;
            if (current == CreditRating.D)
            {
                defaulted = false; // already defaulted; caller handles resolution
                return CreditRating.D;
            }

            if (rng.NextDouble() < DefaultHazard(current, hazardMultiplier))
            {
                defaulted = true;
                return CreditRating.D;
            }

            int cur = (int)current;   // 0=AAA .. 7=D
            int hm = (int)home;

            // Mean reversion: pull toward home. Base per-step migration probability.
            float pDown = 0.08f;
            float pUp = 0.08f;
            if (cur < hm) pDown += 0.10f;      // below home (worse number is higher) -> nudge down toward home
            else if (cur > hm) pUp += 0.10f;   // above home number -> nudge up

            double roll = rng.NextDouble();
            if (roll < pUp)
                return cur > 0 ? (CreditRating)(cur - 1) : current;
            if (roll < pUp + pDown)
                return cur < (int)CreditRating.CCC ? (CreditRating)(cur + 1) : current;
            return current;
        }

        private static readonly IssuerArchetype[] _archetypes = new IssuerArchetype[]
        {
            IssuerArchetype.WaterDistrict, IssuerArchetype.PowerGrid, IssuerArchetype.TransitAuthority,
            IssuerArchetype.PortAuthority, IssuerArchetype.HealthSystem, IssuerArchetype.SchoolBoard
        };

        public static IssuerArchetype ArchetypeByIndex(int i)
        {
            return _archetypes[((i % _archetypes.Length) + _archetypes.Length) % _archetypes.Length];
        }

        public static int ArchetypeCount { get { return _archetypes.Length; } }

        public static MarketIssuer MakeIssuer(string name, IssuerArchetype a)
        {
            MarketIssuer m = new MarketIssuer();
            m.Name = name;
            m.Archetype = a;
            m.HomeRating = HomeRatingFor(a);
            m.Rating = m.HomeRating;
            m.Defaulted = false;
            return m;
        }
    }
}
