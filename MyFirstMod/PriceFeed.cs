using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MyFirstMod
{
    // Live investment price source + comprehensive reflection discovery for the Financial Districts DLC internals.
    public static class PriceFeed
    {
        private static readonly string[] Keywords = new string[]
        {
            "Investment",
            "Invest",
            "Stock",
            "Share",
            "Company",
            "Market",
            "Portfolio",
            "Finance",
            "Trading",
            "Greasy",
            "Gasoline",
            "Oil"
        };

        public static float GetSpot(string underlyingId, float fallback, out bool isLive)
        {
            isLive = false;
            // Real investment value reflection hook will be inserted here once its structural path is logged
            return fallback;
        }

        public static void Discover()
        {
            Debug.Log("[MyFirstMod] --- STARTING EXPANDED DEEP PRICE FEED DISCOVERY ---");

            for (int i = 0; i < Keywords.Length; i++)
            {
                ListTypes(Keywords[i]);
            }

            Dump("EconomyManager");
            Dump("InvestmentManager");
            Dump("StockMarketManager");
            Dump("FinancialManager");

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = SafeGetTypes(a);
                foreach (Type t in types)
                {
                    foreach (string keyword in Keywords)
                    {
                        if (Contains(t.Name, keyword))
                        {
                            DumpType(t, GetInstance(t));
                            break;
                        }
                    }
                }
            }

            Debug.Log("[MyFirstMod] --- END OF DEEP PRICE FEED DISCOVERY ---");
        }

        public static void ListTypes(string keyword)
        {
            StringBuilder sb = new StringBuilder("[MyFirstMod] Types containing '" + keyword + "':\n");
            int matchCount = 0;

            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = SafeGetTypes(a);
                foreach (Type t in types)
                {
                    if (Contains(t.Name, keyword))
                    {
                        sb.AppendLine("  " + t.FullName);
                        matchCount++;
                    }
                }
            }

            if (matchCount > 0)
            {
                Debug.Log(sb.ToString());
            }
        }

        public static void Dump(string typeName)
        {
            Type t = FindType(typeName);
            if (t == null)
            {
                Debug.Log("[MyFirstMod] Core class type not found via direct scan: " + typeName);
                return;
            }
            DumpType(t, GetInstance(t));
        }

        private static void DumpType(Type t, object instance)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[MyFirstMod] DUMPING MEMBERS FOR: " + t.FullName + (instance == null ? " (No live static instance found)" : " (Live active instance captured!)"));

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            FieldInfo[] fields = t.GetFields(flags);
            foreach (FieldInfo f in fields)
            {
                sb.AppendLine("  [Field] " + f.FieldType.Name + " " + f.Name);
            }

            PropertyInfo[] properties = t.GetProperties(flags);
            foreach (PropertyInfo p in properties)
            {
                sb.AppendLine("  [Prop]  " + p.PropertyType.Name + " " + p.Name);
            }

            MethodInfo[] methods = t.GetMethods(flags);
            foreach (MethodInfo m in methods)
            {
                if (Contains(m.Name, "Get") || Contains(m.Name, "Price") || Contains(m.Name, "Stock") || Contains(m.Name, "Value"))
                {
                    sb.AppendLine("  [Method] " + m.ReturnType.Name + " " + m.Name);
                }
            }

            Debug.Log(sb.ToString());
        }

        private static bool Contains(string s, string sub)
        {
            if (s == null || sub == null) return false;
            return s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Type FindType(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types = SafeGetTypes(a);
                foreach (Type t in types)
                {
                    if (t.Name == name || t.FullName == name) return t;
                }
            }
            return null;
        }

        private static Type[] SafeGetTypes(Assembly a)
        {
            try
            {
                return a.GetTypes();
            }
            catch
            {
                return new Type[0];
            }
        }

        private static object GetInstance(Type t)
        {
            string[] instancePatterns = new string[] { "instance", "Instance", "m_instance", "m_Instance" };
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            foreach (string pattern in instancePatterns)
            {
                PropertyInfo p = t.GetProperty(pattern, flags);
                if (p != null)
                {
                    try { return p.GetValue(null, null); } catch { }
                }

                FieldInfo f = t.GetField(pattern, flags);
                if (f != null)
                {
                    try { return f.GetValue(null); } catch { }
                }
            }

            return null;
        }
    }
}
