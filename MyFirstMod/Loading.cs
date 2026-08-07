using ICities;
using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // Runs when a save/map/asset is loaded and unloaded. Creates the options
    // window and its toggle button on load, tears both down on unload.
    //
    // This is the single LoadingExtensionBase for the mod. Do not add a
    // second one — the game instantiates every LoadingExtensionBase in the
    // assembly for each enabled IUserMod, so a second one here would spawn
    // duplicate panels and buttons and clobber OptionsPanel.Instance.
    public class Loading : LoadingExtensionBase
    {
        private OptionsPanel _panel;
        private OptionsToggleButton _toggleButton;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame)
                return;

            Debug.Log("[MyFirstMod] Level loaded - mod is active.");

            UIView view = UIView.GetAView();
            _panel = (OptionsPanel)view.AddUIComponent(typeof(OptionsPanel));
            _toggleButton = (OptionsToggleButton)view.AddUIComponent(typeof(OptionsToggleButton));

            Debug.Log("[MyFirstMod] Click the coin icon (top-left) to open the options window.");
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();

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
