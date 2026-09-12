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

        // P0-3: one field used to carry two unrelated meanings (how much of an
        // issue investors had taken up, AND how much principal was still owed),
        // which let a partial paydown look like unsold inventory and created an
        // infinite-money loop. They are now split:
        //
        //   PlacedFraction      - primary-market take-up [0,1]. Only ever moved
        //                         up by citizen trading as the issue is placed.
        //   OutstandingPrincipal - amortising balance in currency units. Only
        //                         ever moved down by repayment (and up by
        //                         placement as the city receives proceeds).
        //
        // For bonds the city BUYS (market / portfolio) these issuer-side fields
        // are irrelevant and default to "fully placed, full principal".
        public float PlacedFraction;
        public float OutstandingPrincipal;

        // Debt service, capacity and repayment all key off the amount still
        // owed, so SubscribedFace now reports OutstandingPrincipal.
        public float SubscribedFace { get { return OutstandingPrincipal; } }

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
            PlacedFraction = 1f;
            OutstandingPrincipal = faceValue;
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
        public float Happiness;
        public float EmploymentRate;
        public float PopulationGrowth;
        public float CitizenConfidence;
        public float BondAppeal;
        public float FinancialHealth;
        public float CitizenProceeds;
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
                case CreditRating.AAA: spread = 0.0020f; break;
                case CreditRating.AA:  spread = 0.0045f; break;
                case CreditRating.A:   spread = 0.0090f; break;
                case CreditRating.BBB: spread = 0.0160f; break;
                case CreditRating.BB:  spread = 0.0275f; break;
                case CreditRating.B:   spread = 0.0450f; break;
                case CreditRating.CCC: spread = 0.0800f; break;
                case CreditRating.D:   spread = 0.1500f; break;
                default: spread = 0.0200f; break;
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
