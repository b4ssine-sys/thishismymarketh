using System;

namespace MyFirstMod
{
    public enum CreditRating { AAA, AA, A, BBB, BB, B, CCC, D }

    // Schema v5 lifecycle (audit spec §3). A bond is never deleted on a missed
    // payment; it transitions. Redeemed is terminal and retained for history.
    public enum BondState { Active, Delinquent, Defaulted, Redeemed }

    // Phase 6 (WO-11): revenue bonds are backed by a specific city service's
    // income stream. None = general-obligation bond (backed by the city's full
    // taxing power). A revenue source ties the bond's credit to that service.
    public enum RevenueSource { None, Water, Electricity, PublicTransport }

    public struct PayDebtResult
    {
        public int Retired;
        public bool PartialPaydown;
        public float AmountSpent;
    }

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
        public float InterestPaid;
        public float PrincipalRepaid;

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

        // P0-2: a default keeps the liability instead of erasing it. A missed
        // coupon or maturity payment rolls into Arrears, which accrue a penalty
        // each period until cleared.
        public float Arrears;

        // Schema v5 lifecycle (audit spec §3).
        public BondState State;
        public int PeriodsInArrears;   // consecutive periods carrying arrears
        public int DefaultedAtPeriod;  // period the bond entered Defaulted, else -1
        public int IssuePeriod;        // period the bond was issued (for age/history)

        // Phase 5 (P1-2): market/portfolio bonds carry the issuer that stands behind
        // them, so they price off that issuer's credit - not the city's. Empty /
        // default-A for the city's own issued bonds and legacy holdings.
        public string IssuerName;
        public CreditRating IssuerRating;

        // Phase 6 (WO-11): the city service whose revenue backs this bond.
        // None for general-obligation bonds.
        public RevenueSource Revenue;

        // Debt service, capacity and repayment all key off the amount still
        // owed, so SubscribedFace now reports OutstandingPrincipal.
        public float SubscribedFace { get { return OutstandingPrincipal; } }

        // Convenience views of the lifecycle for UI / reporting.
        public bool IsDelinquent { get { return State == BondState.Delinquent; } }
        public bool IsDefaulted { get { return State == BondState.Defaulted; } }
        public bool IsDistressed { get { return State == BondState.Delinquent || State == BondState.Defaulted; } }
        // Kept for existing call sites / UI: "in default" == the hard Defaulted state.
        public bool InDefault { get { return State == BondState.Defaulted; } }

        // Audit spec §2: one base for coupon. Unplaced notional is the primary
        // inventory still available for take-up.
        public float PeriodCoupon(int periodsPerYear)
        {
            return (OutstandingPrincipal * CouponRate) / periodsPerYear;
        }
        public float UnplacedNotional { get { return FaceValue * (1f - PlacedFraction); } }

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
            InterestPaid = 0f;
            PrincipalRepaid = 0f;
            PlacedFraction = 1f;
            OutstandingPrincipal = faceValue;
            Arrears = 0f;
            State = BondState.Active;
            PeriodsInArrears = 0;
            DefaultedAtPeriod = -1;
            IssuePeriod = 0;
            IssuerName = "";
            IssuerRating = CreditRating.A;
            Revenue = RevenueSource.None;
        }
    }

    // P0-8: immutable snapshots handed to the UI thread. The engine used to pass
    // live Bond / InterestRateSwap references that the sim thread kept mutating;
    // these DTOs are deep-copied under the lock so the UI reads a stable picture,
    // and every UI action is keyed by the stable Id, never a list index.
    public class BondView
    {
        public string Id;
        public string Name;
        public float FaceValue;
        public float CouponRate;
        public int TotalPeriods;
        public int RemainingPeriods;
        public float PurchasePrice;
        public float CouponsReceived;
        public float InterestPaid;
        public float PrincipalRepaid;
        public float PlacedFraction;
        public float OutstandingPrincipal;
        public float Arrears;
        public BondState State;
        public bool InDefault;
        public string IssuerName;
        public CreditRating IssuerRating;
        public RevenueSource Revenue;
        public float Price;

        public float SubscribedFace { get { return OutstandingPrincipal; } }

        public static BondView From(Bond b, float price)
        {
            return new BondView
            {
                Id = b.Id,
                Name = b.Name,
                FaceValue = b.FaceValue,
                CouponRate = b.CouponRate,
                TotalPeriods = b.TotalPeriods,
                RemainingPeriods = b.RemainingPeriods,
                PurchasePrice = b.PurchasePrice,
                CouponsReceived = b.CouponsReceived,
                InterestPaid = b.InterestPaid,
                PrincipalRepaid = b.PrincipalRepaid,
                PlacedFraction = b.PlacedFraction,
                OutstandingPrincipal = b.OutstandingPrincipal,
                Arrears = b.Arrears,
                State = b.State,
                InDefault = b.InDefault,
                IssuerName = b.IssuerName,
                IssuerRating = b.IssuerRating,
                Revenue = b.Revenue,
                Price = price
            };
        }
    }

    public class SwapView
    {
        public string Id;
        public float NotionalAmount;
        public float FixedRate;
        public int TotalPeriods;
        public int RemainingPeriods;
        public bool PayFixed;
        public float CumulativePL;
        public float LastSettlement;
        public float UnpaidSettlement;

        public static SwapView From(InterestRateSwap s)
        {
            return new SwapView
            {
                Id = s.Id,
                NotionalAmount = s.NotionalAmount,
                FixedRate = s.FixedRate,
                TotalPeriods = s.TotalPeriods,
                RemainingPeriods = s.RemainingPeriods,
                PayFixed = s.PayFixed,
                CumulativePL = s.CumulativePL,
                LastSettlement = s.LastSettlement,
                UnpaidSettlement = s.UnpaidSettlement
            };
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
        public float UnpaidSettlement;

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

    public class EngineSnapshot
    {
        public float GrossIncome;
        public float TotalExpenses;
        public float DebtBurden;
        public float DSCR;
        public float MonthsOfReserves;
        public float NOI;
        public CreditRating Rating;
        public float BenchmarkRate;
        public float RequiredYield;
        public float PortfolioValue;
        public int DefaultPenalty;
        public int TotalDefaults;
        public float RealizedPL;
        public int TicksInCurrentPeriod;
        public int IssuedCount;
        public int PortfolioCount;
        public int MarketCount;
        public float RevenueVolatility;
        public float SwapPL;
        public int SwapCount;
        public float DemandScore;
        public float DefaultProbability;
        public float AbsorptionCapacity;
        public float RemainingCapacity;
        public int Population;
        public float Happiness;
        public float EmploymentRate;
        public float PopulationGrowth;
        public float CitizenConfidence;
        public float BondAppeal;
        public float FinancialHealth;
        public float CitizenBuyVolume;
        public float CitizenSellVolume;
        public float SmoothedPressure;
        public float CitizenProceedsThisPeriod;
        public float TotalCitizenProceeds;
        public float Health;
        public float Education;
        public float LandValue;
        public float CrimeRate;
        public float CashReserves;
        public float CityVitals;
        public float Momentum;
        public int TransactionLogCount;
        public int ReportCount;
        public int CurrentQuarter;
        public float TotalDebtFace;
        public float TotalDebtOwed;
        public float TotalCouponsPaid;
        public float TotalHedgedNotional;
        public float OverHedgeRatio;
        public string CreditStatusLabel;
        public string DemandLabelText;
        public string PressureLabelText;
    }

    public static class BondPricing
    {
        public const int PeriodsPerYear = 12;

        // P2-1: closed-form annuity + discounted principal, O(1) instead of the old
        // O(n) discounting loop that ran for every portfolio bond every tick.
        //   d  = (1 + r)^-n
        //   PV = C * (1 - d) / r  +  F * d
        public static float PresentValue(Bond bond, float annualYield)
        {
            if (bond.RemainingPeriods <= 0)
                return bond.OutstandingPrincipal;

            float r = annualYield / PeriodsPerYear;
            float coupon = (bond.OutstandingPrincipal * bond.CouponRate) / PeriodsPerYear;

            if (r <= 0f)
                return coupon * bond.RemainingPeriods + bond.OutstandingPrincipal;

            double d = Math.Pow(1.0 + r, -bond.RemainingPeriods);
            double pv = coupon * (1.0 - d) / r + bond.OutstandingPrincipal * d;
            return (float)pv;
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

        public static string RevenueLabel(RevenueSource src)
        {
            switch (src)
            {
                case RevenueSource.Water:           return "Water";
                case RevenueSource.Electricity:     return "Electric";
                case RevenueSource.PublicTransport:  return "Transit";
                default: return "GO";
            }
        }
    }
}
