using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // The toolbar icon that opens the window. It is always on screen, so it also
    // delivers alerts to the Chirper (WO-38) whether or not the window is open.
    public class BondToggleButton : UIPanel
    {
        private UIButton _button;
        private int _lastAlert;

        public override void Start()
        {
            base.Start();

            _button = AddUIComponent<UIButton>();
            _button.size = new Vector2(36f, 36f);
            _button.relativePosition = Vector3.zero;
            _button.normalBgSprite = "InfoIconLevel";
            _button.hoveredBgSprite = "InfoIconLevelHovered";
            _button.pressedBgSprite = "InfoIconLevelPressed";
            _button.tooltip = "Municipal Bond Market (Shift+B)";
            _button.eventClick += OnToggleClick;

            absolutePosition = new Vector3(60f, 6f);
            size = new Vector2(36f, 36f);
        }

        public override void Update()
        {
            base.Update();
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;
            Alert[] alerts = engine.Snapshot.Alerts;
            for (int i = 0; i < alerts.Length; i++)
            {
                if (alerts[i].Sequence <= _lastAlert) continue;
                _lastAlert = alerts[i].Sequence;
                ChirperBridge.Post(alerts[i].Text);
            }
        }

        private void OnToggleClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (BondMarketWindow.Instance != null)
                BondMarketWindow.Instance.Toggle();
        }
    }
}
