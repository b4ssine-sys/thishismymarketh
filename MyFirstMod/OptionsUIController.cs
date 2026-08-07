using System;
using ColossalFramework.UI;
using UnityEngine;
using MyFirstMod.Options;

namespace MyFirstMod
{
    // ON-SCREEN VISUAL TOGGLE BUTTON (small top-left icon)
    public class OptionsToggleButton : UIButton
    {
        public override void Start()
        {
            base.Start();

            UIView view = UIView.GetAView();
            this.atlas = view.defaultAtlas;
            this.font = view.defaultFont;

            this.width = 32f;
            this.height = 32f;

            // Top-left corner, stacked under the game's own small icon badges there.
            this.relativePosition = new Vector3(10f, 100f);

            this.normalBgSprite = "ButtonMenu";
            this.hoveredBgSprite = "ButtonMenuHovered";
            this.pressedBgSprite = "ButtonMenuPressed";

            // Money icon from the game's own atlas (used by the Economy info view).
            // If this renders as a blank square in your build, the sprite name
            // differs on your game version - tell me and I'll swap it.
            this.normalFgSprite = "InfoIconMoney";
            this.hoveredFgSprite = "InfoIconMoneyHovered";
            this.pressedFgSprite = "InfoIconMoneyPressed";

            this.eventClick += delegate (UIComponent component, UIMouseEventParameter param)
            {
                if (OptionsPanel.Instance != null)
                {
                    OptionsPanel.Instance.isVisible = !OptionsPanel.Instance.isVisible;
                    if (OptionsPanel.Instance.isVisible)
                    {
                        OptionsPanel.Instance.BringToFront();
                        OptionsPanel.Instance.Refresh();
                    }
                }
            };
        }
    }

    // MAIN TRANSACTION OPTIONS PANEL
    public class OptionsPanel : UIPanel
    {
        public static OptionsPanel Instance;

        private UILabel _spotLabel;
        private UILabel _optionRowLabel;
        private UILabel _contractsLabel;
        private UITextField _customQtyInput;

        private UIButton _btnQty1;
        private UIButton _btnQty10;
        private UIButton _btnQty100;

        private const string UnderlyingId = "Greasy Gasoline";
        private const float SampleSpot = 128f;
        private const float Vol = 0.38f;
        private const int ExpiryDays = 30;

        private int _selectedQuantity = 1;
        private int _ownedContractsCount = 0;
        private float _lastPremiumCalculated = 0f;

        private float _timer = 0f;
        private const float UpdateInterval = 5.0f; // 5-second automated refresh cycle

        private DateTime _start;
        private bool _haveStart;

        public override void Start()
        {
            base.Start();
            Instance = this;

            UIView view = UIView.GetAView();
            atlas = view.defaultAtlas;
            backgroundSprite = "MenuPanel2";
            color = new Color32(255, 255, 255, 255);

            width = 480f;
            height = 320f;
            relativePosition = new Vector3(80f, 120f); // Spawns near the button frame
            canFocus = true;
            isInteractive = true;

            // Starts hidden; click the "MKT" screen button to flip display visibility open
            isVisible = false;

            UILabel title = CreateLabel(16f, 12f, 1.1f);
            title.text = "Options Market - Greasy Gasoline";

            _spotLabel = CreateLabel(16f, 42f, 0.9f);

            UILabel qtyTitle = CreateLabel(16f, 75f, 0.85f);
            qtyTitle.text = "Select Quantity:";

            _btnQty1 = CreateQtyButton("1", 130f, 72f, 1);
            _btnQty10 = CreateQtyButton("10", 175f, 72f, 10);
            _btnQty100 = CreateQtyButton("100", 225f, 72f, 100);

            UILabel customXLabel = CreateLabel(285f, 75f, 0.85f);
            customXLabel.text = "Custom (X):";

            _customQtyInput = AddUIComponent<UITextField>();
            _customQtyInput.atlas = atlas;
            _customQtyInput.font = view.defaultFont;
            _customQtyInput.size = new Vector2(60f, 22f);
            _customQtyInput.relativePosition = new Vector3(375f, 72f);
            _customQtyInput.normalBgSprite = "TextFieldPanel";
            _customQtyInput.hoveredBgSprite = "TextFieldPanelHovered";
            _customQtyInput.focusedBgSprite = "TextFieldPanelFocused";
            _customQtyInput.text = "1";
            _customQtyInput.numericalOnly = true;
            _customQtyInput.textColor = new Color32(0, 0, 0, 255);
            _customQtyInput.eventTextChanged += delegate (UIComponent component, string value)
            {
                int val;
                if (int.TryParse(value, out val) && val > 0)
                {
                    _selectedQuantity = val;
                    ResetQtyButtonColors();
                }
            };

            _optionRowLabel = CreateLabel(16f, 125f, 0.9f);

            UIButton buyBtn = AddUIComponent<UIButton>();
            buyBtn.atlas = atlas;
            buyBtn.font = view.defaultFont;
            buyBtn.text = "Buy Call";
            buyBtn.width = 100f;
            buyBtn.height = 26f;
            buyBtn.relativePosition = new Vector3(16f, 165f);
            buyBtn.normalBgSprite = "ButtonMenu";
            buyBtn.hoveredBgSprite = "ButtonMenuHovered";
            buyBtn.pressedBgSprite = "ButtonMenuPressed";
            buyBtn.eventClick += delegate { HandleTransaction(1); };

            UIButton sellBtn = AddUIComponent<UIButton>();
            sellBtn.atlas = atlas;
            sellBtn.font = view.defaultFont;
            sellBtn.text = "Sell Call";
            sellBtn.width = 100f;
            sellBtn.height = 26f;
            sellBtn.relativePosition = new Vector3(130f, 165f);
            sellBtn.normalBgSprite = "ButtonMenu";
            sellBtn.hoveredBgSprite = "ButtonMenuHovered";
            sellBtn.pressedBgSprite = "ButtonMenuPressed";
            sellBtn.eventClick += delegate { HandleTransaction(-1); };

            _contractsLabel = CreateLabel(16f, 215f, 0.95f);
            _contractsLabel.textColor = new Color32(140, 230, 140, 255);

            UIButton close = AddUIComponent<UIButton>();
            close.atlas = atlas;
            close.font = view.defaultFont;
            close.text = "Close";
            close.width = 90f;
            close.height = 28f;
            close.relativePosition = new Vector3(16f, height - 45f);
            close.normalBgSprite = "ButtonMenu";
            close.hoveredBgSprite = "ButtonMenuHovered";
            close.pressedBgSprite = "ButtonMenuPressed";
            close.eventClick += delegate { isVisible = false; };

            UpdateButtonSelectionState();
            Refresh();
        }

        private UILabel CreateLabel(float x, float y, float scale)
        {
            UILabel l = AddUIComponent<UILabel>();
            l.font = UIView.GetAView().defaultFont;
            l.textColor = new Color32(240, 240, 240, 255);
            l.textScale = scale;
            l.relativePosition = new Vector3(x, y);
            return l;
        }

        private UIButton CreateQtyButton(string text, float x, float y, int amount)
        {
            UIButton btn = AddUIComponent<UIButton>();
            btn.atlas = atlas;
            btn.font = UIView.GetAView().defaultFont;
            btn.text = text;
            btn.textScale = 0.8f;
            btn.width = 40f;
            btn.height = 22f;
            btn.relativePosition = new Vector3(x, y);
            btn.normalBgSprite = "ButtonMenu";
            btn.hoveredBgSprite = "ButtonMenuHovered";
            btn.pressedBgSprite = "ButtonMenuPressed";

            btn.eventClick += delegate (UIComponent component, UIMouseEventParameter param)
            {
                _selectedQuantity = amount;
                _customQtyInput.text = amount.ToString();
                UpdateButtonSelectionState();
            };
            return btn;
        }

        private void ResetQtyButtonColors()
        {
            _btnQty1.color = new Color32(255, 255, 255, 255);
            _btnQty10.color = new Color32(255, 255, 255, 255);
            _btnQty100.color = new Color32(255, 255, 255, 255);
        }

        private void UpdateButtonSelectionState()
        {
            ResetQtyButtonColors();
            if (_selectedQuantity == 1) _btnQty1.color = new Color32(100, 200, 255, 255);
            else if (_selectedQuantity == 10) _btnQty10.color = new Color32(100, 200, 255, 255);
            else if (_selectedQuantity == 100) _btnQty100.color = new Color32(100, 200, 255, 255);

        }
        public override void Update()
        {
            base.Update();
            _timer += Time.deltaTime;
            if (_timer >= UpdateInterval)
            {
                _timer = 0f;
                Refresh();
            }
        }

        private void HandleTransaction(int direction)
        {
            if (direction < 0 && _ownedContractsCount < _selectedQuantity)
            {
                return;
            }

            long totalCostScaled = (long)(_lastPremiumCalculated * 100f) * _selectedQuantity;
            // Clamp before narrowing to int so an unusually large premium * quantity
            // can't wrap around into a negative amount and hand out free cash.
            totalCostScaled = Math.Max(int.MinValue, Math.Min(int.MaxValue, totalCostScaled));

            if (EconomyManager.instance == null)
            {
                // No live economy to charge against (e.g. testing outside a loaded
                // city). Track the position without a cash transfer instead of
                // silently swallowing whatever error comes back from AddResource.
                _ownedContractsCount += _selectedQuantity * direction;
                Debug.LogWarning("[OptionsMarket] EconomyManager unavailable; position tracked without a cash transfer.");
                Refresh();
                return;
            }

            if (direction > 0)
            {
                EconomyManager.instance.AddResource(EconomyManager.Resource.PublicIncome, (int)(-totalCostScaled), ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
                _ownedContractsCount += _selectedQuantity;
            }
            else
            {
                EconomyManager.instance.AddResource(EconomyManager.Resource.PublicIncome, (int)(totalCostScaled), ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
                _ownedContractsCount -= _selectedQuantity;
            }

            Refresh();
        }

        public void Refresh()
        {
            bool live;
            float spot = PriceFeed.GetSpot(UnderlyingId, SampleSpot, out live);
            int day = CurrentDay();
            int currentDay = day > ExpiryDays ? ExpiryDays : day;
            _spotLabel.text = "Underlying: " + UnderlyingId + " Price (Spot): $" + spot.ToString("0.00")
            + (live ? " (LIVE)" : " (SAMPLE)");
            float strike = (float)Math.Round(spot, 2);
            OptionContract c = new OptionContract(UnderlyingId, OptionKind.Call, strike, ExpiryDays);
            _lastPremiumCalculated = OptionPricing.Premium(c, spot, Vol, 0f, currentDay);
            _optionRowLabel.text = string.Format("TYPE: CALL | STRIKE: ${0:0.00} | PREMIUM: ${1:0.00}", strike, _lastPremiumCalculated);
            _contractsLabel.text = "YOUR ACTIVE PORTFOLIO: " + _ownedContractsCount + " Greasy Gasoline Call Contracts Held";
            BringToFront();
        }

        private int CurrentDay()
        {
            try
            {
                DateTime now = SimulationManager.instance.m_currentGameTime;
                if (!_haveStart) { _start = now; _haveStart = true; }
                return (int)(now - _start).TotalDays;
            }
            catch
            {
                return (int)(Time.realtimeSinceStartup / 20f);
            }
        }
    }
}
