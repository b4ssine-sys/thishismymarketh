using System;
using System.Reflection;
using ICities;

namespace MyFirstMod
{
    // WO-38: posts an alert into the game's Chirper, so the player hears about it
    // without the panel open. The Chirper panel is bound by name at runtime (like
    // the ledger reader); if it cannot be found the alert goes to Debug Output and
    // the window's feed only, and the failure is logged once.
    public static class ChirperBridge
    {
        private sealed class Chirp : IChirperMessage
        {
            private readonly string _text;
            public Chirp(string text) { _text = text; }
            public uint senderID { get { return 0u; } }
            public string senderName { get { return "City Treasurer"; } }
            public string text { get { return _text; } }
        }

        private static bool _resolved;
        private static object _panel;
        private static MethodInfo _addMessage;
        private static bool _failureLogged;

        public static void Reset()
        {
            _resolved = false;
            _panel = null;
            _addMessage = null;
        }

        public static bool Post(string text)
        {
            UnityEngine.Debug.Log("[MyFirstMod] Alert: " + text);
            try
            {
                if (!_resolved) Resolve();
                if (_panel == null || _addMessage == null) return false;
                ParameterInfo[] ps = _addMessage.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = new Chirp(text);
                for (int i = 1; i < ps.Length; i++)
                    args[i] = ps[i].ParameterType == typeof(bool) ? (object)true : null;
                _addMessage.Invoke(_panel, args);
                return true;
            }
            catch (Exception e)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    UnityEngine.Debug.Log("[MyFirstMod] Chirper unavailable, alerts go to the panel feed only: " + e.Message);
                }
                _panel = null;
                return false;
            }
        }

        private static void Resolve()
        {
            _resolved = true;
            Type chirpPanel = null;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length && chirpPanel == null; i++)
            {
                try { chirpPanel = assemblies[i].GetType("ChirpPanel", false); }
                catch (Exception) { }
            }
            if (chirpPanel == null) return;

            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            PropertyInfo prop = chirpPanel.GetProperty("instance", statics);
            if (prop != null) _panel = prop.GetValue(null, null);
            if (_panel == null)
            {
                FieldInfo field = chirpPanel.GetField("instance", statics);
                if (field != null) _panel = field.GetValue(null);
            }

            MethodInfo[] methods = chirpPanel.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "AddMessage") continue;
                ParameterInfo[] ps = methods[i].GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType.IsAssignableFrom(typeof(Chirp)))
                {
                    _addMessage = methods[i];
                    if (ps.Length == 1) break; // prefer the simplest overload
                }
            }
        }
    }
}
