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

        private const float PANEL_PADDING = 16f;
        private static readonly Color32 COLOR_LONG = new Color32(140, 230, 140, 255);
        private static readonly Color32 COLOR_SHORT = new Color32(230, 180, 120, 255);
        private static readonly Color32 COLOR_NEUTRAL = new Color32(180, 180, 180, 255);
        private static readonly Color32 COLOR_LOSS = new Color32(230, 120, 120, 255);
        private static readonly Color32 COLOR_SELECTED = new Color32(100, 200, 255, 255);
        private static readonly Color32 COLOR_WHITE = new Color32(255, 255, 255, 255);
        private static readonly Color32 COLOR_TEXT = new Color32(240, 240, 240, 255);

        // A single open position on the current underlying. Positive
        // ContractsHeld = long (bought), negative = short (written). The
        // strike/expiry are locked in when the position is opened from flat
        // (or re-locked on a flip through zero) so an existing position is
        // always priced against the actual contract it was entered at,
        // instead of a constantly-moving fresh ATM quote.
        private struct Position
        {
            public int ContractsHeld;
            public float StrikePrice;
            public int ExpiryDays;
        }

        private Position _currentPosition;

        private UILabel _titleLabel;
        private UILabel _spotLabel;
        private UILabel _qtyTitleLabel;
        private UILabel _customQtyLabel;
        private UILabel _expiryTitleLabel;
        private UILabel _customExpiryLabel;
        private UILabel _optionRowLabel;
        private UILabel _sellHintLabel;
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

        private UIButton _buyBtn;
        private UIButton _sellBtn;
        private UIButton _closeBtn;

        private const string UnderlyingId = "Greasy Gasoline";
        private const float SampleSpot = 128f;
        private const float Vol = 0.38f;

        private int _selectedQuantity = 1;
        private int _selectedExpiryDays = 30;
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
            SetupUI();
        }

        private void SetupUI()
        {
            UIView view = UIView.GetAView();
            atlas = view.defaultAtlas;
            backgroundSprite = "MenuPanel2";
            color = COLOR_WHITE;

            width = 620f;
            height = 460f;
            // Centered on screen instead of anchored near the toggle button.
            relativePosition = new Vector3((view.fixedWidth - width) / 2f, (view.fixedHeight - height) / 2f);
            canFocus = true;
            isInteractive = true;

            // Starts hidden; click the toggle icon to flip display visibility open
            isVisible = false;

            _titleLabel = CreateLabel(1.3f);
            _titleLabel.text = "Options Market - Greasy Gasoline";

            _spotLabel = CreateLabel(1.05f);

            _qtyTitleLabel = CreateLabel(1.0f);
            _qtyTitleLabel.text = "Select Quantity:";

            _btnQty1 = CreateQtyButton("1", 1);
            _btnQty10 = CreateQtyButton("10", 10);
            _btnQty100 = CreateQtyButton("100", 100);

            _customQtyLabel = CreateLabel(1.0f);
            _customQtyLabel.text = "Custom (X):";

            _customQtyInput = AddUIComponent<UITextField>();
            _customQtyInput.atlas = atlas;
            _customQtyInput.font = view.defaultFont;
            _customQtyInput.size = new Vector2(70f, 26f);
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

            _expiryTitleLabel = CreateLabel(1.0f);
            _expiryTitleLabel.text = "Contract Length (days):";

            _btnExp7 = CreateExpiryButton("7", 7);
            _btnExp14 = CreateExpiryButton("14", 14);
            _btnExp30 = CreateExpiryButton("30", 30);
            _btnExp60 = CreateExpiryButton("60", 60);

            _customExpiryLabel = CreateLabel(1.0f);
            _customExpiryLabel.text = "Custom:";

            _customExpiryInput = AddUIComponent<UITextField>();
            _customExpiryInput.atlas = atlas;
            _customExpiryInput.font = view.defaultFont;
            _customExpiryInput.size = new Vector2(70f, 26f);
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

            _optionRowLabel = CreateLabel(1.05f);

            _buyBtn = CreateStandardButton("Buy Call", 130f, 32f);
            _buyBtn.eventClick += delegate { HandleTransaction(1); };

            _sellBtn = CreateStandardButton("Sell Call", 130f, 32f);
            _sellBtn.eventClick += delegate { HandleTransaction(-1); };

            _sellHintLabel = CreateLabel(0.8f);
            _sellHintLabel.text = "(Selling beyond what you hold writes/shorts new calls)";
            _sellHintLabel.textColor = COLOR_NEUTRAL;

            _contractsLabel = CreateLabel(1.05f);

            _portfolioValueLabel = CreateLabel(0.95f);

            _closeBtn = CreateStandardButton("Close", 110f, 32f);
            _closeBtn.eventClick += delegate { isVisible = false; };

            UpdateButtonSelectionState();
            UpdateExpiryButtonSelectionState();
            Refresh();
        }

        // Lays out every control top-to-bottom using each label's actual
        // measured height (instead of hand-picked y-offsets), so text that
        // wraps to an extra line pushes everything below it down rather than
        // overlapping or spilling past the panel edge.
        private void PerformAutoLayout()
        {
            float currentY = PANEL_PADDING;
            float contentWidth = width - (PANEL_PADDING * 2f);

            ConfigureLabelLayout(_titleLabel, currentY, contentWidth);
            currentY += _titleLabel.height + 10f;

            ConfigureLabelLayout(_spotLabel, currentY, contentWidth);
            currentY += _spotLabel.height + 14f;

            _qtyTitleLabel.relativePosition = new Vector3(PANEL_PADDING, currentY + 4f);
            _btnQty1.relativePosition = new Vector3(150f, currentY);
            _btnQty10.relativePosition = new Vector3(205f, currentY);
            _btnQty100.relativePosition = new Vector3(260f, currentY);
            _customQtyLabel.relativePosition = new Vector3(335f, currentY + 4f);
            _customQtyInput.relativePosition = new Vector3(440f, currentY);
            currentY += 34f;

            _expiryTitleLabel.relativePosition = new Vector3(PANEL_PADDING, currentY + 4f);
            _btnExp7.relativePosition = new Vector3(230f, currentY);
            _btnExp14.relativePosition = new Vector3(285f, currentY);
            _btnExp30.relativePosition = new Vector3(340f, currentY);
            _btnExp60.relativePosition = new Vector3(395f, currentY);
            _customExpiryLabel.relativePosition = new Vector3(460f, currentY + 4f);
            _customExpiryInput.relativePosition = new Vector3(530f, currentY);
            currentY += 38f;

            ConfigureLabelLayout(_optionRowLabel, currentY, contentWidth);
            currentY += _optionRowLabel.height + 12f;

            _buyBtn.relativePosition = new Vector3(PANEL_PADDING, currentY);
            _sellBtn.relativePosition = new Vector3(PANEL_PADDING + 144f, currentY);
            currentY += 40f;

            ConfigureLabelLayout(_sellHintLabel, currentY, contentWidth);
            currentY += _sellHintLabel.height + 12f;

            ConfigureLabelLayout(_contractsLabel, currentY, contentWidth);
            currentY += _contractsLabel.height + 12f;

            ConfigureLabelLayout(_portfolioValueLabel, currentY, contentWidth);

            _closeBtn.relativePosition = new Vector3(PANEL_PADDING, height - 48f);
        }

        private void ConfigureLabelLayout(UILabel label, float y, float targetWidth)
        {
            label.relativePosition = new Vector3(PANEL_PADDING, y);
            label.autoSize = false;
            label.wordWrap = true;
            label.width = targetWidth;
        }

        private UILabel CreateLabel(float scale)
        {
            UILabel l = AddUIComponent<UILabel>();
            l.font = UIView.GetAView().defaultFont;
            l.textColor = COLOR_TEXT;
            l.textScale = scale;
            return l;
        }

        private UIButton CreateStandardButton(string text, float btnWidth, float btnHeight)
        {
            UIButton btn = AddUIComponent<UIButton>();
            btn.atlas = atlas;
            btn.font = UIView.GetAView().defaultFont;
            btn.text = text;
            btn.textScale = 1f;
            btn.width = btnWidth;
            btn.height = btnHeight;
            btn.normalBgSprite = "ButtonMenu";
            btn.hoveredBgSprite = "ButtonMenuHovered";
            btn.pressedBgSprite = "ButtonMenuPressed";
            return btn;
        }

        private UIButton CreateQtyButton(string text, int amount)
        {
            UIButton btn = CreateStandardButton(text, 50f, 26f);
            btn.textScale = 0.9f;
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
            _btnQty1.color = COLOR_WHITE;
            _btnQty10.color = COLOR_WHITE;
            _btnQty100.color = COLOR_WHITE;
        }

        private void UpdateButtonSelectionState()
        {
            ResetQtyButtonColors();
            if (_selectedQuantity == 1) _btnQty1.color = COLOR_SELECTED;
            else if (_selectedQuantity == 10) _btnQty10.color = COLOR_SELECTED;
            else if (_selectedQuantity == 100) _btnQty100.color = COLOR_SELECTED;
        }

        private UIButton CreateExpiryButton(string text, int days)
        {
            UIButton btn = CreateStandardButton(text, 50f, 26f);
            btn.textScale = 0.9f;
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
            _btnExp7.color = COLOR_WHITE;
            _btnExp14.color = COLOR_WHITE;
            _btnExp30.color = COLOR_WHITE;
            _btnExp60.color = COLOR_WHITE;
        }

        private void UpdateExpiryButtonSelectionState()
        {
            ResetExpiryButtonColors();
            if (_selectedExpiryDays == 7) _btnExp7.color = COLOR_SELECTED;
            else if (_selectedExpiryDays == 14) _btnExp14.color = COLOR_SELECTED;
            else if (_selectedExpiryDays == 30) _btnExp30.color = COLOR_SELECTED;
            else if (_selectedExpiryDays == 60) _btnExp60.color = COLOR_SELECTED;
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
            bool live;
            float currentSpot = PriceFeed.GetSpot(UnderlyingId, SampleSpot, out live);

            // Lock in the execution strike/expiry when opening a brand-new
            // position from flat, so the whole position prices consistently
            // against the contract it was actually entered at.
            if (_currentPosition.ContractsHeld == 0)
            {
                _currentPosition.StrikePrice = (float)Math.Round(currentSpot, 2);
                _currentPosition.ExpiryDays = _selectedExpiryDays;
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
                ApplyPositionChange(direction, currentSpot);
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

            ApplyPositionChange(direction, currentSpot);
            Refresh();
        }

        // Adding to a position (buying while long/flat, or selling/writing
        // while short/flat) books it at the current premium. Reducing a
        // position realizes against that position's own average premium
        // instead of the current quote, so the remaining open quantity keeps
        // an accurate cost basis. Crossing through zero closes the old side
        // and re-locks a fresh strike/expiry to open the new side.
        private void ApplyPositionChange(int direction, float currentSpot)
        {
            int qtyDelta = direction * _selectedQuantity;
            bool sameSideOrFlat = _currentPosition.ContractsHeld == 0
                || Math.Sign(_currentPosition.ContractsHeld) == Math.Sign(qtyDelta);

            if (sameSideOrFlat)
            {
                _totalPremiumPaid += qtyDelta * _lastPremiumCalculated;
                _currentPosition.ContractsHeld += qtyDelta;
            }
            else
            {
                float avgPremium = _totalPremiumPaid / _currentPosition.ContractsHeld;
                int closingAmount = Math.Min(Math.Abs(qtyDelta), Math.Abs(_currentPosition.ContractsHeld));
                int closingSigned = Math.Sign(qtyDelta) * closingAmount;

                _totalPremiumPaid += closingSigned * avgPremium;
                _currentPosition.ContractsHeld += closingSigned;

                int remainder = qtyDelta - closingSigned;
                if (remainder != 0)
                {
                    // Flipped through zero - the leftover opens a fresh
                    // position on the other side at today's strike/expiry.
                    _currentPosition.StrikePrice = (float)Math.Round(currentSpot, 2);
                    _currentPosition.ExpiryDays = _selectedExpiryDays;
                    _currentPosition.ContractsHeld = remainder;
                    _totalPremiumPaid = remainder * _lastPremiumCalculated;
                }
            }

            if (_currentPosition.ContractsHeld == 0)
            {
                _totalPremiumPaid = 0f;
            }
        }

        public void Refresh()
        {
            bool live;
            float spot = PriceFeed.GetSpot(UnderlyingId, SampleSpot, out live);
            int currentDay = Math.Min(CurrentDay(), _selectedExpiryDays);

            _spotLabel.text = string.Format("Underlying: {0} | Spot: ₡{1:0.00} {2}",
                UnderlyingId, spot, live ? "(LIVE)" : "(SAMPLE)");

            // Price a held position against its own locked-in contract;
            // otherwise quote what a brand-new trade would get today.
            float activeStrike = _currentPosition.ContractsHeld != 0
                ? _currentPosition.StrikePrice
                : (float)Math.Round(spot, 2);
            int activeExpiry = _currentPosition.ContractsHeld != 0
                ? _currentPosition.ExpiryDays
                : _selectedExpiryDays;

            OptionContract contract = new OptionContract(UnderlyingId, OptionKind.Call, activeStrike, activeExpiry);
            _lastPremiumCalculated = OptionPricing.Premium(contract, spot, Vol, 0f, currentDay);

            _optionRowLabel.text = string.Format("TYPE: CALL | STRIKE: ₡{0:0.00} | EXPIRY: {1}d | PREMIUM: ₡{2:0.00}",
                activeStrike, activeExpiry, _lastPremiumCalculated);

            int totalQuantity = Math.Abs(_currentPosition.ContractsHeld);
            if (_currentPosition.ContractsHeld > 0)
            {
                _contractsLabel.text = string.Format("ACTIVE PORTFOLIO: {0} Greasy Gasoline Call Contracts Held (LONG) @ Strike ₡{1:0.00}",
                    totalQuantity, _currentPosition.StrikePrice);
                _contractsLabel.textColor = COLOR_LONG;
            }
            else if (_currentPosition.ContractsHeld < 0)
            {
                _contractsLabel.text = string.Format("ACTIVE PORTFOLIO: {0} Greasy Gasoline Call Contracts Written (SHORT) @ Strike ₡{1:0.00}",
                    totalQuantity, _currentPosition.StrikePrice);
                _contractsLabel.textColor = COLOR_SHORT;
            }
            else
            {
                _contractsLabel.text = "ACTIVE PORTFOLIO: No Open Options Positions";
                _contractsLabel.textColor = COLOR_NEUTRAL;
            }

            float liveValue = _currentPosition.ContractsHeld * _lastPremiumCalculated;
            float unrealizedPL = liveValue - _totalPremiumPaid;
            _portfolioValueLabel.text = string.Format(
                "LIVE VALUE: ₡{0:0.00} | COST BASIS: ₡{1:0.00} | UNREALIZED: {2}₡{3:0.00}",
                liveValue, _totalPremiumPaid, unrealizedPL >= 0 ? "+" : "-", Math.Abs(unrealizedPL));
            _portfolioValueLabel.textColor = unrealizedPL >= 0 ? COLOR_LONG : COLOR_LOSS;

            PerformAutoLayout();
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
