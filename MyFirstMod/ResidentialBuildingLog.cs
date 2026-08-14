using System;
using ICities;
using ColossalFramework;
using UnityEngine;

namespace MyFirstMod
{
    public class ResidentialBuildingLog : BuildingExtensionBase
    {
        public static ResidentialBuildingLog Instance;

        private const int MAX_BUILDINGS = 49152;

        private static readonly object _lock = new object();

        private static readonly bool[] _tracked = new bool[MAX_BUILDINGS];
        private static bool _scanned;

        private static int _totalCount;
        private static int _lowCount;
        private static int _highCount;
        private static int _lowEcoCount;
        private static int _highEcoCount;

        public int TotalCount { get { lock (_lock) { return _totalCount; } } }
        public int LowCount { get { lock (_lock) { return _lowCount; } } }
        public int HighCount { get { lock (_lock) { return _highCount; } } }
        public int LowEcoCount { get { lock (_lock) { return _lowEcoCount; } } }
        public int HighEcoCount { get { lock (_lock) { return _highEcoCount; } } }

        public override void OnCreated(IBuilding building)
        {
            base.OnCreated(building);
            Instance = this;
        }

        public override void OnReleased()
        {
            if (Instance == this)
                Instance = null;
            base.OnReleased();
        }

        public void ScanAll()
        {
            lock (_lock)
            {
                Array.Clear(_tracked, 0, _tracked.Length);
                _totalCount = 0;
                _lowCount = 0;
                _highCount = 0;
                _lowEcoCount = 0;
                _highEcoCount = 0;

                BuildingManager bm = Singleton<BuildingManager>.instance;
                if (bm == null)
                {
                    _scanned = true;
                    return;
                }

                Building[] buffer = bm.m_buildings.m_buffer;
                int len = buffer.Length;
                if (len > MAX_BUILDINGS) len = MAX_BUILDINGS;

                for (ushort i = 1; i < len; i++)
                {
                    Building b = buffer[i];
                    if ((b.m_flags & Building.Flags.Created) == 0)
                        continue;
                    if (!IsResidential(b))
                        continue;

                    _tracked[i] = true;
                    _totalCount++;
                    AdjustSubType(b, 1);
                }
                _scanned = true;
            }

            Debug.Log(string.Format(
                "[MyFirstMod] Residential scan: {0} total (Low: {1}, High: {2}, LowEco: {3}, HighEco: {4})",
                _totalCount, _lowCount, _highCount, _lowEcoCount, _highEcoCount));
        }

        public override void OnBuildingCreated(ushort id)
        {
            if (!_scanned) return;
            if (id == 0 || id >= MAX_BUILDINGS) return;

            BuildingManager bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            Building b = bm.m_buildings.m_buffer[id];
            if ((b.m_flags & Building.Flags.Created) == 0) return;
            if (!IsResidential(b)) return;

            lock (_lock)
            {
                if (_tracked[id]) return;
                _tracked[id] = true;
                _totalCount++;
                AdjustSubType(b, 1);
            }

            Debug.Log(string.Format(
                "[MyFirstMod] Residential built: #{0} {1} Total: {2}",
                id, SubTypeName(b), _totalCount));
        }

        public override void OnBuildingReleased(ushort id)
        {
            if (id == 0 || id >= MAX_BUILDINGS) return;

            lock (_lock)
            {
                if (!_tracked[id]) return;

                BuildingManager bm = Singleton<BuildingManager>.instance;
                if (bm != null)
                {
                    Building b = bm.m_buildings.m_buffer[id];
                    AdjustSubType(b, -1);
                }

                _tracked[id] = false;
                _totalCount--;
            }

            Debug.Log(string.Format(
                "[MyFirstMod] Residential removed: #{0} Total: {1}",
                id, _totalCount));
        }

        private static bool IsResidential(Building b)
        {
            if (b.Info == null) return false;
            if (b.Info.m_class == null) return false;
            return b.Info.m_class.m_service == ItemClass.Service.Residential;
        }

        private static ItemClass.SubService GetSubService(Building b)
        {
            if (b.Info == null) return ItemClass.SubService.None;
            if (b.Info.m_class == null) return ItemClass.SubService.None;
            return b.Info.m_class.m_subService;
        }

        private void AdjustSubType(Building b, int delta)
        {
            ItemClass.SubService sub = GetSubService(b);
            if (sub == ItemClass.SubService.ResidentialLow)
                _lowCount += delta;
            else if (sub == ItemClass.SubService.ResidentialHigh)
                _highCount += delta;
            else if (sub == ItemClass.SubService.ResidentialLowEco)
                _lowEcoCount += delta;
            else if (sub == ItemClass.SubService.ResidentialHighEco)
                _highEcoCount += delta;
        }

        private static string SubTypeName(Building b)
        {
            ItemClass.SubService sub = GetSubService(b);
            if (sub == ItemClass.SubService.ResidentialLow) return "Low";
            if (sub == ItemClass.SubService.ResidentialHigh) return "High";
            if (sub == ItemClass.SubService.ResidentialLowEco) return "LowEco";
            if (sub == ItemClass.SubService.ResidentialHighEco) return "HighEco";
            return "Other";
        }

        public static void Reset()
        {
            lock (_lock)
            {
                Array.Clear(_tracked, 0, _tracked.Length);
                _totalCount = 0;
                _lowCount = 0;
                _highCount = 0;
                _lowEcoCount = 0;
                _highEcoCount = 0;
                _scanned = false;
            }
        }
    }
}
