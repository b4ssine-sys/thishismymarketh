using System;

namespace MyFirstMod
{
    // Phase 2 (P1-1): the credit metrics, normalized to a single annualized
    // horizon. The old engine divided a per-PERIOD debt service by a per-TICK
    // revenue - a 15x distortion that made healthy cities read as near-default.
    // Everything here is expressed as an annual run-rate so DSCR and debt burden
    // match their textbook definitions and the rating grid's thresholds mean what
    // they say.
    //
    // Pure and dependency-free (only DebtBook, which is itself pure), so the whole
    // measurement core is unit-tested on CI. The tick/period cadence is passed in
    // rather than read from the Unity-coupled engine, keeping this testable.
    public struct CreditMetrics
    {
        public float AnnualOperatingRevenue;
        public float AnnualOperatingExpense;
        public float AnnualNOI;
        public float AnnualDebtService;
        public float DSCR;
        public float DebtBurden;
        public float MonthsOfReserves;
    }

    public static class CreditModel
    {
        // Coupon on an amortizing balance is already an annual rate, so annual debt
        // service is simply Σ OutstandingPrincipal * CouponRate over serviceable
        // bonds. Revenue/expense arrive as per-tick averages and are annualized by
        // ticksPerPeriod * periodsPerYear.
        public static CreditMetrics CalculateMetrics(
            float avgIncomePerTick,
            float avgExpensePerTick,
            DebtBook debtBook,
            float currentCashReserves,
            int ticksPerPeriod,
            int periodsPerYear)
        {
            var m = new CreditMetrics();

            int ticksPerYear = ticksPerPeriod * periodsPerYear;
            m.AnnualOperatingRevenue = avgIncomePerTick * ticksPerYear;
            m.AnnualOperatingExpense = avgExpensePerTick * ticksPerYear;
            m.AnnualNOI = m.AnnualOperatingRevenue - m.AnnualOperatingExpense;

            m.AnnualDebtService = 0f;
            if (debtBook != null)
            {
                for (int i = 0; i < debtBook.Bonds.Count; i++)
                {
                    Bond b = debtBook.Bonds[i];
                    if (b.State == BondState.Active || b.State == BondState.Delinquent)
                        m.AnnualDebtService += b.OutstandingPrincipal * b.CouponRate;
                }
            }

            if (m.AnnualDebtService <= 1f)
            {
                // Unlevered: no scheduled service. Strong-but-finite DSCR when the
                // city runs a surplus, zero otherwise.
                m.DebtBurden = 0f;
                m.DSCR = m.AnnualNOI > 0f ? 20f : 0f;
            }
            else
            {
                m.DebtBurden = m.AnnualOperatingRevenue > 0f
                    ? (m.AnnualDebtService / m.AnnualOperatingRevenue)
                    : 1.0f;
                m.DSCR = m.AnnualNOI / m.AnnualDebtService;
            }

            // Liquidity expressed analytically: how many months of operating
            // expense the treasury holds. Replaces the old raw cash-floor DSCR
            // fudge. A city with negligible expense is treated as fully liquid.
            float monthlyExpense = m.AnnualOperatingExpense / periodsPerYear;
            m.MonthsOfReserves = monthlyExpense > 100f
                ? (currentCashReserves / monthlyExpense)
                : 12f;

            return m;
        }
    }
}
