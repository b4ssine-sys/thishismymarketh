using System;
using UnityEngine;

namespace MyFirstMod
{
    // WO-39: per-player UI memory (panel position, last workspace, text scale,
    // whether the briefing has been seen). Stored in Unity's PlayerPrefs, not the
    // save, so it follows the player across cities. Every access is guarded: a
    // failed read falls back to the default, a failed write is ignored.
    public static class UiPrefs
    {
        private const string Prefix = "MyFirstMod.BondMarket.";

        public const float MinTextScale = 0.8f;
        public const float MaxTextScale = 1.4f;

        public static float GetFloat(string key, float fallback)
        {
            try { return PlayerPrefs.GetFloat(Prefix + key, fallback); }
            catch (Exception) { return fallback; }
        }

        public static int GetInt(string key, int fallback)
        {
            try { return PlayerPrefs.GetInt(Prefix + key, fallback); }
            catch (Exception) { return fallback; }
        }

        public static void SetFloat(string key, float value)
        {
            try { PlayerPrefs.SetFloat(Prefix + key, value); PlayerPrefs.Save(); }
            catch (Exception) { }
        }

        public static void SetInt(string key, int value)
        {
            try { PlayerPrefs.SetInt(Prefix + key, value); PlayerPrefs.Save(); }
            catch (Exception) { }
        }

        public static float TextScale
        {
            get
            {
                float v = GetFloat("TextScale", 1f);
                return v < MinTextScale ? MinTextScale : (v > MaxTextScale ? MaxTextScale : v);
            }
            set { SetFloat("TextScale", value); }
        }

        public static int Workspace
        {
            get { return GetInt("Workspace", 0); }
            set { SetInt("Workspace", value); }
        }

        public static bool BriefingSeen
        {
            get { return GetInt("BriefingSeen", 0) != 0; }
            set { SetInt("BriefingSeen", value ? 1 : 0); }
        }
    }
}
