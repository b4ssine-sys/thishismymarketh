using System;
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

            // Any mode that puts the player in a playable city. A scenario start
            // (NewGameFromScenario) is a normal game too; editors are skipped.
            // Compared by name so this compiles whether or not the game build's
            // LoadMode enum declares that member.
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame &&
                mode.ToString() != "NewGameFromScenario")
                return;

            Debug.Log("[MyFirstMod] Level loaded - Municipal Bond Market active.");

            BondMarketEngine.NeedsReset = true;
            if (mode != LoadMode.LoadGame)
                BondMarketEngine.PendingSaveData = null; // fresh city: nothing to restore

            // Build the UI defensively and log any failure: a silent exception
            // here leaves the player with no icon and no panel and no clue why.
            bool uiCreated = false;
            try
            {
                UIView view = UIView.GetAView();
                _panel = (BondMarketPanel)view.AddUIComponent(typeof(BondMarketPanel));
                _toggleButton = (BondToggleButton)view.AddUIComponent(typeof(BondToggleButton));
                uiCreated = _panel != null && _toggleButton != null;
            }
            catch (Exception e)
            {
                Debug.LogError("[MyFirstMod] Failed to create bond market UI: " + e);
            }

            SelfCheck.Log(mode.ToString(), uiCreated);

            ResidentialBuildingLog.Reset();
            if (ResidentialBuildingLog.Instance != null)
                ResidentialBuildingLog.Instance.ScanAll();

            Debug.Log("[MyFirstMod] Click the icon (top-left) to open the bond market.");
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();
            ResidentialBuildingLog.Reset();
            EconomyReader.Reset();
            BondMarketEngine.Instance = null;

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
