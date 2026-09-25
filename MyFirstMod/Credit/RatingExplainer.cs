using System;

namespace MyFirstMod
{
    // One factor's distance to a rating change. Delta is signed in the factor's
    // own units: positive means the factor must rise, negative that it must fall.
    public struct FactorGap
    {
        public bool Possible;
        public float Target;
        public float Delta;
    }

    public sealed class RatingExplanation
    {
        public CreditRating Rating;
        public bool Unlevered;          // no debt service: DSCR and burden are not in play
        public bool HardFloorD;         // rated D by the hard floor (arrears or coverage collapse)

        public CreditRating UpRating;   // one notch better (same as Rating at AAA)
        public FactorGap DscrUp;
        public FactorGap BurdenUp;
        public FactorGap ReservesUp;
        public FactorGap SurplusUp;     // unlevered only: annual operating surplus needed
        public bool UpNeedsBoth;        // DscrUp and BurdenUp must both be met together

        public CreditRating DownRating; // one notch worse (same as Rating at D)
        public FactorGap DscrDown;      // margin above the threshold that holds the rating
        public FactorGap BurdenDown;
        public FactorGap ReservesDown;

        public string UpText = "";
        public string DownText = "";
    }

    // WO-35: the rating explains itself. For each of the three factors that decide
    // it (DSCR, debt burden, months of reserves) find the smallest single change
    // that lifts the rating a notch, and the margin before it drops a notch. Pure:
    // it probes RatingEngine with adjusted metrics, so it can never disagree with
    // the model.
    public static class RatingExplainer
    {
        private static readonly float[] DscrSteps = { 0.2f, 0.90f, 1.05f, 1.25f, 1.50f, 2.00f, 2.50f };
        private static readonly float[] BurdenSteps = { 0.08f, 0.12f, 0.18f, 0.25f, 0.32f, 0.40f };
        private static readonly float[] ReserveSteps = { 1.0f, 6.0f };
        // The grid's rows from B up to AAA: minimum DSCR and maximum burden.
        private static readonly float[] RowDscr = { 0.90f, 1.05f, 1.25f, 1.50f, 2.00f, 2.50f };
        private static readonly float[] RowBurden = { 0.40f, 0.32f, 0.25f, 0.18f, 0.12f, 0.08f };

        private const float Epsilon = 0.0005f;

        private static bool Better(CreditRating a, CreditRating b) { return (int)a < (int)b; }
        private static bool Worse(CreditRating a, CreditRating b) { return (int)a > (int)b; }

        public static RatingExplanation Explain(CreditMetrics m, bool hasArrears)
        {
            var e = new RatingExplanation();
            e.Rating = RatingEngine.EvaluateRating(m, hasArrears);
            e.Unlevered = m.AnnualDebtService <= 1f;
            e.HardFloorD = e.Rating == CreditRating.D;
            e.UpRating = e.Rating == CreditRating.AAA ? CreditRating.AAA : (CreditRating)((int)e.Rating - 1);
            e.DownRating = e.Rating == CreditRating.D ? CreditRating.D : (CreditRating)((int)e.Rating + 1);

            if (hasArrears)
            {
                e.UpText = "Clear every arrear to leave D.";
                e.DownText = "";
                return e;
            }

            bool levered = !e.Unlevered;

            // Up: the nearest threshold that improves the rating on its own.
            if (levered)
            {
                for (int i = 0; i < DscrSteps.Length; i++)
                {
                    float t = DscrSteps[i];
                    if (t <= m.DSCR) continue;
                    CreditMetrics p = m; p.DSCR = t;
                    if (Better(RatingEngine.EvaluateRating(p, false), e.Rating))
                    { e.DscrUp = Gap(t, t - m.DSCR); break; }
                }
                for (int i = BurdenSteps.Length - 1; i >= 0; i--)
                {
                    float t = BurdenSteps[i];
                    if (t >= m.DebtBurden) continue;
                    CreditMetrics p = m; p.DebtBurden = t;
                    if (Better(RatingEngine.EvaluateRating(p, false), e.Rating))
                    { e.BurdenUp = Gap(t, t - m.DebtBurden); break; }
                }
            }
            // An unlevered city running a deficit is scored as DSCR 1.0; turning an
            // operating surplus scores it as strong coverage.
            if (e.Unlevered && m.AnnualNOI <= 0f)
            {
                CreditMetrics p = m; p.AnnualNOI = 1f; p.DSCR = 20f;
                if (Better(RatingEngine.EvaluateRating(p, false), e.Rating))
                    e.SurplusUp = Gap(1f, 1f - m.AnnualNOI);
            }
            for (int i = 0; i < ReserveSteps.Length; i++)
            {
                float t = ReserveSteps[i];
                if (t <= m.MonthsOfReserves) continue;
                CreditMetrics p = m; p.MonthsOfReserves = t;
                if (Better(RatingEngine.EvaluateRating(p, false), e.Rating))
                { e.ReservesUp = Gap(t, t - m.MonthsOfReserves); break; }
            }

            // No single factor is enough: the nearest grid row reachable by moving
            // DSCR and burden together.
            if (levered && e.Rating != CreditRating.AAA &&
                !e.DscrUp.Possible && !e.BurdenUp.Possible && !e.ReservesUp.Possible)
            {
                for (int row = 0; row < RowDscr.Length; row++)
                {
                    CreditMetrics p = m;
                    if (p.DSCR < RowDscr[row]) p.DSCR = RowDscr[row];
                    if (p.DebtBurden > RowBurden[row]) p.DebtBurden = RowBurden[row];
                    if (!Better(RatingEngine.EvaluateRating(p, false), e.Rating)) continue;
                    if (p.DSCR > m.DSCR) e.DscrUp = Gap(p.DSCR, p.DSCR - m.DSCR);
                    if (p.DebtBurden < m.DebtBurden) e.BurdenUp = Gap(p.DebtBurden, p.DebtBurden - m.DebtBurden);
                    e.UpNeedsBoth = e.DscrUp.Possible && e.BurdenUp.Possible;
                    break;
                }
            }

            // Down: the nearest threshold below which the rating drops.
            if (levered)
            {
                for (int i = DscrSteps.Length - 1; i >= 0; i--)
                {
                    float t = DscrSteps[i];
                    if (t > m.DSCR) continue;
                    CreditMetrics p = m; p.DSCR = t - Epsilon;
                    if (Worse(RatingEngine.EvaluateRating(p, false), e.Rating))
                    { e.DscrDown = Gap(t, m.DSCR - t); break; }
                }
                for (int i = 0; i < BurdenSteps.Length; i++)
                {
                    float t = BurdenSteps[i];
                    if (t < m.DebtBurden) continue;
                    CreditMetrics p = m; p.DebtBurden = t + Epsilon;
                    if (Worse(RatingEngine.EvaluateRating(p, false), e.Rating))
                    { e.BurdenDown = Gap(t, t - m.DebtBurden); break; }
                }
            }
            for (int i = ReserveSteps.Length - 1; i >= 0; i--)
            {
                float t = ReserveSteps[i];
                if (t > m.MonthsOfReserves) continue;
                CreditMetrics p = m; p.MonthsOfReserves = t - 0.01f;
                if (Worse(RatingEngine.EvaluateRating(p, false), e.Rating))
                { e.ReservesDown = Gap(t, m.MonthsOfReserves - t); break; }
            }

            e.UpText = UpSentence(e);
            e.DownText = DownSentence(e);
            return e;
        }

        private static FactorGap Gap(float target, float delta)
        {
            FactorGap g;
            g.Possible = true;
            g.Target = target;
            g.Delta = delta;
            return g;
        }

        private static string UpSentence(RatingExplanation e)
        {
            if (e.Rating == CreditRating.AAA) return "Top rating.";
            if (e.UpNeedsBoth)
                return string.Format("+{0:F2} DSCR and {1:F1} pts debt burden together to reach {2}.",
                    e.DscrUp.Delta, e.BurdenUp.Delta * 100f, BondPricing.RatingLabel(e.UpRating));
            string parts = Join(
                e.DscrUp.Possible ? string.Format("+{0:F2} DSCR", e.DscrUp.Delta) : null,
                e.BurdenUp.Possible ? string.Format("{0:F1} pts debt burden", e.BurdenUp.Delta * 100f) : null,
                e.SurplusUp.Possible ? string.Format("an operating surplus (+{0:N0} a year)", e.SurplusUp.Delta) : null,
                e.ReservesUp.Possible ? string.Format("+{0:F1} months of reserves", e.ReservesUp.Delta) : null);
            if (parts.Length == 0)
                return string.Format("No single factor reaches {0} on its own.", BondPricing.RatingLabel(e.UpRating));
            return string.Format("{0} to reach {1}.", parts, BondPricing.RatingLabel(e.UpRating));
        }

        private static string DownSentence(RatingExplanation e)
        {
            if (e.Rating == CreditRating.D) return "";
            string parts = Join(
                e.DscrDown.Possible ? string.Format("DSCR falls {0:F2}", e.DscrDown.Delta) : null,
                e.BurdenDown.Possible ? string.Format("debt burden rises {0:F1} pts", e.BurdenDown.Delta * 100f) : null,
                e.ReservesDown.Possible ? string.Format("reserves fall {0:F1} months", e.ReservesDown.Delta) : null);
            if (parts.Length == 0) return "No single factor is near a downgrade.";
            return string.Format("Drops to {0} if {1}.", BondPricing.RatingLabel(e.DownRating), parts);
        }

        private static string Join(params string[] all)
        {
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] != null) n++;
            string result = "";
            int seen = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (seen > 0) result += seen == n - 1 ? " or " : ", ";
                result += all[i];
                seen++;
            }
            return result;
        }
    }
}
