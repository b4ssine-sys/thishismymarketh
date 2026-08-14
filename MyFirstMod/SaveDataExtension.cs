using ICities;
using UnityEngine;

namespace MyFirstMod
{
    public class SaveDataExtension : SerializableDataExtensionBase
    {
        private const string DATA_ID = "MyFirstMod_BondMarket";

        public override void OnSaveData()
        {
            if (BondMarketEngine.Instance == null)
                return;

            try
            {
                byte[] data = BondMarketEngine.Instance.SerializeState();
                serializableDataManager.SaveData(DATA_ID, data);
                Debug.Log("[MyFirstMod] Bond market saved (" + data.Length + " bytes).");
            }
            catch (System.Exception ex)
            {
                Debug.Log("[MyFirstMod] Save failed: " + ex.Message);
            }
        }

        public override void OnLoadData()
        {
            try
            {
                byte[] data = serializableDataManager.LoadData(DATA_ID);
                if (data != null && data.Length > 0)
                {
                    BondMarketEngine.PendingSaveData = data;
                    Debug.Log("[MyFirstMod] Bond market data loaded (" + data.Length + " bytes), will restore on next reset.");
                }
                else
                {
                    BondMarketEngine.PendingSaveData = null;
                    Debug.Log("[MyFirstMod] No bond market save data found, starting fresh.");
                }
            }
            catch (System.Exception ex)
            {
                BondMarketEngine.PendingSaveData = null;
                Debug.Log("[MyFirstMod] Load failed: " + ex.Message);
            }
        }
    }
}
