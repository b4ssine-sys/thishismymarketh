using ICities;
using UnityEngine;

namespace MyFirstMod
{
    public class SaveDataExtension : SerializableDataExtensionBase
    {
        private const string DATA_KEY = "MyFirstMod_BondMarket";

        public override void OnSaveData()
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null)
            {
                Debug.Log("[MyFirstMod] SaveData: No engine instance, skipping save.");
                return;
            }

            byte[] data = engine.SerializeState();
            if (data == null || data.Length == 0)
            {
                Debug.Log("[MyFirstMod] SaveData: Serialization returned empty, skipping.");
                return;
            }

            serializableDataManager.SaveData(DATA_KEY, data);
            Debug.Log("[MyFirstMod] SaveData: Saved " + data.Length + " bytes.");
        }

        public override void OnLoadData()
        {
            byte[] data = serializableDataManager.LoadData(DATA_KEY);
            if (data == null || data.Length == 0)
            {
                Debug.Log("[MyFirstMod] LoadData: No saved data found.");
                BondMarketEngine.PendingSaveData = null;
                return;
            }

            BondMarketEngine.PendingSaveData = data;
            Debug.Log("[MyFirstMod] LoadData: Loaded " + data.Length + " bytes, pending restore.");
        }
    }
}
