using System;

namespace MyFirstMod
{
    // RC-1 (WO-17/WO-18): credit metrics normalized to annual run-rates.
    // Revenue/expense arrive as per-PERIOD averages (the sampler's natural
    // cadence) and are annualized by periodsPerYear alone. The old API took
    // per-tick averages and multiplied by ticksPerPeriod × periodsPerYear,
    // which was 15× wrong once the sampler moved to once-per-period.
    //
    // Pure and dependency-free (only DebtBook, which is itself pure), so the
    // whole measurement core is unit-tested on CI.
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
        public static CreditMetrics CalculateMetrics(
            float avgIncomePerPeriod,
            float avgExpensePerPeriod,
            DebtBook debtBook,
            float currentCashReserves,
            int periodsPerYear)
        {
            float annualDebtService = 0f;
            if (debtBook != null)
            {
                for (int i = 0; i < debtBook.Bonds.Count; i++)
                {
                    Bond b = debtBook.Bonds[i];
                    if (b.State == BondState.Active || b.State == BondState.Delinquent)
                        annualDebtService += b.OutstandingPrincipal * b.CouponRate;
                }
            }
            return FromAnnual(avgIncomePerPeriod * periodsPerYear, avgExpensePerPeriod * periodsPerYear,
                annualDebtService, currentCashReserves, periodsPerYear);
        }

        // The ratios from annual figures. Shared by the engine and by what-if
        // previews (WO-35, WO-36) so a preview can never disagree with the model.
        public static CreditMetrics FromAnnual(float annualRevenue, float annualExpense,
            float annualDebtService, float cashReserves, int periodsPerYear)
        {
            var m = new CreditMetrics();
            m.AnnualOperatingRevenue = annualRevenue;
            m.AnnualOperatingExpense = annualExpense;
            m.AnnualNOI = annualRevenue - annualExpense;
            m.AnnualDebtService = annualDebtService;

            if (m.AnnualDebtService <= 1f)
            {
                // WO-18: unlevered — coverage is undefined when there is nothing
                // to cover. Surplus gets strong-but-finite DSCR; deficit or cold
                // start gets 1.0 (solvent, no leverage) and the liquidity notch
                // carries the distinction.
                m.DebtBurden = 0f;
                m.DSCR = m.AnnualNOI > 0f ? 20f : 1f;
            }
            else
            {
                m.DebtBurden = m.AnnualOperatingRevenue > 0f
                    ? (m.AnnualDebtService / m.AnnualOperatingRevenue)
                    : 1.0f;
                m.DSCR = m.AnnualNOI / m.AnnualDebtService;
            }

            float monthlyExpense = m.AnnualOperatingExpense / periodsPerYear;
            m.MonthsOfReserves = monthlyExpense > 100f
                ? (cashReserves / monthlyExpense)
                : 12f;

            return m;
        }
    }
}
