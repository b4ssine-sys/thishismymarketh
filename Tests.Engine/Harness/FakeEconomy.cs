// Test-only: compiled solely by Tests.Engine, which defines
// BOND_MARKET_ENGINE_TESTS. The game compiles every .cs under Source\, so
// without this guard a full repo copy fails in-game.
#if BOND_MARKET_ENGINE_TESTS
using System;
using ColossalFramework;

namespace MyFirstMod.EngineTests
{
    // A treasury that behaves the ways the real game might: moves can hit the
    // balance immediately or after the callback (Deferred), and the game may
    // move less than asked (FetchCapPerCall).
    public sealed class FakeEconomy : EconomyManager
    {
        // Same name as the game's private field, so TreasuryProbe binds to it.
        private long m_cashAmount;

        public long Cash { get { return m_cashAmount; } set { m_cashAmount = value; } }
        public int FetchCapPerCall = int.MaxValue;
        public bool Deferred;
        public long TotalFetched;
        public long TotalAdded;

        // Cumulative operating ledger, read by EconomyReader through the
        // (Service, out long, out long) overload.
        public long LedgerIncome;
        public long LedgerExpense;

        private long _deferredDelta;

        public override int FetchResource(Resource resource, int amount, ItemClass.Service service, ItemClass.SubService subService, ItemClass.Level level)
        {
            int got = Math.Max(0, Math.Min(amount, FetchCapPerCall));
            TotalFetched += got;
            if (Deferred) _deferredDelta -= got; else m_cashAmount -= got;
            return got;
        }

        public override int AddResource(Resource resource, int amount, ItemClass.Service service, ItemClass.SubService subService, ItemClass.Level level)
        {
            TotalAdded += amount;
            if (Deferred) _deferredDelta += amount; else m_cashAmount += amount;
            return amount;
        }

        public void ApplyDeferred()
        {
            m_cashAmount += _deferredDelta;
            _deferredDelta = 0;
        }

        public void GetIncomeAndExpenses(ItemClass.Service service, out long income, out long expense)
        {
            if (service == ItemClass.Service.Residential)
            {
                income = LedgerIncome;
                expense = LedgerExpense;
            }
            else
            {
                income = 0;
                expense = 0;
            }
        }
    }
}

#endif
