using System;

namespace MyFirstMod
{
    // Phase 2: the calibrated municipal rating grid, evaluated against the
    // normalized CreditMetrics and adjusted one notch for treasury liquidity.
    // Replaces the old ad-hoc thresholds and the cash-balance DSCR overrides.
    // Pure and unit-tested.
    public static class RatingEngine
    {
        public static CreditRating EvaluateRating(CreditMetrics m, bool hasActiveArrears)
        {
            // 1. Hard default floor: unresolved arrears or coverage collapse.
            if (hasActiveArrears || m.DSCR < 0.2f) return CreditRating.D;

            // 2. Base rating from the core credit ratios.
            // 0=AAA 1=AA 2=A 3=BBB 4=BB 5=B 6=CCC 7=D
            int baseNotch;
            if      (m.DSCR >= 2.50f && m.DebtBurden <= 0.08f) baseNotch = 0; // AAA
            else if (m.DSCR >= 2.00f && m.DebtBurden <= 0.12f) baseNotch = 1; // AA
            else if (m.DSCR >= 1.50f && m.DebtBurden <= 0.18f) baseNotch = 2; // A
            else if (m.DSCR >= 1.25f && m.DebtBurden <= 0.25f) baseNotch = 3; // BBB
            else if (m.DSCR >= 1.05f && m.DebtBurden <= 0.32f) baseNotch = 4; // BB
            else if (m.DSCR >= 0.90f && m.DebtBurden <= 0.40f) baseNotch = 5; // B
            else                                               baseNotch = 6; // CCC

            // 3. Liquidity notch: strong cash cushion upgrades one notch, a
            // depleted treasury downgrades one.
            if (m.MonthsOfReserves >= 6.0f && baseNotch > 0)
                baseNotch -= 1;
            else if (m.MonthsOfReserves < 1.0f && baseNotch < 6)
                baseNotch += 1;

            // Base/notch path tops out at CCC; D is reserved for the hard floor.
            if (baseNotch > (int)CreditRating.CCC) baseNotch = (int)CreditRating.CCC;
            if (baseNotch < 0) baseNotch = 0;
            return (CreditRating)baseNotch;
        }
    }
}
