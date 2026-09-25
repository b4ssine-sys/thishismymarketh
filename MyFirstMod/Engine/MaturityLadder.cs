namespace MyFirstMod
{
    public struct LadderMonth
    {
        public int Offset;           // months from now, 1-based
        public float Coupon;
        public float Principal;
        public float Arrears;        // carried arrears fall due in the first month
        public float Due;
        public float ProjectedCash;  // treasury after this month's flows and payments
        public bool Shortfall;       // projected cash cannot cover what is due
        public bool Imminent;        // a shortfall within the warning lead (amber)
    }

    // WO-37: the next months of coupon and principal due on the city's debt, with
    // the treasury projected forward on the current operating flow. A month whose
    // projected cash falls short is flagged; within the warning lead it is amber,
    // so the player sees a shortfall at least that many periods before it lands.
    public static class MaturityLadder
    {
        public const int DefaultMonths = 36;
        public const int WarningLead = 2;

        public static LadderMonth[] Build(BondView[] issued, float cash, float netOperatingPerMonth,
            int months, int warningLead, int periodsPerYear)
        {
            var ladder = new LadderMonth[months];
            float running = cash;
            for (int k = 0; k < months; k++)
            {
                int offset = k + 1;
                LadderMonth m = new LadderMonth();
                m.Offset = offset;

                for (int i = 0; i < issued.Length; i++)
                {
                    BondView b = issued[i];
                    if (b.State == BondState.Redeemed) continue;
                    if (offset == 1) m.Arrears += b.Arrears;
                    if (offset > b.RemainingPeriods) continue;
                    m.Coupon += b.OutstandingPrincipal * b.CouponRate / periodsPerYear;
                    if (offset == b.RemainingPeriods) m.Principal += b.OutstandingPrincipal;
                }

                m.Due = m.Coupon + m.Principal + m.Arrears;
                float available = running + netOperatingPerMonth;
                m.Shortfall = m.Due > 0.01f && available < m.Due;
                running = available - m.Due;
                m.ProjectedCash = running;
                m.Imminent = m.Shortfall && offset <= warningLead;
                ladder[k] = m;
            }
            return ladder;
        }

        // Offset of the first projected shortfall, or 0 if none.
        public static int FirstShortfall(LadderMonth[] ladder)
        {
            for (int i = 0; i < ladder.Length; i++)
                if (ladder[i].Shortfall) return ladder[i].Offset;
            return 0;
        }
    }
}
