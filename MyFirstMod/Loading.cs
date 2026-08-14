using ICities;
using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    public class Loading : LoadingExtensionBase
    {
        private BondMarketPanel _panel;
        private BondToggleButton _toggleButton;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame)
                return;

            Debug.Log("[MyFirstMod] Level loaded - Municipal Bond Market active.");

            BondMarketEngine.NeedsReset = true;

            UIView view = UIView.GetAView();
            _panel = (BondMarketPanel)view.AddUIComponent(typeof(BondMarketPanel));
            _toggleButton = (BondToggleButton)view.AddUIComponent(typeof(BondToggleButton));

            ResidentialBuildingLog.Reset();
            if (ResidentialBuildingLog.Instance != null)
                ResidentialBuildingLog.Instance.ScanAll();

            Debug.Log("[MyFirstMod] Click the icon (top-left) to open the bond market.");
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();
            ResidentialBuildingLog.Reset();

            if (_toggleButton != null)
            {
                UnityEngine.Object.Destroy(_toggleButton.gameObject);
                _toggleButton = null;
            }

            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel.gameObject);
                _panel = null;
            }
        }
    }
}
