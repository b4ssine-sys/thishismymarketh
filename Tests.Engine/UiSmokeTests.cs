// Test-only: compiled solely by Tests.Engine, which defines
// BOND_MARKET_ENGINE_TESTS. The game compiles every .cs under Source\, so
// without this guard a full repo copy fails in-game.
#if BOND_MARKET_ENGINE_TESTS
using Xunit;

namespace MyFirstMod.EngineTests
{
    // The real window and workspaces, built on the stub UI types and fed real
    // engine snapshots. The game cannot run here; this proves every rendering
    // path runs through a city's life without a null reference or a bad index,
    // and that an idle window does no label work (WO-42).
    public class UiSmokeTests
    {
        private static BondMarketWindow OpenWindow()
        {
            var w = new BondMarketWindow();
            w.Start();
            w.Toggle(); // visible; shows the first-run briefing too
            return w;
        }

        private static void RenderAll(BondMarketWindow w)
        {
            for (int i = 0; i < 4; i++)
            {
                w.SelectWorkspace(i);
                w.RedrawNow();
            }
        }

        [Fact]
        public void EveryWorkspaceRenders_ThroughACitysLife()
        {
            var h = new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000).Boot();
            var w = OpenWindow();
            RenderAll(w);

            // Borrow, invest, hedge.
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, h.Snap.SpreadForFullCover[IssueTemplates.EmergencyNote] + 0.001f));
            h.Engine.Submit(EngineCommand.BuyBond(h.Snap.Market[0].Id));
            h.QuietTick();
            h.Engine.Submit(EngineCommand.AutoHedge());
            h.QuietTick();
            RenderAll(w);
            Assert.Single(h.Snap.Issued);
            Assert.Single(h.Snap.Portfolio);
            Assert.Single(h.Snap.Swaps);

            // A year of quarters, then a deficit into the debt, rendering as it goes.
            for (int q = 0; q < 4; q++) { h.Months(3); RenderAll(w); }
            h.IncomePerTick = 10000L * 100 / h.TicksPerMonth;
            h.ExpensePerTick = 60000L * 100 / h.TicksPerMonth;
            for (int m = 0; m < 30; m++) { h.Months(1); RenderAll(w); }

            Assert.True(h.Snap.Alerts.Length > 0);
        }

        [Fact]
        public void OrderOutcome_ReachesTheStatusLine()
        {
            var h = new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000).Boot();
            var w = OpenWindow();
            RenderAll(w);

            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, -0.05f));
            h.QuietTick();
            w.RedrawNow();
            Assert.StartsWith("Not done: auction failed", w.StatusText);
        }

        [Fact]
        public void TreasuryRecommendation_OpensTheTicket()
        {
            var h = new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000).Boot();
            var w = OpenWindow();
            w.SelectWorkspace(BondMarketWindow.TreasuryIndex);
            w.RedrawNow();
            Assert.Equal(AdviceAction.OpenBorrow, Advisor.Recommend(h.Snap).Action);
            w.OpenBorrow(IssueTemplates.EmergencyNote);
            w.RedrawNow();
            Assert.Equal(BondMarketWindow.BorrowIndex, w.ActiveWorkspace);
        }

        // WO-42 acceptance: close to zero UI work while the city is idle. With no
        // new snapshot the window writes no label at all, however often it checks.
        [Fact]
        public void IdleWindow_WritesNoLabels()
        {
            var h = new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000).Boot();
            var w = OpenWindow();
            RenderAll(w);
            w.SelectWorkspace(BondMarketWindow.TreasuryIndex);
            w.RedrawNow();

            // Ten ticks inside one month (no period boundary): the engine publishes
            // nothing, and a hundred redraws find nothing to do.
            int before = BoundLabel.Writes;
            for (int i = 0; i < 10; i++)
            {
                h.QuietTick();
                for (int j = 0; j < 10; j++) w.RedrawNow();
            }
            Assert.Equal(before, BoundLabel.Writes);
        }

        // A new snapshot rewrites only the figures whose values changed.
        [Fact]
        public void NewSnapshot_RewritesOnlyChangedFigures()
        {
            var h = new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000).Boot();
            var w = OpenWindow();
            w.SelectWorkspace(BondMarketWindow.RiskIndex);
            w.RedrawNow();

            int before = BoundLabel.Writes;
            h.Engine.Submit(EngineCommand.AckCreditNotice()); // publishes, changes nothing on Risk
            h.QuietTick();
            w.RedrawNow();
            int after = BoundLabel.Writes;
            Assert.True(after - before <= 2, "a no-op order rewrote " + (after - before) + " labels");
        }
    }
}

#endif
