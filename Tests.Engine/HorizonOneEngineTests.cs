// Test-only: compiled solely by Tests.Engine, which defines
// BOND_MARKET_ENGINE_TESTS. The game compiles every .cs under Source\, so
// without this guard a full repo copy fails in-game.
#if BOND_MARKET_ENGINE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace MyFirstMod.EngineTests
{
    // Horizon 1 acceptance criteria, demonstrated against the real engine.
    public class HorizonOneEngineTests
    {
        private readonly ITestOutputHelper _out;
        public HorizonOneEngineTests(ITestOutputHelper output) { _out = output; }

        private static GameHarness ClearingCity()
        {
            return new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000);
        }

        private const int Note = IssueTemplates.EmergencyNote;

        // WO-36 acceptance: a deal priced 200bp tight shows low cover before
        // commit and under-fills after.
        [Fact]
        public void Ticket_200bpTight_ShowsLowCover_ThenUnderFills()
        {
            var h = ClearingCity().Boot();
            EngineSnapshot s = h.Snap;
            float tight = s.SpreadForFullCover[Note] - 0.02f;

            IssuancePreviewResult preview = s.PreviewIssue(Note, tight);
            Assert.True(preview.Cover < PrimaryAuction.MinCover, "preview cover " + preview.Cover);
            Assert.False(preview.Fills);

            int seq = h.Engine.Submit(EngineCommand.IssueBond(Note, tight));
            long delta = h.QuietTick();
            CommandResult r = h.ResultFor(seq);
            Assert.False(r.Success);
            Assert.Equal(preview.Cover, r.Detail, 3);   // the engine saw the cover the ticket showed
            Assert.Equal(0, delta);
            Assert.Empty(h.Snap.Issued);
        }

        // The ticket's preview is what the deal does.
        [Fact]
        public void Ticket_PreviewMatchesTheDeal()
        {
            var h = ClearingCity().Boot();
            EngineSnapshot s = h.Snap;
            float spread = s.SpreadForFullCover[Note] + 0.001f;
            IssuancePreviewResult preview = s.PreviewIssue(Note, spread);
            Assert.True(preview.Fills);

            int seq = h.Engine.Submit(EngineCommand.IssueBond(Note, spread));
            long delta = h.QuietTick();
            CommandResult r = h.ResultFor(seq);

            Assert.True(r.Success, r.Message);
            Assert.Equal(preview.Cover, r.Detail, 3);
            Assert.InRange(delta / 100f, preview.Proceeds - 1f, preview.Proceeds + 1f);
            BondView issued = h.Snap.Issued[0];
            Assert.InRange(issued.OutstandingPrincipal, preview.PlacedFace - 1f, preview.PlacedFace + 1f);
        }

        // The ticket lets a well-reserved AAA city borrow: paying the concession
        // the ticket shows clears the auction that fails at the default price.
        [Fact]
        public void Ticket_ClearsForWellReservedAaaCity_AtTheShownSpread()
        {
            var h = new GameHarness(startCashDisplay: 500000).Boot();
            float spread = h.Snap.SpreadForFullCover[Note] + 0.001f;
            Assert.True(h.Snap.PreviewIssue(Note, spread).Fills);

            int seq = h.Engine.Submit(EngineCommand.IssueBond(Note, spread));
            h.QuietTick();
            Assert.True(h.ResultFor(seq).Success, h.ResultFor(seq).Message);
        }

        // WO-35 acceptance, through the engine: the published rating always comes
        // with its explanation.
        [Fact]
        public void Snapshot_RatingExplainsItself()
        {
            var h = ClearingCity().Boot();
            EngineSnapshot s = h.Snap;
            Assert.Equal(s.Rating, s.Explanation.Rating);
            Assert.False(string.IsNullOrEmpty(s.Explanation.UpText));
        }

        private static void Collect(GameHarness h, Dictionary<int, Alert> seen)
        {
            foreach (Alert a in h.Snap.Alerts) seen[a.Sequence] = a;
        }

        // WO-37 acceptance: a default is never the first warning the player gets.
        // A city issues, then runs a deficit into its note's maturity.
        [Fact]
        public void Ladder_WarnsBeforeTheFirstMissedPayment_AndBeforeDefault()
        {
            var h = ClearingCity().Boot();
            int seq = h.Engine.Submit(EngineCommand.IssueBond(Note, h.Snap.SpreadForFullCover[Note] + 0.001f));
            h.QuietTick();
            Assert.True(h.ResultFor(seq).Success, h.ResultFor(seq).Message);

            // Operating deficit sized to drain the treasury before the note matures.
            h.IncomePerTick = 60000L * 100 / h.TicksPerMonth;
            h.ExpensePerTick = 62500L * 100 / h.TicksPerMonth;

            var alerts = new Dictionary<int, Alert>();
            int firstWarning = -1, firstMiss = -1, firstDefault = -1, firstAmber = -1;
            for (int month = 0; month < 40 && firstDefault < 0; month++)
            {
                h.Months(1);
                EngineSnapshot s = h.Snap;
                Collect(h, alerts);
                if (firstAmber < 0 && s.Ladder.Any(m => m.Imminent)) firstAmber = s.PeriodCounter;
                if (firstWarning < 0 && alerts.Values.Any(a => a.Kind == AlertKind.ShortfallAhead))
                    firstWarning = alerts.Values.First(a => a.Kind == AlertKind.ShortfallAhead).Period;
                if (firstMiss < 0 && s.Issued.Any(b => b.Arrears > 0.01f)) firstMiss = s.PeriodCounter;
                if (firstDefault < 0 && s.Issued.Any(b => b.State == BondState.Defaulted)) firstDefault = s.PeriodCounter;
            }
            _out.WriteLine($"amber={firstAmber} warning={firstWarning} miss={firstMiss} default={firstDefault}");

            Assert.True(firstMiss > 0, "the scenario must reach a missed payment");
            Assert.True(firstWarning > 0, "a shortfall warning must be raised");
            Assert.True(firstMiss - firstWarning >= MaturityLadder.WarningLead,
                $"warning at {firstWarning} must lead the miss at {firstMiss} by {MaturityLadder.WarningLead}");
            Assert.True(firstAmber > 0 && firstAmber <= firstWarning);
            if (firstDefault > 0) Assert.True(firstWarning < firstDefault);
        }

        // WO-38 acceptance: each alert type fires in a scripted test city, at most
        // one alert per in-game month.
        [Theory]
        [InlineData(12345)]
        [InlineData(777)]
        [InlineData(2026)]
        public void Alerts_EachTypeFires_OnePerMonth(int seed)
        {
            var h = new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000, seed: seed).Boot();
            var alerts = new Dictionary<int, Alert>();

            bool Run(AlertKind kind, int maxMonths)
            {
                for (int i = 0; i < maxMonths; i++)
                {
                    h.Months(1);
                    Collect(h, alerts);
                    if (alerts.Values.Any(a => a.Kind == kind)) return true;
                }
                return false;
            }

            // Failed auction: an offer far below fair value.
            h.Engine.Submit(EngineCommand.IssueBond(Note, -0.05f));
            Assert.True(Run(AlertKind.FailedAuction, 2), "failed auction");

            // Downgrade: borrow at a clearing price, then let the operating surplus
            // shrink to almost nothing so debt service coverage collapses.
            h.Engine.Submit(EngineCommand.IssueBond(Note, h.Snap.SpreadForFullCover[Note] + 0.001f));
            h.Months(1);
            Collect(h, alerts);
            Assert.Single(h.Snap.Issued);
            long normalExpense = h.ExpensePerTick;
            h.ExpensePerTick = 59950L * 100 / h.TicksPerMonth;
            Assert.True(Run(AlertKind.Downgrade, 12), "downgrade");
            h.ExpensePerTick = normalExpense;

            // Big rate move: a turbulent rate setting, then back to normal.
            h.Engine.Submit(EngineCommand.SetRateVolatility(20f));
            Assert.True(Run(AlertKind.BigRateMove, 24), "big rate move");
            h.Engine.Submit(EngineCommand.SetRateVolatility(1f));

            // Shortfall ahead, then the missed coupon: a deep deficit into the debt.
            h.IncomePerTick = 10000L * 100 / h.TicksPerMonth;
            h.ExpensePerTick = 60000L * 100 / h.TicksPerMonth;
            Assert.True(Run(AlertKind.ShortfallAhead, 12), "shortfall ahead");
            Assert.True(Run(AlertKind.CouponShortfall, 24), "coupon shortfall");

            foreach (AlertKind kind in Enum.GetValues(typeof(AlertKind)))
                Assert.Contains(alerts.Values, a => a.Kind == kind);
            Assert.Equal(alerts.Count, alerts.Values.Select(a => a.Period).Distinct().Count()); // one per month
            foreach (Alert a in alerts.Values.OrderBy(a => a.Sequence))
                _out.WriteLine($"period {a.Period}: {a.Kind} - {a.Text}");
        }
    }
}

#endif
