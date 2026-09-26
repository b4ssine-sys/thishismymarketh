// Headless simulator only (BOND_MARKET_HEADLESS). The game compiles every .cs
// under Source\, so without this guard a full repo copy fails in-game.
#if BOND_MARKET_HEADLESS

namespace MyFirstMod.HeadlessSim
{
    public enum Strategy
    {
        Passive,     // never borrows: how unlevered cities are rated
        Prudent,     // small notes at the clearing price, repays when flush
        Aggressive   // the largest issue the market will take, whenever it can
    }

    // A scripted player. Acts once a month on the published snapshot, placing
    // orders exactly as the UI would.
    public static class Player
    {
        public static void Act(Strategy strategy, BondMarketEngine engine, EngineSnapshot s)
        {
            if (strategy == Strategy.Passive || !s.CanIssueBonds && s.Issued.Length == 0) return;

            if (strategy == Strategy.Prudent)
            {
                if (s.MonthsOfReserves > 8f && s.Issued.Length > 0)
                {
                    engine.Submit(EngineCommand.PayDebtPercent(0.25f));
                    return;
                }
                if (s.CanIssueBonds && s.Issued.Length < 2 && s.MonthsOfReserves < 4f)
                    Issue(engine, s, CheapestAvailable(s, 0.15f * s.GrossIncome));
                return;
            }

            // Aggressive
            if (s.CanIssueBonds)
                Issue(engine, s, LargestAvailable(s));
        }

        private static void Issue(BondMarketEngine engine, EngineSnapshot s, int template)
        {
            if (template < 0) return;
            float spread = s.SpreadForFullCover[template];
            if (float.IsNaN(spread)) return;
            engine.Submit(EngineCommand.IssueBond(template, spread + 0.0005f));
        }

        // The largest template that fits the market's remaining capacity.
        private static int LargestAvailable(EngineSnapshot s)
        {
            int best = -1;
            for (int t = 0; t < IssueTemplates.Count; t++)
            {
                if (!s.IsTemplateAvailable(t) || IssueTemplates.Face(t) > s.RemainingCapacity) continue;
                if (best < 0 || IssueTemplates.Face(t) > IssueTemplates.Face(best)) best = t;
            }
            return best;
        }

        // The largest template no bigger than the target face.
        private static int CheapestAvailable(EngineSnapshot s, float targetFace)
        {
            int best = s.IsTemplateAvailable(IssueTemplates.EmergencyNote) ? IssueTemplates.EmergencyNote : -1;
            for (int t = 0; t < IssueTemplates.Count; t++)
            {
                if (!s.IsTemplateAvailable(t) || IssueTemplates.Face(t) > targetFace) continue;
                if (IssueTemplates.Face(t) > s.RemainingCapacity) continue;
                if (best < 0 || IssueTemplates.Face(t) > IssueTemplates.Face(best)) best = t;
            }
            return best;
        }
    }
}

#endif
