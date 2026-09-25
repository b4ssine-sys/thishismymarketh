// Test-only: compiled solely by Tests.Engine, which defines
// BOND_MARKET_ENGINE_TESTS. The game compiles every .cs under Source\, so
// without this guard a full repo copy fails in-game.
#if BOND_MARKET_ENGINE_TESTS
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace MyFirstMod.EngineTests
{
    // Horizon 2 (WO-41, WO-44): the engine's per-tick costs.
    public class PerformanceTests
    {
        private readonly ITestOutputHelper _out;
        public PerformanceTests(ITestOutputHelper output) { _out = output; }

        // An active city: debt, holdings, a swap, and six months of history.
        private static GameHarness ActiveCity()
        {
            var h = City();
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, h.Snap.SpreadForFullCover[0] + 0.001f));
            h.Engine.Submit(EngineCommand.BuyBond(h.Snap.Market[0].Id));
            h.QuietTick();
            h.Engine.Submit(EngineCommand.AutoHedge());
            h.QuietTick();
            h.Months(6);
            h.AlignToPeriodStart();
            return h;
        }

        // WO-44 acceptance: 0 bytes allocated per tick.
        [Fact]
        public void OrdinaryTicks_AllocateNothing()
        {
            var h = ActiveCity();
            Assert.Single(h.Snap.Issued);
            Assert.Single(h.Snap.Swaps);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 12; i++) h.Tick(); // inside one month: no period boundary
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
        }

        // WO-44 acceptance: under 50 microseconds of engine time per tick and
        // under 1 ms for the once-per-period metrics block, over five in-game years.
        [Fact]
        public void TickAndPeriod_StayWithinBudget()
        {
            var h = ActiveCity();
            h.Engine.Perf.Reset();
            h.Months(60);
            EnginePerf p = h.Engine.Perf;
            _out.WriteLine(p.Line());
            Assert.True(p.Ticks > 800 && p.Periods >= 59);
            Assert.True(p.TickMicrosAverage < EnginePerf.TickBudgetMicros,
                "tick average " + p.TickMicrosAverage + "us over budget");
            Assert.True(p.PeriodMicrosAverage < EnginePerf.PeriodBudgetMicros,
                "period average " + p.PeriodMicrosAverage + "us over budget");
        }
        private static GameHarness City()
        {
            return new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000).Boot();
        }

        private static string EngineSource([CallerFilePath] string here = "")
        {
            return File.ReadAllText(Path.Combine(Path.GetDirectoryName(here), "..", "MyFirstMod", "BondMarketEngine.cs"));
        }

        // WO-41 acceptance: no Math.Exp in per-tick paths. Between period rebuilds
        // nothing evaluates the curve: not idle ticks, not orders that price bonds
        // and swaps, not the snapshots they publish.
        [Fact]
        public void CurveIsEvaluatedOnlyAtThePeriodRebuild()
        {
            var h = City();
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, h.Snap.SpreadForFullCover[0] + 0.001f));
            h.QuietTick();

            int before = YieldCurve.Evaluations;
            h.QuietTick();                                              // idle
            h.Engine.Submit(EngineCommand.BuyBond(h.Snap.Market[0].Id)); // bond pricing
            h.QuietTick();
            h.Engine.Submit(EngineCommand.AutoHedge());                   // par swap rate
            h.QuietTick();
            h.Engine.Submit(EngineCommand.TerminateAllSwaps());           // swap mark-to-market
            h.QuietTick();
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0.02f)); // auction fair yield
            h.QuietTick();
            Assert.Equal(before, YieldCurve.Evaluations);

            h.Months(1); // a period boundary rebuilds the table
            Assert.True(YieldCurve.Evaluations > before);
        }

        [Fact]
        public void EngineSource_HasNoDirectCurveEvaluation()
        {
            string src = EngineSource();
            Assert.DoesNotContain("Math.Exp", src);
            Assert.DoesNotContain(".SpotRate(", src);
            Assert.DoesNotContain(".DiscountFactor(", src);
        }
    }
}

#endif
