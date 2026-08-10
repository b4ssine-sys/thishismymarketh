using System;

namespace MyFirstMod
{
    public enum CreditRating { AAA, AA, A, BBB, BB, B, CCC, D }

    public class Bond
    {
        public string Id;
        public string Name;
        public float FaceValue;
        public float CouponRate;
        public int TotalPeriods;
        public int RemainingPeriods;
        public float PurchasePrice;
        public float CouponsReceived;
        public float SoldFraction;

        public float SubscribedFace { get { return FaceValue * SoldFraction; } }

        public Bond(string id, string name, float faceValue, float couponRate, int totalPeriods)
        {
            Id = id;
            Name = name;
            FaceValue = faceValue;
            CouponRate = couponRate;
            TotalPeriods = totalPeriods;
            RemainingPeriods = totalPeriods;
            PurchasePrice = 0f;
            CouponsReceived = 0f;
            SoldFraction = 1f;
        }
    }

    public class InterestRateSwap
    {
        public string Id;
        public float NotionalAmount;
        public float FixedRate;
        public int TotalPeriods;
        public int RemainingPeriods;
        public bool PayFixed;
        public float CumulativePL;
        public float LastSettlement;

        public InterestRateSwap(string id, float notional, float fixedRate, int totalPeriods, bool payFixed)
        {
            Id = id;
            NotionalAmount = notional;
            FixedRate = fixedRate;
            TotalPeriods = totalPeriods;
            RemainingPeriods = totalPeriods;
            PayFixed = payFixed;
            CumulativePL = 0f;
            LastSettlement = 0f;
        }
    }

    public class CimTransaction
    {
        public int Sequence;
        public float BuyVolume;
        public float SellVolume;
        public float Pressure;
        public string Detail;
    }

    public class QuarterlyReport
    {
        public int Quarter;
        public CreditRating Rating;
        public string CreditStatus;
        public float DSCR;
        public float DebtBurden;
        public float GrossIncome;
        public float TotalExpenses;
        public float NOI;
        public float DefaultProbability;
        public int IssuedBonds;
        public int MaxBonds;
        public float DebtFace;
        public float DebtOwed;
        public float AvgSubscription;
        public float CouponsPaid;
        public int QuarterDefaults;
        public int TotalDefaults;
        public float BenchmarkRate;
        public float RequiredYield;
        public float DemandScore;
        public float SmoothedPressure;
        public float AbsorptionCapacity;
        public int Population;
        public int PortfolioBonds;
        public int SwapCount;
        public float HedgedNotional;
        public float RealizedPL;
        public float SwapPL;
        public float RevenueVolatility;
        public string Outlook;
    }

    public static class BondPricing
    {
        public const int PeriodsPerYear = 12;

        public static float PresentValue(Bond bond, float annualYield)
        {
            if (bond.RemainingPeriods <= 0)
                return bond.FaceValue;

            float r = annualYield / PeriodsPerYear;
            float coupon = (bond.FaceValue * bond.CouponRate) / PeriodsPerYear;

            float pvCoupons = 0f;
            float discount = 1f;
            for (int t = 0; t < bond.RemainingPeriods; t++)
            {
                discount *= (1f + r);
                pvCoupons += coupon / discount;
            }

            float pvPrincipal = bond.FaceValue / discount;
            return pvCoupons + pvPrincipal;
        }

        public static float GetRequiredYield(float benchmarkRate, CreditRating rating)
        {
            float spread;
            switch (rating)
            {
                case CreditRating.AAA: spread = 0.005f; break;
                case CreditRating.AA:  spread = 0.012f; break;
                case CreditRating.A:   spread = 0.022f; break;
                case CreditRating.BBB: spread = 0.038f; break;
                case CreditRating.BB:  spread = 0.060f; break;
                case CreditRating.B:   spread = 0.090f; break;
                case CreditRating.CCC: spread = 0.140f; break;
                case CreditRating.D:   spread = 0.300f; break;
                default: spread = 0.050f; break;
            }
            return benchmarkRate + spread;
        }

        public static CreditRating CalculateRating(float debtBurden, float dscr)
        {
            if (debtBurden < 0.05f && dscr > 3.0f) return CreditRating.AAA;
            if (debtBurden < 0.10f && dscr > 2.0f) return CreditRating.AA;
            if (debtBurden < 0.15f && dscr > 1.5f) return CreditRating.A;
            if (debtBurden < 0.25f && dscr > 1.2f) return CreditRating.BBB;
            if (debtBurden < 0.35f && dscr > 0.9f) return CreditRating.BB;
            if (dscr > 0.8f) return CreditRating.B;
            if (dscr > 0.5f) return CreditRating.CCC;
            return CreditRating.D;
        }

        public static string RatingLabel(CreditRating rating)
        {
            switch (rating)
            {
                case CreditRating.AAA: return "AAA";
                case CreditRating.AA:  return "AA";
                case CreditRating.A:   return "A";
                case CreditRating.BBB: return "BBB";
                case CreditRating.BB:  return "BB";
                case CreditRating.B:   return "B";
                case CreditRating.CCC: return "CCC";
                case CreditRating.D:   return "D";
                default: return "?";
            }
        }
    }
}
