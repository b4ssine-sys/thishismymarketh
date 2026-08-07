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
        private UILabel _portfolioValueLabel;
        private UITextField _customQtyInput;
        private UITextField _customExpiryInput;

        private UIButton _btnQty1;
        private UIButton _btnQty10;
        private UIButton _btnQty100;

        private UIButton _btnExp7;
        private UIButton _btnExp14;
        private UIButton _btnExp30;
        private UIButton _btnExp60;

        private const string UnderlyingId = "Greasy Gasoline";
        private const float SampleSpot = 128f;
        private const float Vol = 0.38f;

        private int _selectedQuantity = 1;
        private int _selectedExpiryDays = 30;
        // Positive = long (bought) calls held, negative = short (written) calls owed.
        private int _ownedContractsCount = 0;
        private float _totalPremiumPaid = 0f;   // net premium paid (positive) or received (negative) for the open position
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

            width = 620f;
            height = 440f;
            // Centered on screen instead of anchored near the toggle button.
            relativePosition = new Vector3((view.fixedWidth - width) / 2f, (view.fixedHeight - height) / 2f);
            canFocus = true;
            isInteractive = true;

            // Starts hidden; click the toggle icon to flip display visibility open
            isVisible = false;

            UILabel title = CreateLabel(16f, 16f, 1.3f);
            title.text = "Options Market - Greasy Gasoline";

            _spotLabel = CreateLabel(16f, 54f, 1.05f);

            UILabel qtyTitle = CreateLabel(16f, 92f, 1.0f);
            qtyTitle.text = "Select Quantity:";

            _btnQty1 = CreateQtyButton("1", 150f, 88f, 1);
            _btnQty10 = CreateQtyButton("10", 205f, 88f, 10);
            _btnQty100 = CreateQtyButton("100", 260f, 88f, 100);

            UILabel customXLabel = CreateLabel(335f, 92f, 1.0f);
            customXLabel.text = "Custom (X):";

            _customQtyInput = AddUIComponent<UITextField>();
            _customQtyInput.atlas = atlas;
            _customQtyInput.font = view.defaultFont;
            _customQtyInput.size = new Vector2(70f, 26f);
            _customQtyInput.relativePosition = new Vector3(440f, 88f);
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

            UILabel expiryTitle = CreateLabel(16f, 130f, 1.0f);
            expiryTitle.text = "Contract Length (days):";

            _btnExp7 = CreateExpiryButton("7", 230f, 126f, 7);
            _btnExp14 = CreateExpiryButton("14", 285f, 126f, 14);
            _btnExp30 = CreateExpiryButton("30", 340f, 126f, 30);
            _btnExp60 = CreateExpiryButton("60", 395f, 126f, 60);

            UILabel customExpiryLabel = CreateLabel(460f, 130f, 1.0f);
            customExpiryLabel.text = "Custom:";

            _customExpiryInput = AddUIComponent<UITextField>();
            _customExpiryInput.atlas = atlas;
            _customExpiryInput.font = view.defaultFont;
            _customExpiryInput.size = new Vector2(70f, 26f);
            _customExpiryInput.relativePosition = new Vector3(530f, 126f);
            _customExpiryInput.normalBgSprite = "TextFieldPanel";
            _customExpiryInput.hoveredBgSprite = "TextFieldPanelHovered";
            _customExpiryInput.focusedBgSprite = "TextFieldPanelFocused";
            _customExpiryInput.text = "30";
            _customExpiryInput.numericalOnly = true;
            _customExpiryInput.textColor = new Color32(0, 0, 0, 255);
            _customExpiryInput.eventTextChanged += delegate (UIComponent component, string value)
            {
                int val;
                if (int.TryParse(value, out val) && val > 0)
                {
                    _selectedExpiryDays = val;
                    ResetExpiryButtonColors();
                }
            };

            _optionRowLabel = CreateLabel(16f, 178f, 1.05f);

            UIButton buyBtn = AddUIComponent<UIButton>();
            buyBtn.atlas = atlas;
            buyBtn.font = view.defaultFont;
            buyBtn.text = "Buy Call";
            buyBtn.textScale = 1f;
            buyBtn.width = 130f;
            buyBtn.height = 32f;
            buyBtn.relativePosition = new Vector3(16f, 222f);
            buyBtn.normalBgSprite = "ButtonMenu";
            buyBtn.hoveredBgSprite = "ButtonMenuHovered";
            buyBtn.pressedBgSprite = "ButtonMenuPressed";
            buyBtn.eventClick += delegate { HandleTransaction(1); };

            UIButton sellBtn = AddUIComponent<UIButton>();
            sellBtn.atlas = atlas;
            sellBtn.font = view.defaultFont;
            sellBtn.text = "Sell Call";
            sellBtn.textScale = 1f;
            sellBtn.width = 130f;
            sellBtn.height = 32f;
            sellBtn.relativePosition = new Vector3(160f, 222f);
            sellBtn.normalBgSprite = "ButtonMenu";
            sellBtn.hoveredBgSprite = "ButtonMenuHovered";
            sellBtn.pressedBgSprite = "ButtonMenuPressed";
            sellBtn.eventClick += delegate { HandleTransaction(-1); };

            UILabel sellHint = CreateLabel(16f, 260f, 0.8f);
            sellHint.text = "(Selling beyond what you hold writes/shorts new calls)";
            sellHint.textColor = new Color32(180, 180, 180, 255);

            _contractsLabel = CreateLabel(16f, 290f, 1.05f);
            _contractsLabel.textColor = new Color32(140, 230, 140, 255);

            _portfolioValueLabel = CreateLabel(16f, 318f, 0.95f);
            _portfolioValueLabel.autoSize = false;
            _portfolioValueLabel.wordWrap = true;
            _portfolioValueLabel.width = width - 32f;

            UIButton close = AddUIComponent<UIButton>();
            close.atlas = atlas;
            close.font = view.defaultFont;
            close.text = "Close";
            close.textScale = 1f;
            close.width = 110f;
            close.height = 32f;
            close.relativePosition = new Vector3(16f, height - 50f);
            close.normalBgSprite = "ButtonMenu";
            close.hoveredBgSprite = "ButtonMenuHovered";
            close.pressedBgSprite = "ButtonMenuPressed";
            close.eventClick += delegate { isVisible = false; };

            UpdateButtonSelectionState();
            UpdateExpiryButtonSelectionState();
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
            btn.textScale = 0.9f;
            btn.width = 50f;
            btn.height = 26f;
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

        private UIButton CreateExpiryButton(string text, float x, float y, int days)
        {
            UIButton btn = AddUIComponent<UIButton>();
            btn.atlas = atlas;
            btn.font = UIView.GetAView().defaultFont;
            btn.text = text;
            btn.textScale = 0.9f;
            btn.width = 50f;
            btn.height = 26f;
            btn.relativePosition = new Vector3(x, y);
            btn.normalBgSprite = "ButtonMenu";
            btn.hoveredBgSprite = "ButtonMenuHovered";
            btn.pressedBgSprite = "ButtonMenuPressed";

            btn.eventClick += delegate (UIComponent component, UIMouseEventParameter param)
            {
                _selectedExpiryDays = days;
                _customExpiryInput.text = days.ToString();
                UpdateExpiryButtonSelectionState();
            };
            return btn;
        }

        private void ResetExpiryButtonColors()
        {
            _btnExp7.color = new Color32(255, 255, 255, 255);
            _btnExp14.color = new Color32(255, 255, 255, 255);
            _btnExp30.color = new Color32(255, 255, 255, 255);
            _btnExp60.color = new Color32(255, 255, 255, 255);
        }

        private void UpdateExpiryButtonSelectionState()
        {
            ResetExpiryButtonColors();
            if (_selectedExpiryDays == 7) _btnExp7.color = new Color32(100, 200, 255, 255);
            else if (_selectedExpiryDays == 14) _btnExp14.color = new Color32(100, 200, 255, 255);
            else if (_selectedExpiryDays == 30) _btnExp30.color = new Color32(100, 200, 255, 255);
            else if (_selectedExpiryDays == 60) _btnExp60.color = new Color32(100, 200, 255, 255);
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
            // No ownership guard here: selling beyond what you currently hold
            // writes (shorts) new calls rather than just closing a long position.

            long totalCostScaled = (long)(_lastPremiumCalculated * 100f) * _selectedQuantity;
            // Clamp before narrowing to int so an unusually large premium * quantity
            // can't wrap around into a negative amount and hand out free cash.
            totalCostScaled = Math.Max(int.MinValue, Math.Min(int.MaxValue, totalCostScaled));

            if (EconomyManager.instance == null)
            {
                // No live economy to charge against (e.g. testing outside a loaded
                // city). Track the position without a cash transfer instead of
                // silently swallowing whatever error comes back from AddResource.
                ApplyPositionChange(direction);
                Debug.LogWarning("[OptionsMarket] EconomyManager unavailable; position tracked without a cash transfer.");
                Refresh();
                return;
            }

            if (direction > 0)
            {
                EconomyManager.instance.AddResource(EconomyManager.Resource.PublicIncome, (int)(-totalCostScaled), ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
            }
            else
            {
                EconomyManager.instance.AddResource(EconomyManager.Resource.PublicIncome, (int)(totalCostScaled), ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
            }

            ApplyPositionChange(direction);
            Refresh();
        }

        // Tracks quantity held (positive = long, negative = short) and cost
        // basis so Refresh() can show a live mark-to-market value. Adding to
        // a position (buying while long/flat, or selling/writing while
        // short/flat) books it at the current premium. Reducing a position
        // realizes against that position's own average premium instead of
        // the current quote, so the remaining open quantity keeps an
        // accurate cost basis. Crossing through zero splits into a close of
        // the old side plus a fresh open on the new side.
        private void ApplyPositionChange(int direction)
        {
            int qtyDelta = direction * _selectedQuantity;
            bool sameSideOrFlat = _ownedContractsCount == 0 || Math.Sign(_ownedContractsCount) == Math.Sign(qtyDelta);

            if (sameSideOrFlat)
            {
                _totalPremiumPaid += qtyDelta * _lastPremiumCalculated;
                _ownedContractsCount += qtyDelta;
            }
            else
            {
                float avgPremium = _totalPremiumPaid / _ownedContractsCount;
                int closingAmount = Math.Min(Math.Abs(qtyDelta), Math.Abs(_ownedContractsCount));
                int closingSigned = Math.Sign(qtyDelta) * closingAmount;

                _totalPremiumPaid += closingSigned * avgPremium;
                _ownedContractsCount += closingSigned;

                int remainder = qtyDelta - closingSigned;
                if (remainder != 0)
                {
                    _totalPremiumPaid += remainder * _lastPremiumCalculated;
                    _ownedContractsCount += remainder;
                }
            }

            if (_ownedContractsCount == 0)
            {
                _totalPremiumPaid = 0f;
            }
        }

        public void Refresh()
        {
            bool live;
            float spot = PriceFeed.GetSpot(UnderlyingId, SampleSpot, out live);
            int day = CurrentDay();
            int currentDay = day > _selectedExpiryDays ? _selectedExpiryDays : day;
            _spotLabel.text = "Underlying: " + UnderlyingId + " Price (Spot): ₡" + spot.ToString("0.00")
            + (live ? " (LIVE)" : " (SAMPLE)");
            float strike = (float)Math.Round(spot, 2);
            OptionContract c = new OptionContract(UnderlyingId, OptionKind.Call, strike, _selectedExpiryDays);
            _lastPremiumCalculated = OptionPricing.Premium(c, spot, Vol, 0f, currentDay);
            _optionRowLabel.text = string.Format("TYPE: CALL | STRIKE: ₡{0:0.00} | EXPIRY: {1}d | PREMIUM: ₡{2:0.00}", strike, _selectedExpiryDays, _lastPremiumCalculated);

            if (_ownedContractsCount >= 0)
            {
                _contractsLabel.text = "YOUR ACTIVE PORTFOLIO: " + _ownedContractsCount + " Greasy Gasoline Call Contracts Held (LONG)";
                _contractsLabel.textColor = new Color32(140, 230, 140, 255);
            }
            else
            {
                _contractsLabel.text = "YOUR ACTIVE PORTFOLIO: " + Math.Abs(_ownedContractsCount) + " Greasy Gasoline Call Contracts Written (SHORT)";
                _contractsLabel.textColor = new Color32(230, 180, 120, 255);
            }

            float liveValue = _ownedContractsCount * _lastPremiumCalculated;
            float unrealizedPL = liveValue - _totalPremiumPaid;
            _portfolioValueLabel.text = string.Format(
                "LIVE VALUE: ₡{0:0.00} | COST BASIS: ₡{1:0.00} | UNREALIZED: {2}₡{3:0.00}",
                liveValue, _totalPremiumPaid, unrealizedPL >= 0 ? "+" : "-", Math.Abs(unrealizedPL));
            _portfolioValueLabel.textColor = unrealizedPL >= 0
                ? new Color32(140, 230, 140, 255)
                : new Color32(230, 120, 120, 255);

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
