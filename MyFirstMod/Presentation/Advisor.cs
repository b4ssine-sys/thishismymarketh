namespace MyFirstMod
{
    public enum AdviceAction
    {
        None,
        OpenBorrow,       // the issuance ticket, TemplateIndex selected
        PayDown,          // the Borrow workspace's repayment controls
        OpenRisk,         // hedging
        OpenInvest
    }

    public sealed class Advice
    {
        public string Text;
        public AdviceAction Action;
        public int TemplateIndex;
        public string ActionLabel;
    }

    // WO-35 / Treasury: the one recommendation the player sees first. Pure: reads
    // only the published snapshot, most urgent condition first.
    public static class Advisor
    {
        public static Advice Recommend(EngineSnapshot s)
        {
            if (s.HasArrears)
                return Make("Debt service is in arrears. Pay it down before the grace period ends, or the bond defaults.",
                    AdviceAction.PayDown, "Pay down");

            int shortfall = MaturityLadder.FirstShortfall(s.Ladder);
            if (shortfall > 0 && shortfall <= 6)
                return Make(string.Format(
                    "Payments due in {0} month(s) exceed the cash the city is projected to have. Pay down early or build reserves now.",
                    shortfall), AdviceAction.PayDown, "Open Borrow");

            RatingExplanation e = s.Explanation;
            if (e != null && e.Rating != CreditRating.D && NearDowngrade(e))
                return Make("One step from a downgrade. " + e.DownText, AdviceAction.None, null);

            if (s.IssuedCount == 0 && s.CanIssueBonds && IssueTemplates.IsAvailable(IssueTemplates.EmergencyNote, s.RevenueBondsEnabled))
            {
                Advice a = Make("Start a credit history: an Emergency Note is the smallest, shortest bond. Price it on the ticket and issue.",
                    AdviceAction.OpenBorrow, "Price an Emergency Note");
                a.TemplateIndex = IssueTemplates.EmergencyNote;
                return a;
            }

            if (s.RecommendedHedge != null && s.RecommendedHedge.StartsWith("HIGH RISK"))
                return Make("Rate risk is high on unhedged debt. " + s.RecommendedHedge + ".", AdviceAction.OpenRisk, "Open Risk");

            if (s.PortfolioCount == 0 && s.MonthsOfReserves >= 6f)
                return Make("Reserves are well above six months. Idle cash could earn coupons in the bond market.",
                    AdviceAction.OpenInvest, "Open Invest");

            string up = e != null ? e.UpText : "";
            return Make("Finances are steady. " + up, AdviceAction.None, null);
        }

        private static bool NearDowngrade(RatingExplanation e)
        {
            return (e.DscrDown.Possible && e.DscrDown.Delta < 0.10f)
                || (e.BurdenDown.Possible && e.BurdenDown.Delta < 0.01f)
                || (e.ReservesDown.Possible && e.ReservesDown.Delta < 0.5f);
        }

        private static Advice Make(string text, AdviceAction action, string label)
        {
            Advice a = new Advice();
            a.Text = text;
            a.Action = action;
            a.ActionLabel = label;
            return a;
        }
    }
}
