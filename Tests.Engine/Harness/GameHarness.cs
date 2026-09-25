// Test-only: compiled solely by Tests.Engine, which defines
// BOND_MARKET_ENGINE_TESTS. The game compiles every .cs under Source\, so
// without this guard a full repo copy fails in-game.
#if BOND_MARKET_ENGINE_TESTS
using System;
using ColossalFramework;

namespace MyFirstMod.EngineTests
{
    // A minimal stand-in for the game loop around BondMarketEngine: it owns the
    // singletons, feeds organic income and expense into the treasury each tick,
    // calls OnUpdateMoneyAmount, applies the return value the way the game may,
    // and turns the calendar month every TicksPerMonth ticks.
    public sealed class GameHarness
    {
        public readonly FakeEconomy Economy = new FakeEconomy();
        public readonly SimulationManager Sim = new SimulationManager();
        public readonly DistrictManager Districts = new DistrictManager();
        public BondMarketEngine Engine = new BondMarketEngine();

        public bool GameAssignsReturn = true;
        public int TicksPerMonth = 15;
        public long IncomePerTick;   // internal units
        public long ExpensePerTick;  // internal units

        private int _tickInMonth;

        // Internal units: 100 per display unit, as in the game.
        public GameHarness(long startCashDisplay = 500000, int population = 50000,
            long incomePerMonthDisplay = 60000, long expensePerMonthDisplay = 45000, int seed = 12345)
        {
            Singleton<EconomyManager>.instance = Economy;
            Singleton<SimulationManager>.instance = Sim;
            Singleton<DistrictManager>.instance = Districts;
            Singleton<CitizenManager>.instance = null;
            EconomyReader.Reset();
            TreasuryProbe.Reset();
            BondMarketEngine.Instance = null;
            BondMarketEngine.PendingSaveData = null;
            BondMarketEngine.NeedsReset = true;
            BondMarketEngine.SeedForNextReset = seed;

            Sim.m_currentGameTime = new DateTime(2030, 1, 1);
            Districts.m_districts.m_buffer = new District[1];
            Districts.m_districts.m_size = 1;
            Districts.m_districts.m_buffer[0].m_populationData.m_finalCount = (uint)population;
            Districts.m_districts.m_buffer[0].m_finalHappiness = 80;

            Economy.Cash = startCashDisplay * 100;
            IncomePerTick = incomePerMonthDisplay * 100 / TicksPerMonth;
            ExpensePerTick = expensePerMonthDisplay * 100 / TicksPerMonth;
        }

        public EngineSnapshot Snap { get { return Engine.Snapshot; } }

        public void Tick()
        {
            Economy.Cash += IncomePerTick - ExpensePerTick;
            Economy.LedgerIncome += IncomePerTick;
            Economy.LedgerExpense += ExpensePerTick;

            long handed = Economy.Cash;
            long returned = Engine.OnUpdateMoneyAmount(handed);
            if (GameAssignsReturn) Economy.Cash = returned;
            if (Economy.Deferred) Economy.ApplyDeferred();

            _tickInMonth++;
            if (_tickInMonth >= TicksPerMonth)
            {
                _tickInMonth = 0;
                Sim.m_currentGameTime = Sim.m_currentGameTime.AddMonths(1);
            }
        }

        public void Ticks(int n)
        {
            for (int i = 0; i < n; i++) Tick();
        }

        public void Months(int n)
        {
            Ticks(n * TicksPerMonth);
        }

        // Runs one tick with no organic flow, so the treasury change is exactly
        // what the engine moved.
        public long QuietTick()
        {
            long inc = IncomePerTick, exp = ExpensePerTick;
            IncomePerTick = 0; ExpensePerTick = 0;
            long before = Economy.Cash;
            Tick();
            IncomePerTick = inc; ExpensePerTick = exp;
            return Economy.Cash - before;
        }

        public CommandResult ResultFor(int sequence)
        {
            CommandResult[] results = Snap.RecentResults;
            for (int i = 0; i < results.Length; i++)
                if (results[i].Sequence == sequence) return results[i];
            return null;
        }

        // Ticks until the engine has just run a period boundary, so the next
        // TicksPerMonth - 1 ticks are ordinary ones.
        public void AlignToPeriodStart()
        {
            int period = Snap.PeriodCounter;
            for (int i = 0; i < TicksPerMonth + 1 && Snap.PeriodCounter == period; i++) Tick();
        }

        // A booted city: metrics initialised and a few months of history, ending
        // just after a month boundary so the next tick is an ordinary one.
        public GameHarness Boot(int months = 3)
        {
            Months(months);
            Tick();
            return this;
        }
    }
}

#endif
