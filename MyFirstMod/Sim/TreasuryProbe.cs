using System;
using System.Reflection;
using System.Reflection.Emit;
using ColossalFramework;

namespace MyFirstMod
{
    // The engine moves cash (AddResource/FetchResource) inside the game's
    // OnUpdateMoneyAmount callback, and the game may assign the callback's return
    // value back to its treasury. Returning the balance we were handed would then
    // erase every move made during the callback. To be correct whether the game
    // assigns the return value or ignores it, and whether the moves hit the
    // treasury immediately or later, the engine reads the live treasury field
    // before and after its moves and returns what it was handed plus the change.
    //
    // Bound by name at runtime (the field is private). If it cannot be found the
    // engine returns what it was handed, exactly as before, and the self-check
    // line reports cash=unbound.
    public static class TreasuryProbe
    {
        private const string FieldName = "m_cashAmount";

        private delegate long CashReader(object economyManager);

        private static volatile bool _resolved;
        private static volatile FieldInfo _field;
        // WO-44: a compiled field read, so the per-tick treasury read allocates
        // nothing (FieldInfo.GetValue boxes). Falls back to reflection if the
        // runtime cannot emit.
        private static volatile CashReader _reader;

        public static void Reset()
        {
            _field = null;
            _reader = null;
            _resolved = false;
        }

        public static bool Bound { get { return _field != null; } }
        public static string Mode { get { return _field != null ? "live-field" : "unbound"; } }

        public static bool Probe()
        {
            object em = Singleton<EconomyManager>.instance;
            if (em != null && !_resolved) Resolve(em.GetType());
            return _field != null;
        }

        public static bool TryRead(out long cash)
        {
            cash = 0L;
            try
            {
                object em = Singleton<EconomyManager>.instance;
                if (em == null) return false;
                if (!_resolved) Resolve(em.GetType());
                FieldInfo f = _field;
                if (f == null) return false;
                CashReader reader = _reader;
                cash = reader != null ? reader(em) : Convert.ToInt64(f.GetValue(em));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static CashReader Compile(FieldInfo field)
        {
            try
            {
                DynamicMethod m = new DynamicMethod("ReadTreasury", typeof(long), new Type[] { typeof(object) },
                    typeof(TreasuryProbe).Module, true);
                ILGenerator il = m.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, field.DeclaringType);
                il.Emit(OpCodes.Ldfld, field);
                if (field.FieldType == typeof(int)) il.Emit(OpCodes.Conv_I8);
                il.Emit(OpCodes.Ret);
                return (CashReader)m.CreateDelegate(typeof(CashReader));
            }
            catch
            {
                return null;
            }
        }

        private static void Resolve(Type t)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
                for (Type cur = t; cur != null && _field == null; cur = cur.BaseType)
                {
                    FieldInfo f = cur.GetField(FieldName, flags);
                    if (f != null && (f.FieldType == typeof(long) || f.FieldType == typeof(int)))
                        _field = f;
                }
                if (_field != null) _reader = Compile(_field);
            }
            catch
            {
            }
            _resolved = true;
        }
    }
}
