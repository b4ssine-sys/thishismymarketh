using System;
using System.Reflection;
using ColossalFramework;

namespace MyFirstMod
{
    // Phase 2 (P1-9): read the city's REAL operating income and expenses from the
    // game's ledger instead of inferring them from bank-balance deltas (which
    // conflate taxes with loan drawdowns, milestone rewards and the mod's own
    // transfers).
    //
    // The exact EconomyManager.GetIncomeAndExpenses overload varies across game
    // versions, and this project can't introspect the game assembly at authoring
    // time, so the call is bound by REFLECTION at runtime: the correct overload is
    // resolved once and cached, and if none is found (or anything throws) the
    // reader reports failure and the engine falls back to its balance-delta proxy.
    // This keeps the mod compiling regardless of the target assembly and never
    // crashes the tick.
    public static class EconomyReader
    {
        private static bool _resolved;
        private static MethodInfo _method;
        private static int _shape;              // 0 = (Service,out,out); 1 = (Service,SubService,Level,out,out)
        private static Array _services;         // cached ItemClass.Service values
        private static bool _servicesCached;

        public static void Reset()
        {
            _resolved = false;
            _method = null;
            _shape = 0;
        }

        private static void CacheServices()
        {
            _servicesCached = true;
            try { _services = Enum.GetValues(typeof(ItemClass.Service)); }
            catch { _services = null; }
        }

        private static void Resolve(EconomyManager em)
        {
            _resolved = true;
            _method = null;
            try
            {
                Type t = em.GetType();
                Type svc = typeof(ItemClass.Service);
                Type sub = typeof(ItemClass.SubService);
                Type lvl = typeof(ItemClass.Level);
                Type lref = typeof(long).MakeByRefType();

                _method = t.GetMethod("GetIncomeAndExpenses", new Type[] { svc, sub, lvl, lref, lref });
                if (_method != null) { _shape = 1; return; }

                _method = t.GetMethod("GetIncomeAndExpenses", new Type[] { svc, lref, lref });
                if (_method != null) { _shape = 0; return; }
            }
            catch
            {
                _method = null;
            }
        }

        // Sums cumulative operating income and expenses across all services.
        // Returns false if the ledger API is unavailable (caller should fall back).
        public static bool TryReadCumulative(out long income, out long expense)
        {
            income = 0;
            expense = 0;
            try
            {
                EconomyManager em = Singleton<EconomyManager>.instance;
                if (em == null) return false;
                if (!_servicesCached) CacheServices();
                if (_services == null) return false;
                if (!_resolved) Resolve(em);
                if (_method == null) return false;

                long totalIncome = 0;
                long totalExpense = 0;
                for (int i = 0; i < _services.Length; i++)
                {
                    ItemClass.Service service = (ItemClass.Service)_services.GetValue(i);
                    if (service == ItemClass.Service.None) continue;

                    long svcIncome;
                    long svcExpense;
                    Invoke(em, service, out svcIncome, out svcExpense);
                    totalIncome += svcIncome;
                    totalExpense += svcExpense;
                }

                income = totalIncome;
                expense = totalExpense;
                return true;
            }
            catch
            {
                income = 0;
                expense = 0;
                return false;
            }
        }

        // Single reflected call for one service. Assumes the method is resolved;
        // callers are responsible for Resolve() and null-checking _method.
        private static void Invoke(EconomyManager em, ItemClass.Service service,
                                   out long income, out long expense)
        {
            int incIdx = _shape == 1 ? 3 : 1;
            int expIdx = _shape == 1 ? 4 : 2;

            object[] args = _shape == 1
                ? new object[] { service, ItemClass.SubService.None, ItemClass.Level.None, 0L, 0L }
                : new object[] { service, 0L, 0L };

            _method.Invoke(em, args);
            income = (long)args[incIdx];
            expense = (long)args[expIdx];
        }

        // Phase 5 seam (memo G-2): cumulative operating income and expense for ONE
        // service, which is what a revenue-bond pledge is rated against. The
        // per-service loop already existed inside TryReadCumulative; this exposes a
        // single service without summing the rest.
        //
        // Revenue bonds are NOT shipped in Phase 5: the GetIncomeAndExpenses binding
        // below is reflection-based and has never been confirmed against the real
        // game assembly, and the balance-delta fallback carries no per-service
        // breakdown at all - so a pledge backed by a silent fallback would quietly
        // behave as a general obligation. This accessor exists so the feature can
        // land unchanged once the binding is verified in-game; nothing calls it yet.
        //
        // Returns false if the ledger API is unavailable. A false return must NOT be
        // treated as "this service earns nothing" - it means "unknown", and any
        // caller gating issuance on coverage has to refuse rather than assume.
        public static bool TryReadService(ItemClass.Service service,
                                          out long income, out long expense)
        {
            income = 0;
            expense = 0;
            if (service == ItemClass.Service.None) return false;

            try
            {
                EconomyManager em = Singleton<EconomyManager>.instance;
                if (em == null) return false;
                if (!_resolved) Resolve(em);
                if (_method == null) return false;

                Invoke(em, service, out income, out expense);
                return true;
            }
            catch
            {
                income = 0;
                expense = 0;
                return false;
            }
        }
    }
}
