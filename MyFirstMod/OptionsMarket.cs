using System;
using System.Collections.Generic;

// Pure domain logic for a secondary options market layered on the game's
// investment prices. Nothing here references the game, so it compiles and
// runs on its own — you can unit-test the pricing in a console project first.
namespace MyFirstMod.Options
{
    public enum OptionKind { Call, Put }

    // The "product": a contract definition with no ownership data.
    public struct OptionContract
    {
        public string UnderlyingId;   // which investment category it's written on
        public OptionKind Kind;
        public float Strike;          // exercise price
        public int ExpiryDay;         // in-game day index on which it cash-settles

        public OptionContract(string underlyingId, OptionKind kind, float strike, int expiryDay)
        {
            UnderlyingId = underlyingId;
            Kind = kind;
            Strike = strike;
            ExpiryDay = expiryDay;
        }

        // Payoff per single long contract if settled now at the given spot price.
        public float Intrinsic(float spot)
        {
            float raw = Kind == OptionKind.Call ? spot - Strike : Strike - spot;
            return Math.Max(0f, raw);
        }
    }

    // A player holding. Positive quantity = bought (long), negative = written (short).
    public class OptionPosition
    {
        public OptionContract Contract;
        public int Quantity;
        public float PremiumPaid;     // total cash paid at open (negative = received, if written)

        public OptionPosition(OptionContract contract, int quantity, float premiumPaid)
        {
            Contract = contract;
            Quantity = quantity;
            PremiumPaid = premiumPaid;
        }

        // Cash the city receives at expiry. Negative if short and in-the-money.
        public float Settlement(float spotAtExpiry)
        {
            return Contract.Intrinsic(spotAtExpiry) * Quantity;
        }
    }

    // Black-Scholes pricing behind one entry point, so the model is swappable.
    public static class OptionPricing
    {
        public const float DaysPerYear = 365f;

        // Premium per single contract. Days are in-game day indices.
        public static float Premium(OptionContract c, float spot, float volatility,
                                    float riskFreeRate, int currentDay)
        {
            int daysLeft = Math.Max(0, c.ExpiryDay - currentDay);
            if (daysLeft == 0)
                return c.Intrinsic(spot);

            double t = daysLeft / DaysPerYear;
            double s = Math.Max(0.01f, spot);
            double k = Math.Max(0.01f, c.Strike);
            double vol = Math.Max(0.01f, volatility);

            double d1 = (Math.Log(s / k) + (riskFreeRate + 0.5 * vol * vol) * t) / (vol * Math.Sqrt(t));
            double d2 = d1 - vol * Math.Sqrt(t);
            double discountedK = k * Math.Exp(-riskFreeRate * t);

            double price = c.Kind == OptionKind.Call
                ? s * Cnd(d1) - discountedK * Cnd(d2)
                : discountedK * Cnd(-d2) - s * Cnd(-d1);

            return (float)Math.Max(0.0, price);
        }

        // Cumulative normal distribution (Abramowitz & Stegun 7.1.26).
        private static double Cnd(double x)
        {
            double sign = x < 0 ? -1.0 : 1.0;
            x = Math.Abs(x) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t
                        - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return 0.5 * (1.0 + sign * y);
        }
    }

    // Builds the small chain of contracts offered for one underlying.
    public static class ChainBuilder
    {
        public static List<OptionContract> Build(string underlyingId, float spot, int currentDay,
                                                 int[] expiriesInDays, int strikesEachSide,
                                                 float strikeStepPct)
        {
            var chain = new List<OptionContract>();
            foreach (int d in expiriesInDays)
            {
                int expiry = currentDay + d;
                for (int i = -strikesEachSide; i <= strikesEachSide; i++)
                {
                    float strike = (float)Math.Round(spot * (1f + i * strikeStepPct), 2);
                    chain.Add(new OptionContract(underlyingId, OptionKind.Call, strike, expiry));
                    chain.Add(new OptionContract(underlyingId, OptionKind.Put, strike, expiry));
                }
            }
            return chain;
        }
    }
}
