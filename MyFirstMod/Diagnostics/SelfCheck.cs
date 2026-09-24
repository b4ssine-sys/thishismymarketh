using System;
using System.Reflection;

namespace MyFirstMod
{
    // WO-34: gathers the game-facing values for StartupReport and writes the line.
    public static class SelfCheck
    {
        public static void Log(string loadMode, bool uiCreated)
        {
            string line;
            try
            {
                bool bound = EconomyReader.Probe();
                line = StartupReport.Format(Mod.Version, StateSerializer.FORMAT_VERSION,
                    bound, EconomyReader.BindingShape, GameBuild(), loadMode, uiCreated);
            }
            catch (Exception e)
            {
                line = StartupReport.Prefix + " failed to gather: " + e.Message;
            }
            UnityEngine.Debug.Log(line);
        }

        // BuildConfig.applicationVersion is looked up by name so a game build that
        // moves or renames it degrades to "unknown" rather than a load failure.
        private static string GameBuild()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t;
                try { t = assemblies[i].GetType("BuildConfig", false); }
                catch { continue; }
                if (t == null) continue;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                FieldInfo f = t.GetField("applicationVersion", flags);
                if (f != null) return Convert.ToString(f.GetValue(null));
                PropertyInfo p = t.GetProperty("applicationVersion", flags);
                if (p != null) return Convert.ToString(p.GetValue(null, null));
            }
            return null;
        }
    }
}
