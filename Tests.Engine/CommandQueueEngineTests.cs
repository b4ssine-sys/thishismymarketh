// Test-only: compiled solely by Tests.Engine, which defines
// BOND_MARKET_ENGINE_TESTS. The game compiles every .cs under Source\, so
// without this guard a full repo copy fails in-game.
#if BOND_MARKET_ENGINE_TESTS
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace MyFirstMod.EngineTests
{
    // WO-40 acceptance: _lock deleted, and cash moves (whole and partial) are
    // exact when every player action runs as an order on the simulation thread.
    public class CommandQueueEngineTests
    {
        // A city whose auctions clear under the current pricing (see the skipped
        // test at the bottom for the case that does not).
        private static GameHarness ClearingCity()
        {
            return new GameHarness(startCashDisplay: 20000, population: 50000,
                incomePerMonthDisplay: 60000, expensePerMonthDisplay: 58000);
        }

        private static float OwedDisplay(EngineSnapshot s)
        {
            float owed = 0f;
            foreach (var b in s.Issued) owed += b.OutstandingPrincipal + b.Arrears;
            return owed;
        }

        private static string RepoRoot([CallerFilePath] string here = "")
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), ".."));
        }

        [Fact]
        public void Lock_IsDeleted()
        {
            var fields = typeof(BondMarketEngine).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.DoesNotContain(fields, f => f.Name == "_lock");
            Assert.DoesNotContain(fields, f => f.FieldType == typeof(object));

            string source = File.ReadAllText(Path.Combine(RepoRoot(), "MyFirstMod", "BondMarketEngine.cs"));
            Assert.DoesNotContain("lock (", source);
            Assert.DoesNotContain("Monitor.", source);
            Assert.DoesNotContain("LastCashAmount", source);
        }

        [Fact]
        public void Order_WaitsForTheNextTick()
        {
            var h = ClearingCity().Boot();
            int seq = h.Engine.Submit(EngineCommand.AckCreditNotice());
            Assert.True(h.Snap.LastProcessedSequence < seq); // game paused: nothing runs
            h.QuietTick();
            Assert.True(h.Snap.LastProcessedSequence >= seq);
            Assert.NotNull(h.ResultFor(seq));
        }

        // An order still queued when the player loads another city never runs there.
        [Fact]
        public void OrderQueuedBeforeCityChange_IsDiscarded()
        {
            var h = ClearingCity().Boot();
            int executed = h.Snap.CommandsExecuted;
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));

            BondMarketEngine.NeedsReset = true; // next city loads before the next tick
            h.QuietTick();

            Assert.Equal(executed, h.Snap.CommandsExecuted);
            Assert.Empty(h.Snap.Issued);
        }

        // Issue proceeds reach the treasury exactly once whatever the game does
        // with OnUpdateMoneyAmount's return value and whenever it applies moves.
        [Theory]
        [InlineData(true, false)]   // assigns return, moves immediate
        [InlineData(false, false)]  // ignores return, moves immediate
        [InlineData(true, true)]    // assigns return, moves applied after the callback
        [InlineData(false, true)]   // ignores return, moves applied after the callback
        public void IssueProceeds_LandExactlyOnce(bool gameAssignsReturn, bool deferred)
        {
            var h = ClearingCity().Boot();
            h.GameAssignsReturn = gameAssignsReturn;
            h.Economy.Deferred = deferred;

            int seq = h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));
            long delta = h.QuietTick();
            CommandResult r = h.ResultFor(seq);

            Assert.True(r.Success, r.Message);
            Assert.True(r.Amount > 0f);
            Assert.InRange(delta, (long)(r.Amount * 100f) - 1, (long)(r.Amount * 100f) + 1);
            Assert.Equal(h.Economy.TotalAdded - h.Economy.TotalFetched, delta);
            Assert.Single(h.Snap.Issued);
        }

        // The game moves less than asked on a purchase: the buy is all-or-nothing,
        // the partial move is refunded and the bond stays on the market.
        [Fact]
        public void Buy_PartialCashMove_RefundsAndBuysNothing()
        {
            var h = ClearingCity().Boot();
            string id = h.Snap.Market[0].Id;
            h.Economy.FetchCapPerCall = 1000;

            int seq = h.Engine.Submit(EngineCommand.BuyBond(id));
            long delta = h.QuietTick();

            Assert.False(h.ResultFor(seq).Success);
            Assert.Equal(0, delta);
            Assert.Empty(h.Snap.Portfolio);
            Assert.Contains(h.Snap.Market, b => b.Id == id);
        }

        // The game moves less than asked on a repayment: exactly the moved cash
        // comes off the debt; the shortfall stays owed as arrears.
        [Fact]
        public void Repay_PartialCashMove_ConservesMoney()
        {
            var h = ClearingCity().Boot();
            int issue = h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));
            h.QuietTick();
            Assert.True(h.ResultFor(issue).Success, h.ResultFor(issue).Message);

            float owedBefore = OwedDisplay(h.Snap);
            h.Economy.FetchCapPerCall = 5000; // 50.00 display units per call

            int seq = h.Engine.Submit(EngineCommand.PayDebtPercent(0.5f));
            long delta = h.QuietTick();
            float owedAfter = OwedDisplay(h.Snap);

            Assert.Equal(-5000, delta);
            Assert.InRange((owedBefore - owedAfter) * 100f, 4990f, 5010f);
            Assert.True(h.ResultFor(seq).Amount < 0f);
        }

        [Fact]
        public void Repay_FullCashMove_RetiresBondIntoHistory()
        {
            var h = ClearingCity().Boot();
            int issue = h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));
            h.QuietTick();
            string id = h.Snap.Issued[0].Id;
            float owed = OwedDisplay(h.Snap);

            int seq = h.Engine.Submit(EngineCommand.RepayBond(id));
            long delta = h.QuietTick();

            Assert.True(h.ResultFor(seq).Success, h.ResultFor(seq).Message);
            Assert.InRange(-delta, (long)(owed * 100f) - 1, (long)(owed * 100f) + 1);
            Assert.Empty(h.Snap.Issued);
            Assert.Contains(h.Snap.Redeemed, b => b.Id == id);
        }

        // Orders from many threads at once: every one runs exactly once.
        [Fact]
        public void OrdersFromManyThreads_RunExactlyOnce()
        {
            var h = ClearingCity().Boot();
            int before = h.Snap.CommandsExecuted;
            const int threads = 8, perThread = 50;
            var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
            {
                for (int i = 0; i < perThread; i++) h.Engine.Submit(EngineCommand.AckCreditNotice());
            })).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());

            h.QuietTick();
            Assert.Equal(before + threads * perThread, h.Snap.CommandsExecuted);
            Assert.Equal(threads * perThread, h.Snap.LastProcessedSequence);
        }

        // A published snapshot never changes, whatever the engine does next.
        [Fact]
        public void PublishedSnapshot_IsNeverMutated()
        {
            var h = ClearingCity().Boot();
            EngineSnapshot s1 = h.Snap;
            int version = s1.Version, marketLen = s1.Market.Length;
            string firstId = s1.Market[0].Id;
            float firstPrice = s1.Market[0].Price, cash = s1.CashBalance;

            h.Engine.Submit(EngineCommand.BuyBond(firstId));
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));
            h.Months(3);

            Assert.Equal(version, s1.Version);
            Assert.Equal(marketLen, s1.Market.Length);
            Assert.Equal(firstId, s1.Market[0].Id);
            Assert.Equal(firstPrice, s1.Market[0].Price);
            Assert.Equal(cash, s1.CashBalance);
            Assert.Empty(s1.Issued);
            Assert.True(h.Snap.Version > version);
        }

        // Idle ticks publish nothing (and so allocate no snapshot).
        [Fact]
        public void IdleTick_PublishesNothing()
        {
            var h = ClearingCity().Boot();
            EngineSnapshot s1 = h.Snap;
            h.QuietTick();
            h.QuietTick();
            Assert.Same(s1, h.Snap);
        }

        // A save taken from another thread while the simulation ticks is always a
        // whole, loadable state.
        [Fact]
        public void SaveFromAnotherThread_WhileTicking_AlwaysLoads()
        {
            var h = ClearingCity().Boot();
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));
            h.QuietTick();
            string noteId = h.Snap.Issued[0].Id;
            h.Engine.Submit(EngineCommand.BuyBond(h.Snap.Market[0].Id));

            // Bounded (about 20 in-game years) so the fake calendar never overflows,
            // and any error on the simulation thread fails this test.
            bool stop = false;
            Exception simError = null;
            var sim = new Thread(() =>
            {
                try
                {
                    for (int t = 0; t < 3600 && !Volatile.Read(ref stop); t++) h.Tick();
                }
                catch (Exception e) { simError = e; }
            });
            sim.Start();
            while (h.Snap.LastProcessedSequence < 2) Thread.Sleep(1);

            for (int i = 0; i < 25; i++)
            {
                byte[] data = h.Engine.SerializeState();
                Assert.NotNull(data);
                Assert.True(StateSerializer.TryDeserialize(data, out BondMarketState state));
                // The note may mature while the loop runs; it is always in exactly
                // one of the two lists, never lost and never duplicated.
                int copies = state.Issued.Count(b => b.Id == noteId) + state.Redeemed.Count(b => b.Id == noteId);
                Assert.Equal(1, copies);
            }
            Volatile.Write(ref stop, true);
            sim.Join();
            Assert.Null(simError);
        }

        [Fact]
        public void SaveAndLoad_RestoresOrdersCarriedOut()
        {
            var h = ClearingCity().Boot();
            h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));
            h.QuietTick();
            string id = h.Snap.Issued[0].Id;
            byte[] data = h.Engine.SerializeState();

            var loaded = ClearingCity();
            BondMarketEngine.PendingSaveData = data;
            loaded.Tick();

            Assert.Single(loaded.Snap.Issued);
            Assert.Equal(id, loaded.Snap.Issued[0].Id);
        }

        // Known defect, pending a decision (STATUS.md): the auction's fair yield is
        // the risk-free spot at the bond's tenor, while the offer is the city's
        // short-end required yield, which a healthy treasury pulls lower still.
        // A well-reserved AAA city therefore fails its Emergency Note auction.
        [Fact(Skip = "Pending MD decision on auction fair yield; see STATUS.md")]
        public void EmergencyNote_ClearsForWellReservedAaaCity()
        {
            var h = new GameHarness(startCashDisplay: 500000).Boot();
            Assert.Equal(CreditRating.AAA, h.Snap.Rating);
            int seq = h.Engine.Submit(EngineCommand.IssueBond(IssueTemplates.EmergencyNote, 0f));
            h.QuietTick();
            Assert.True(h.ResultFor(seq).Success, h.ResultFor(seq).Message);
        }
    }
}

#endif
