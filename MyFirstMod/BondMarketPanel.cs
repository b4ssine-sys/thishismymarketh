using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    public class BondToggleButton : UIPanel
    {
        private UIButton _button;

        public override void Start()
        {
            base.Start();

            _button = AddUIComponent<UIButton>();
            _button.size = new Vector2(36f, 36f);
            _button.relativePosition = Vector3.zero;
            _button.normalBgSprite = "InfoIconLevel";
            _button.hoveredBgSprite = "InfoIconLevelHovered";
            _button.pressedBgSprite = "InfoIconLevelPressed";
            _button.tooltip = "Municipal Bond Market";
            _button.eventClick += OnToggleClick;

            absolutePosition = new Vector3(60f, 6f);
            size = new Vector2(36f, 36f);
        }

        private void OnToggleClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (BondMarketPanel.Instance != null)
                BondMarketPanel.Instance.Toggle();
        }
    }

    public class BondMarketPanel : UIPanel
    {
        public static BondMarketPanel Instance;

        private const float WIDTH = 800f;
        private const float HEIGHT = 520f;
        private const float HEADER_HEIGHT = 100f;
        private const float TAB_HEIGHT = 30f;
        private const float ROW_HEIGHT = 36f;
        private const int MAX_ROWS = 6;

        private UILabel _titleLabel;
        private UIButton _closeButton;
        private UILabel _summaryLabel;

        private UIButton _marketTabBtn;
        private UIButton _portfolioTabBtn;
        private UIButton _cityDebtTabBtn;
        private UIButton _sellAllBtn;
        private UIButton _buy1MBtn;
        private UIButton _buy10MBtn;
        private UIButton _buy1BBtn;
        private UIButton _pay25Btn;
        private UIButton _pay50Btn;
        private int _activeTab;

        private UIPanel _listPanel;
        private UILabel[] _infoLabels;
        private UILabel[] _priceLabels;
        private UIButton[] _actionButtons;
        private UILabel _scrollHintLabel;

        private UILabel _footerLabel;

        private float _refreshTimer;
        private const float REFRESH_INTERVAL = 4f;

        private int _scrollOffset;

        private readonly List<Bond> _cachedBonds = new List<Bond>();
        private readonly List<float> _cachedPrices = new List<float>();
        private readonly List<InterestRateSwap> _cachedSwaps = new List<InterestRateSwap>();
        private readonly List<Bond> _cachedIssuedBonds = new List<Bond>();

        private UIButton _hedgingTabBtn;
        private UIButton _positionsTabBtn;
        private UIButton _autoHedgeBtn;
        private UIButton _sell25SwapsBtn;
        private UIButton _sell50SwapsBtn;
        private UIButton _exitAllSwapsBtn;

        public override void Start()
        {
            base.Start();
            Instance = this;

            backgroundSprite = "MenuPanel2";
            size = new Vector2(WIDTH, HEIGHT);
            UIView view = GetUIView();
            if (view != null)
            {
                absolutePosition = new Vector3(
                    (view.fixedWidth - WIDTH) / 2f,
                    (view.fixedHeight - HEIGHT) / 2f
                );
            }

            isVisible = false;
            canFocus = true;
            isInteractive = true;

            CreateTitleBar();
            CreateSummary();
            CreateTabs();
            CreateBondList();
            CreateFooter();
        }

        private void CreateTitleBar()
        {
            UIPanel titleBar = AddUIComponent<UIPanel>();
            titleBar.size = new Vector2(WIDTH, 40f);
            titleBar.relativePosition = Vector3.zero;

            UIDragHandle drag = titleBar.AddUIComponent<UIDragHandle>();
            drag.size = titleBar.size;
            drag.relativePosition = Vector3.zero;
            drag.target = this;

            _titleLabel = titleBar.AddUIComponent<UILabel>();
            _titleLabel.text = "Municipal Bond Market";
            _titleLabel.textScale = 1.1f;
            _titleLabel.relativePosition = new Vector3(12f, 10f);

            _closeButton = titleBar.AddUIComponent<UIButton>();
            _closeButton.size = new Vector2(32f, 32f);
            _closeButton.relativePosition = new Vector3(WIDTH - 40f, 4f);
            _closeButton.normalBgSprite = "buttonclose";
            _closeButton.hoveredBgSprite = "buttonclosehover";
            _closeButton.pressedBgSprite = "buttonclosepressed";
            _closeButton.eventClick += OnCloseClick;
        }

        private void CreateSummary()
        {
            _summaryLabel = AddUIComponent<UILabel>();
            _summaryLabel.autoSize = false;
            _summaryLabel.size = new Vector2(WIDTH - 24f, HEADER_HEIGHT - 44f);
            _summaryLabel.relativePosition = new Vector3(12f, 42f);
            _summaryLabel.textScale = 0.8f;
            _summaryLabel.wordWrap = true;
            _summaryLabel.text = "Loading financial data...";
        }

        private void CreateTabs()
        {
            float tabY = HEADER_HEIGHT + 2f;
            float tabW = 88f;
            float gap = 4f;

            _marketTabBtn = AddUIComponent<UIButton>();
            _marketTabBtn.size = new Vector2(tabW, TAB_HEIGHT);
            _marketTabBtn.relativePosition = new Vector3(12f, tabY);
            _marketTabBtn.text = "Market";
            _marketTabBtn.textScale = 0.85f;
            _marketTabBtn.normalBgSprite = "ButtonMenu";
            _marketTabBtn.hoveredBgSprite = "ButtonMenuHovered";
            _marketTabBtn.pressedBgSprite = "ButtonMenuPressed";
            _marketTabBtn.focusedBgSprite = "ButtonMenuFocused";
            _marketTabBtn.eventClick += OnMarketTab;

            _portfolioTabBtn = AddUIComponent<UIButton>();
            _portfolioTabBtn.size = new Vector2(tabW, TAB_HEIGHT);
            _portfolioTabBtn.relativePosition = new Vector3(12f + tabW + gap, tabY);
            _portfolioTabBtn.text = "Portfolio";
            _portfolioTabBtn.textScale = 0.85f;
            _portfolioTabBtn.normalBgSprite = "ButtonMenu";
            _portfolioTabBtn.hoveredBgSprite = "ButtonMenuHovered";
            _portfolioTabBtn.pressedBgSprite = "ButtonMenuPressed";
            _portfolioTabBtn.focusedBgSprite = "ButtonMenuFocused";
            _portfolioTabBtn.eventClick += OnPortfolioTab;

            _cityDebtTabBtn = AddUIComponent<UIButton>();
            _cityDebtTabBtn.size = new Vector2(tabW, TAB_HEIGHT);
            _cityDebtTabBtn.relativePosition = new Vector3(12f + (tabW + gap) * 2f, tabY);
            _cityDebtTabBtn.text = "Debt";
            _cityDebtTabBtn.textScale = 0.85f;
            _cityDebtTabBtn.normalBgSprite = "ButtonMenu";
            _cityDebtTabBtn.hoveredBgSprite = "ButtonMenuHovered";
            _cityDebtTabBtn.pressedBgSprite = "ButtonMenuPressed";
            _cityDebtTabBtn.focusedBgSprite = "ButtonMenuFocused";
            _cityDebtTabBtn.eventClick += OnCityDebtTab;

            _hedgingTabBtn = AddUIComponent<UIButton>();
            _hedgingTabBtn.size = new Vector2(tabW, TAB_HEIGHT);
            _hedgingTabBtn.relativePosition = new Vector3(12f + (tabW + gap) * 3f, tabY);
            _hedgingTabBtn.text = "Hedging";
            _hedgingTabBtn.textScale = 0.85f;
            _hedgingTabBtn.normalBgSprite = "ButtonMenu";
            _hedgingTabBtn.hoveredBgSprite = "ButtonMenuHovered";
            _hedgingTabBtn.pressedBgSprite = "ButtonMenuPressed";
            _hedgingTabBtn.focusedBgSprite = "ButtonMenuFocused";
            _hedgingTabBtn.eventClick += OnHedgingTab;

            _positionsTabBtn = AddUIComponent<UIButton>();
            _positionsTabBtn.size = new Vector2(tabW, TAB_HEIGHT);
            _positionsTabBtn.relativePosition = new Vector3(12f + (tabW + gap) * 4f, tabY);
            _positionsTabBtn.text = "Positions";
            _positionsTabBtn.textScale = 0.85f;
            _positionsTabBtn.normalBgSprite = "ButtonMenu";
            _positionsTabBtn.hoveredBgSprite = "ButtonMenuHovered";
            _positionsTabBtn.pressedBgSprite = "ButtonMenuPressed";
            _positionsTabBtn.focusedBgSprite = "ButtonMenuFocused";
            _positionsTabBtn.eventClick += OnPositionsTab;

            _sellAllBtn = AddUIComponent<UIButton>();
            _sellAllBtn.size = new Vector2(90f, TAB_HEIGHT);
            _sellAllBtn.relativePosition = new Vector3(WIDTH - 102f, tabY);
            _sellAllBtn.text = "Sell All";
            _sellAllBtn.textScale = 0.85f;
            _sellAllBtn.normalBgSprite = "ButtonMenu";
            _sellAllBtn.hoveredBgSprite = "ButtonMenuHovered";
            _sellAllBtn.pressedBgSprite = "ButtonMenuPressed";
            _sellAllBtn.disabledBgSprite = "ButtonMenuDisabled";
            _sellAllBtn.eventClick += OnSellAllClick;
            _sellAllBtn.isVisible = false;

            _buy1MBtn = AddUIComponent<UIButton>();
            _buy1MBtn.size = new Vector2(105f, TAB_HEIGHT);
            _buy1MBtn.relativePosition = new Vector3(WIDTH - 117f, tabY);
            _buy1MBtn.text = "10x 1M 5yr";
            _buy1MBtn.textScale = 0.75f;
            _buy1MBtn.normalBgSprite = "ButtonMenu";
            _buy1MBtn.hoveredBgSprite = "ButtonMenuHovered";
            _buy1MBtn.pressedBgSprite = "ButtonMenuPressed";
            _buy1MBtn.disabledBgSprite = "ButtonMenuDisabled";
            _buy1MBtn.eventClick += OnBuy1MClick;
            _buy1MBtn.isVisible = false;

            _buy10MBtn = AddUIComponent<UIButton>();
            _buy10MBtn.size = new Vector2(105f, TAB_HEIGHT);
            _buy10MBtn.relativePosition = new Vector3(WIDTH - 226f, tabY);
            _buy10MBtn.text = "10x 10M 5yr";
            _buy10MBtn.textScale = 0.75f;
            _buy10MBtn.normalBgSprite = "ButtonMenu";
            _buy10MBtn.hoveredBgSprite = "ButtonMenuHovered";
            _buy10MBtn.pressedBgSprite = "ButtonMenuPressed";
            _buy10MBtn.disabledBgSprite = "ButtonMenuDisabled";
            _buy10MBtn.eventClick += OnBuy10MClick;
            _buy10MBtn.isVisible = false;

            _buy1BBtn = AddUIComponent<UIButton>();
            _buy1BBtn.size = new Vector2(90f, TAB_HEIGHT);
            _buy1BBtn.relativePosition = new Vector3(WIDTH - 320f, tabY);
            _buy1BBtn.text = "Buy 1B 5yr";
            _buy1BBtn.textScale = 0.75f;
            _buy1BBtn.normalBgSprite = "ButtonMenu";
            _buy1BBtn.hoveredBgSprite = "ButtonMenuHovered";
            _buy1BBtn.pressedBgSprite = "ButtonMenuPressed";
            _buy1BBtn.disabledBgSprite = "ButtonMenuDisabled";
            _buy1BBtn.eventClick += OnBuy1BClick;
            _buy1BBtn.isVisible = false;

            _pay50Btn = AddUIComponent<UIButton>();
            _pay50Btn.size = new Vector2(90f, TAB_HEIGHT);
            _pay50Btn.relativePosition = new Vector3(WIDTH - 102f, tabY);
            _pay50Btn.text = "Pay 50%";
            _pay50Btn.textScale = 0.8f;
            _pay50Btn.normalBgSprite = "ButtonMenu";
            _pay50Btn.hoveredBgSprite = "ButtonMenuHovered";
            _pay50Btn.pressedBgSprite = "ButtonMenuPressed";
            _pay50Btn.disabledBgSprite = "ButtonMenuDisabled";
            _pay50Btn.eventClick += OnPay50Click;
            _pay50Btn.isVisible = false;

            _pay25Btn = AddUIComponent<UIButton>();
            _pay25Btn.size = new Vector2(90f, TAB_HEIGHT);
            _pay25Btn.relativePosition = new Vector3(WIDTH - 196f, tabY);
            _pay25Btn.text = "Pay 25%";
            _pay25Btn.textScale = 0.8f;
            _pay25Btn.normalBgSprite = "ButtonMenu";
            _pay25Btn.hoveredBgSprite = "ButtonMenuHovered";
            _pay25Btn.pressedBgSprite = "ButtonMenuPressed";
            _pay25Btn.disabledBgSprite = "ButtonMenuDisabled";
            _pay25Btn.eventClick += OnPay25Click;
            _pay25Btn.isVisible = false;

            _autoHedgeBtn = AddUIComponent<UIButton>();
            _autoHedgeBtn.size = new Vector2(95f, TAB_HEIGHT);
            _autoHedgeBtn.relativePosition = new Vector3(WIDTH - 314f, tabY);
            _autoHedgeBtn.text = "Auto-Hedge";
            _autoHedgeBtn.textScale = 0.75f;
            _autoHedgeBtn.normalBgSprite = "ButtonMenu";
            _autoHedgeBtn.hoveredBgSprite = "ButtonMenuHovered";
            _autoHedgeBtn.pressedBgSprite = "ButtonMenuPressed";
            _autoHedgeBtn.disabledBgSprite = "ButtonMenuDisabled";
            _autoHedgeBtn.eventClick += OnAutoHedgeClick;
            _autoHedgeBtn.isVisible = false;

            _sell25SwapsBtn = AddUIComponent<UIButton>();
            _sell25SwapsBtn.size = new Vector2(65f, TAB_HEIGHT);
            _sell25SwapsBtn.relativePosition = new Vector3(WIDTH - 215f, tabY);
            _sell25SwapsBtn.text = "Sell 25%";
            _sell25SwapsBtn.textScale = 0.75f;
            _sell25SwapsBtn.normalBgSprite = "ButtonMenu";
            _sell25SwapsBtn.hoveredBgSprite = "ButtonMenuHovered";
            _sell25SwapsBtn.pressedBgSprite = "ButtonMenuPressed";
            _sell25SwapsBtn.disabledBgSprite = "ButtonMenuDisabled";
            _sell25SwapsBtn.eventClick += OnSell25SwapsClick;
            _sell25SwapsBtn.isVisible = false;

            _sell50SwapsBtn = AddUIComponent<UIButton>();
            _sell50SwapsBtn.size = new Vector2(65f, TAB_HEIGHT);
            _sell50SwapsBtn.relativePosition = new Vector3(WIDTH - 146f, tabY);
            _sell50SwapsBtn.text = "Sell 50%";
            _sell50SwapsBtn.textScale = 0.75f;
            _sell50SwapsBtn.normalBgSprite = "ButtonMenu";
            _sell50SwapsBtn.hoveredBgSprite = "ButtonMenuHovered";
            _sell50SwapsBtn.pressedBgSprite = "ButtonMenuPressed";
            _sell50SwapsBtn.disabledBgSprite = "ButtonMenuDisabled";
            _sell50SwapsBtn.eventClick += OnSell50SwapsClick;
            _sell50SwapsBtn.isVisible = false;

            _exitAllSwapsBtn = AddUIComponent<UIButton>();
            _exitAllSwapsBtn.size = new Vector2(65f, TAB_HEIGHT);
            _exitAllSwapsBtn.relativePosition = new Vector3(WIDTH - 77f, tabY);
            _exitAllSwapsBtn.text = "Exit All";
            _exitAllSwapsBtn.textScale = 0.75f;
            _exitAllSwapsBtn.normalBgSprite = "ButtonMenu";
            _exitAllSwapsBtn.hoveredBgSprite = "ButtonMenuHovered";
            _exitAllSwapsBtn.pressedBgSprite = "ButtonMenuPressed";
            _exitAllSwapsBtn.disabledBgSprite = "ButtonMenuDisabled";
            _exitAllSwapsBtn.eventClick += OnExitAllSwapsClick;
            _exitAllSwapsBtn.isVisible = false;

            _activeTab = 0;
            UpdateTabHighlights();
        }

        private void CreateBondList()
        {
            float listY = HEADER_HEIGHT + TAB_HEIGHT + 8f;
            _listPanel = AddUIComponent<UIPanel>();
            _listPanel.size = new Vector2(WIDTH - 24f, ROW_HEIGHT * MAX_ROWS + 24f);
            _listPanel.relativePosition = new Vector3(12f, listY);
            _listPanel.eventMouseWheel += OnScrollWheel;

            _infoLabels = new UILabel[MAX_ROWS];
            _priceLabels = new UILabel[MAX_ROWS];
            _actionButtons = new UIButton[MAX_ROWS];

            for (int i = 0; i < MAX_ROWS; i++)
            {
                float y = i * ROW_HEIGHT;

                _infoLabels[i] = _listPanel.AddUIComponent<UILabel>();
                _infoLabels[i].autoSize = false;
                _infoLabels[i].size = new Vector2(460f, ROW_HEIGHT);
                _infoLabels[i].relativePosition = new Vector3(4f, y);
                _infoLabels[i].textScale = 0.75f;
                _infoLabels[i].verticalAlignment = UIVerticalAlignment.Middle;
                _infoLabels[i].text = "";

                _priceLabels[i] = _listPanel.AddUIComponent<UILabel>();
                _priceLabels[i].autoSize = false;
                _priceLabels[i].size = new Vector2(120f, ROW_HEIGHT);
                _priceLabels[i].relativePosition = new Vector3(468f, y);
                _priceLabels[i].textScale = 0.8f;
                _priceLabels[i].textAlignment = UIHorizontalAlignment.Right;
                _priceLabels[i].verticalAlignment = UIVerticalAlignment.Middle;
                _priceLabels[i].text = "";

                int idx = i;
                _actionButtons[i] = _listPanel.AddUIComponent<UIButton>();
                _actionButtons[i].size = new Vector2(80f, ROW_HEIGHT - 4f);
                _actionButtons[i].relativePosition = new Vector3(600f, y + 2f);
                _actionButtons[i].textScale = 0.8f;
                _actionButtons[i].normalBgSprite = "ButtonMenu";
                _actionButtons[i].hoveredBgSprite = "ButtonMenuHovered";
                _actionButtons[i].pressedBgSprite = "ButtonMenuPressed";
                _actionButtons[i].disabledBgSprite = "ButtonMenuDisabled";
                _actionButtons[i].text = "";
                _actionButtons[i].isVisible = false;
                _actionButtons[i].eventClick += delegate(UIComponent c, UIMouseEventParameter p) { OnActionClick(idx); };
            }

            _scrollHintLabel = _listPanel.AddUIComponent<UILabel>();
            _scrollHintLabel.autoSize = false;
            _scrollHintLabel.size = new Vector2(WIDTH - 24f, 20f);
            _scrollHintLabel.relativePosition = new Vector3(0f, ROW_HEIGHT * MAX_ROWS + 4f);
            _scrollHintLabel.textScale = 0.7f;
            _scrollHintLabel.textAlignment = UIHorizontalAlignment.Center;
            _scrollHintLabel.verticalAlignment = UIVerticalAlignment.Middle;
            _scrollHintLabel.text = "";
        }

        private void CreateFooter()
        {
            float footerY = HEIGHT - 36f;
            _footerLabel = AddUIComponent<UILabel>();
            _footerLabel.autoSize = false;
            _footerLabel.size = new Vector2(WIDTH - 24f, 30f);
            _footerLabel.relativePosition = new Vector3(12f, footerY);
            _footerLabel.textScale = 0.8f;
            _footerLabel.textAlignment = UIHorizontalAlignment.Left;
            _footerLabel.verticalAlignment = UIVerticalAlignment.Middle;
            _footerLabel.text = "";
        }

        public void Toggle()
        {
            isVisible = !isVisible;
            if (isVisible)
            {
                BringToFront();
                RefreshData();
            }
        }

        public override void Update()
        {
            base.Update();
            if (!isVisible) return;

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= REFRESH_INTERVAL)
            {
                _refreshTimer = 0f;
                RefreshData();
            }
        }

        private void RefreshData()
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null)
            {
                _summaryLabel.text = "Bond market engine not ready...";
                return;
            }

            string ratingStr = BondPricing.RatingLabel(engine.Rating);
            float yieldPct = engine.RequiredYield * 100f;
            float dscrVal = engine.DSCR;

            if (_activeTab == 4)
            {
                float overHedge = engine.OverHedgeRatio;
                string hedgeStatus = overHedge > 0f
                    ? string.Format("OVER-HEDGED {0:F0}%", overHedge * 100f)
                    : string.Format("Hedged: {0:N0}", engine.TotalHedgedNotional);
                int totalPos = engine.PortfolioCount + engine.IssuedCount + engine.SwapCount;
                _summaryLabel.text = string.Format(
                    "Positions: {0}  |  Bonds: {1}  |  Debt: {2}  |  Swaps: {3}\n" +
                    "Rating: {4}  |  Yield: {5:F1}%  |  Debt Face: {6:N0}  |  {7}",
                    totalPos, engine.PortfolioCount, engine.IssuedCount, engine.SwapCount,
                    ratingStr, yieldPct, engine.TotalDebtFace, hedgeStatus);
            }
            else if (_activeTab == 2)
            {
                _summaryLabel.text = string.Format(
                    "Rating: {0}  |  Yield: {1:F1}%  |  Default: {2:F1}%  |  Demand: {3}\n" +
                    "Debt: {4}/{5}  |  Owed: {6:N0}  |  Capacity: {7:N0}  |  {8}",
                    ratingStr, yieldPct, engine.DefaultProbability * 100f,
                    engine.DemandLabelText,
                    engine.IssuedCount, engine.MaxIssuedBonds,
                    engine.TotalDebtOwed, engine.AbsorptionCapacity,
                    engine.CreditStatusLabel);
            }
            else if (_activeTab == 3)
            {
                float volPct = engine.RevenueVolatility * 100f;
                float overHedge = engine.OverHedgeRatio;
                if (overHedge > 0f)
                {
                    float penalty = overHedge * 4f;
                    if (penalty > 10f) penalty = 10f;
                    _summaryLabel.text = string.Format(
                        "Rate: {0:F1}%  |  Vol: {1:F1}%  |  Swaps: {2}/{3}\n" +
                        "OVER-HEDGED by {4:N0}  |  Rate Penalty: +{5:F1}%",
                        engine.BenchmarkRate * 100f, volPct,
                        engine.SwapCount, engine.MaxActiveSwaps,
                        engine.TotalHedgedNotional - engine.TotalDebtFace, penalty);
                }
                else
                {
                    _summaryLabel.text = string.Format(
                        "Floating Rate: {0:F1}%  |  Volatility: {1:F1}%  |  Swaps: {2}/{3}\n" +
                        "Hedged: {4:N0}  |  Debt Face: {5:N0}",
                        engine.BenchmarkRate * 100f, volPct,
                        engine.SwapCount, engine.MaxActiveSwaps,
                        engine.TotalHedgedNotional, engine.TotalDebtFace);
                }
            }
            else
            {
                float incomeDisplay = engine.GrossIncome / 100f;
                float expenseDisplay = engine.TotalExpenses / 100f;
                float debtBurdenPct = engine.DebtBurden * 100f;

                _summaryLabel.text = string.Format(
                    "Rating: {0}  |  Yield: {1:F1}%  |  DSCR: {2:F2}  |  Demand: {3}\n" +
                    "Income: {4:N0}/tick  |  Expenses: {5:N0}/tick  |  Default: {6:F1}%",
                    ratingStr, yieldPct, dscrVal, engine.DemandLabelText,
                    incomeDisplay, expenseDisplay, engine.DefaultProbability * 100f);
            }

            if (_activeTab == 0)
                RefreshMarket(engine);
            else if (_activeTab == 1)
                RefreshPortfolio(engine);
            else if (_activeTab == 2)
                RefreshCityDebt(engine);
            else if (_activeTab == 3)
                RefreshHedging(engine);
            else
                RefreshPositions(engine);
        }

        private void RefreshMarket(BondMarketEngine engine)
        {
            _sellAllBtn.isVisible = false;
            _buy1MBtn.isVisible = true;
            _buy10MBtn.isVisible = true;
            _buy1BBtn.isVisible = true;
            _pay25Btn.isVisible = false;
            _pay50Btn.isVisible = false;
            _autoHedgeBtn.isVisible = false;
            _sell25SwapsBtn.isVisible = false;
            _sell50SwapsBtn.isVisible = false;
            _exitAllSwapsBtn.isVisible = false;
            _scrollHintLabel.text = "";

            engine.GetMarketSnapshot(_cachedBonds, _cachedPrices);
            int ticksInPeriod = engine.TicksInCurrentPeriod;

            int count = _cachedBonds.Count;
            if (count > MAX_ROWS) count = MAX_ROWS;

            for (int i = 0; i < MAX_ROWS; i++)
            {
                if (i < count)
                {
                    Bond b = _cachedBonds[i];
                    float price = _cachedPrices[i];
                    int daysLeft = b.RemainingPeriods * BondMarketEngine.TICKS_PER_PERIOD - ticksInPeriod;

                    _infoLabels[i].text = string.Format(
                        "{0}   Face: {1:N0}   {2:F1}%   {3}d left",
                        b.Name, b.FaceValue, b.CouponRate * 100f, daysLeft);
                    _priceLabels[i].text = string.Format("{0:N0}", price);
                    _actionButtons[i].text = "Buy";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = true;
                }
                else
                {
                    _infoLabels[i].text = "";
                    _priceLabels[i].text = "";
                    _actionButtons[i].isVisible = false;
                }
            }

            _footerLabel.text = string.Format("Bonds available: {0}  |  Portfolio: {1} bonds",
                engine.MarketCount, engine.PortfolioCount);
        }

        private void RefreshPortfolio(BondMarketEngine engine)
        {
            _sellAllBtn.isVisible = true;
            _sellAllBtn.isEnabled = engine.PortfolioCount > 0;
            _buy1MBtn.isVisible = false;
            _buy10MBtn.isVisible = false;
            _buy1BBtn.isVisible = false;
            _pay25Btn.isVisible = false;
            _pay50Btn.isVisible = false;
            _autoHedgeBtn.isVisible = false;
            _sell25SwapsBtn.isVisible = false;
            _sell50SwapsBtn.isVisible = false;
            _exitAllSwapsBtn.isVisible = false;

            engine.GetPortfolioSnapshot(_cachedBonds, _cachedPrices);
            int ticksInPeriod = engine.TicksInCurrentPeriod;

            int total = _cachedBonds.Count;
            int maxOffset = Math.Max(0, total - MAX_ROWS);
            if (_scrollOffset > maxOffset)
                _scrollOffset = maxOffset;

            float totalValue = 0f;
            float unrealizedPL = 0f;

            for (int j = 0; j < total; j++)
            {
                float p = _cachedPrices[j];
                Bond bj = _cachedBonds[j];
                totalValue += p;
                unrealizedPL += (p + bj.CouponsReceived) - bj.PurchasePrice;
            }

            float totalLifetimePL = engine.RealizedPL + unrealizedPL;

            for (int i = 0; i < MAX_ROWS; i++)
            {
                int bondIdx = _scrollOffset + i;
                if (bondIdx < total)
                {
                    Bond b = _cachedBonds[bondIdx];
                    float price = _cachedPrices[bondIdx];
                    float bondPL = (price + b.CouponsReceived) - b.PurchasePrice;
                    int daysLeft = b.RemainingPeriods * BondMarketEngine.TICKS_PER_PERIOD - ticksInPeriod;

                    string plStr = bondPL >= 0 ? "+" + bondPL.ToString("N0") : bondPL.ToString("N0");
                    _infoLabels[i].text = string.Format(
                        "{0}   {1:F1}%   Paid: {2:N0}   P/L: {3}   {4}d",
                        b.Name, b.CouponRate * 100f, b.PurchasePrice, plStr, daysLeft);
                    _priceLabels[i].text = string.Format("{0:N0}", price);
                    _actionButtons[i].text = "Sell";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = true;
                }
                else
                {
                    _infoLabels[i].text = "";
                    _priceLabels[i].text = "";
                    _actionButtons[i].isVisible = false;
                }
            }

            if (total > MAX_ROWS)
                _scrollHintLabel.text = string.Format("Showing {0}-{1} of {2}  (scroll to see more)",
                    _scrollOffset + 1, Math.Min(_scrollOffset + MAX_ROWS, total), total);
            else
                _scrollHintLabel.text = "";

            string totalPLStr = totalLifetimePL >= 0 ? "+" + totalLifetimePL.ToString("N0") : totalLifetimePL.ToString("N0");
            _footerLabel.text = string.Format("Portfolio Value: {0:N0}  |  Lifetime P/L: {1}  |  {2} bonds",
                totalValue, totalPLStr, engine.PortfolioCount);
        }

        private void RefreshCityDebt(BondMarketEngine engine)
        {
            _sellAllBtn.isVisible = false;
            _buy1MBtn.isVisible = false;
            _buy10MBtn.isVisible = false;
            _buy1BBtn.isVisible = false;
            _autoHedgeBtn.isVisible = false;
            _sell25SwapsBtn.isVisible = false;
            _sell50SwapsBtn.isVisible = false;
            _exitAllSwapsBtn.isVisible = false;
            bool hasDebt = engine.IssuedCount > 0;
            _pay25Btn.isVisible = hasDebt;
            _pay25Btn.isEnabled = hasDebt;
            _pay50Btn.isVisible = hasDebt;
            _pay50Btn.isEnabled = hasDebt;

            engine.GetIssuedBondsSnapshot(_cachedIssuedBonds);
            int issuedCount = _cachedIssuedBonds.Count;
            int templateCount = engine.IssueTemplateCount;
            int totalItems = issuedCount + templateCount;
            bool canIssue = engine.CanIssueBonds;
            float yieldPct = engine.RequiredYield * 100f;

            int maxOffset = Math.Max(0, totalItems - MAX_ROWS);
            if (_scrollOffset > maxOffset)
                _scrollOffset = maxOffset;

            for (int i = 0; i < MAX_ROWS; i++)
            {
                int itemIdx = _scrollOffset + i;
                if (itemIdx < issuedCount)
                {
                    Bond ib = _cachedIssuedBonds[itemIdx];
                    int monthsLeft = ib.RemainingPeriods;
                    float perPeriodCoupon = (ib.FaceValue * ib.CouponRate) / BondPricing.PeriodsPerYear;

                    _infoLabels[i].text = string.Format(
                        "{0}   {1:N0}   {2:F1}%   {3}mo left   Paid: {4:N0}",
                        ib.Name, ib.FaceValue, ib.CouponRate * 100f, monthsLeft, ib.CouponsReceived);
                    _priceLabels[i].text = string.Format("{0:N0}/per", perPeriodCoupon);
                    _actionButtons[i].text = "Repay";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = true;
                }
                else if (itemIdx < totalItems)
                {
                    int tIdx = itemIdx - issuedCount;
                    string tName = engine.GetTemplateName(tIdx);
                    float tFace = engine.GetTemplateFace(tIdx);
                    int tPeriods = engine.GetTemplatePeriods(tIdx);
                    float perPeriodCoupon = (tFace * engine.RequiredYield) / BondPricing.PeriodsPerYear;
                    int years = tPeriods / 12;

                    _infoLabels[i].text = string.Format(
                        "{0}   {1:N0}   {2:F1}%   {3}yr   {4:N0}/per",
                        tName, tFace, yieldPct, years, perPeriodCoupon);
                    _priceLabels[i].text = string.Format("{0:N0}", tFace);
                    _actionButtons[i].text = "Issue";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = canIssue;
                }
                else
                {
                    _infoLabels[i].text = "";
                    _priceLabels[i].text = "";
                    _actionButtons[i].isVisible = false;
                }
            }

            if (totalItems > MAX_ROWS)
                _scrollHintLabel.text = string.Format("Showing {0}-{1} of {2}  (scroll to see more)",
                    _scrollOffset + 1, Math.Min(_scrollOffset + MAX_ROWS, totalItems), totalItems);
            else if (!canIssue && engine.Rating == CreditRating.D)
                _scrollHintLabel.text = "RATING D - BOND MARKET ACCESS DENIED";
            else if (!canIssue && engine.DemandScore < 0.10f)
                _scrollHintLabel.text = "NO DEMAND - CITIZENS UNWILLING TO BUY BONDS";
            else if (!canIssue)
                _scrollHintLabel.text = string.Format("MAX CAPACITY ({0}/{0}) - REPAY EXISTING DEBT FIRST",
                    engine.MaxIssuedBonds);
            else
                _scrollHintLabel.text = "";

            string status = engine.CreditStatusLabel;
            int penalty = engine.DefaultPenalty;
            string penaltyStr = penalty > 0
                ? " | Yield Penalty: +" + (penalty * 0.048f).ToString("F2") + "%"
                : "";

            _footerLabel.text = string.Format(
                "Outstanding: {0}/{1}  |  Paid: {2:N0}  |  {3}{4}",
                engine.IssuedCount, engine.MaxIssuedBonds,
                engine.TotalCouponsPaid, status, penaltyStr);
        }

        private void RefreshHedging(BondMarketEngine engine)
        {
            _sellAllBtn.isVisible = false;
            _buy1MBtn.isVisible = false;
            _buy10MBtn.isVisible = false;
            _buy1BBtn.isVisible = false;
            _pay25Btn.isVisible = false;
            _pay50Btn.isVisible = false;
            bool hasSwaps = engine.SwapCount > 0;
            _autoHedgeBtn.isVisible = true;
            _autoHedgeBtn.isEnabled = engine.SwapCount < engine.MaxActiveSwaps && engine.IssuedCount > 0;
            _sell25SwapsBtn.isVisible = true;
            _sell25SwapsBtn.isEnabled = hasSwaps;
            _sell50SwapsBtn.isVisible = true;
            _sell50SwapsBtn.isEnabled = hasSwaps;
            _exitAllSwapsBtn.isVisible = true;
            _exitAllSwapsBtn.isEnabled = hasSwaps;

            engine.GetActiveSwapsSnapshot(_cachedSwaps);

            int total = _cachedSwaps.Count;
            int maxOffset = Math.Max(0, total - MAX_ROWS);
            if (_scrollOffset > maxOffset)
                _scrollOffset = maxOffset;

            for (int i = 0; i < MAX_ROWS; i++)
            {
                int swapIdx = _scrollOffset + i;
                if (swapIdx < total)
                {
                    InterestRateSwap s = _cachedSwaps[swapIdx];
                    string direction = s.PayFixed ? "Pay Fixed" : "Rcv Fixed";
                    string plStr = s.CumulativePL >= 0f
                        ? "+" + s.CumulativePL.ToString("N0")
                        : s.CumulativePL.ToString("N0");
                    int monthsLeft = s.RemainingPeriods;

                    _infoLabels[i].text = string.Format(
                        "{0}  {1}  Notional: {2:N0}  Fixed: {3:F1}%  {4}mo  P/L: {5}",
                        s.Id, direction, s.NotionalAmount,
                        s.FixedRate * 100f, monthsLeft, plStr);
                    _priceLabels[i].text = string.Format("{0:N0}/per", s.LastSettlement);
                    _actionButtons[i].text = "Exit";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = true;
                }
                else
                {
                    _infoLabels[i].text = "";
                    _priceLabels[i].text = "";
                    _actionButtons[i].isVisible = false;
                }
            }

            if (total > MAX_ROWS)
                _scrollHintLabel.text = string.Format("Showing {0}-{1} of {2}  (scroll to see more)",
                    _scrollOffset + 1, Math.Min(_scrollOffset + MAX_ROWS, total), total);
            else
                _scrollHintLabel.text = "";

            string recommendation = engine.CalculateRecommendedHedge();
            string swapPLStr = engine.SwapPL >= 0f
                ? "+" + engine.SwapPL.ToString("N0")
                : engine.SwapPL.ToString("N0");
            _footerLabel.text = string.Format("Swap P/L: {0}  |  {1}", swapPLStr, recommendation);
        }

        private void RefreshPositions(BondMarketEngine engine)
        {
            _sellAllBtn.isVisible = false;
            _buy1MBtn.isVisible = false;
            _buy10MBtn.isVisible = false;
            _buy1BBtn.isVisible = false;
            _pay25Btn.isVisible = false;
            _pay50Btn.isVisible = false;
            _autoHedgeBtn.isVisible = false;
            _sell25SwapsBtn.isVisible = false;
            _sell50SwapsBtn.isVisible = false;
            _exitAllSwapsBtn.isVisible = false;

            engine.GetPortfolioSnapshot(_cachedBonds, _cachedPrices);
            engine.GetIssuedBondsSnapshot(_cachedIssuedBonds);
            engine.GetActiveSwapsSnapshot(_cachedSwaps);

            int portfolioCount = _cachedBonds.Count;
            int issuedCount = _cachedIssuedBonds.Count;
            int swapCount = _cachedSwaps.Count;
            int totalItems = portfolioCount + issuedCount + swapCount;

            int maxOffset = Math.Max(0, totalItems - MAX_ROWS);
            if (_scrollOffset > maxOffset)
                _scrollOffset = maxOffset;

            float totalPV = 0f;
            for (int j = 0; j < portfolioCount; j++)
                totalPV += _cachedPrices[j];

            for (int i = 0; i < MAX_ROWS; i++)
            {
                int itemIdx = _scrollOffset + i;
                if (itemIdx < portfolioCount)
                {
                    Bond b = _cachedBonds[itemIdx];
                    float price = _cachedPrices[itemIdx];
                    float bondPL = (price + b.CouponsReceived) - b.PurchasePrice;
                    string plStr = bondPL >= 0 ? "+" + bondPL.ToString("N0") : bondPL.ToString("N0");

                    _infoLabels[i].text = string.Format(
                        "[BUY] {0}  {1:F1}%  Face: {2:N0}  P/L: {3}  {4}mo",
                        b.Name, b.CouponRate * 100f, b.FaceValue, plStr, b.RemainingPeriods);
                    _priceLabels[i].text = string.Format("{0:N0}", price);
                    _actionButtons[i].text = "Sell";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = true;
                }
                else if (itemIdx < portfolioCount + issuedCount)
                {
                    int iIdx = itemIdx - portfolioCount;
                    Bond ib = _cachedIssuedBonds[iIdx];
                    float perPeriod = (ib.FaceValue * ib.CouponRate) / BondPricing.PeriodsPerYear;

                    _infoLabels[i].text = string.Format(
                        "[OWE] {0}  {1:F1}%  Face: {2:N0}  Paid: {3:N0}  {4}mo",
                        ib.Name, ib.CouponRate * 100f, ib.FaceValue, ib.CouponsReceived, ib.RemainingPeriods);
                    _priceLabels[i].text = string.Format("{0:N0}/per", perPeriod);
                    _actionButtons[i].text = "Repay";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = true;
                }
                else if (itemIdx < totalItems)
                {
                    int sIdx = itemIdx - portfolioCount - issuedCount;
                    InterestRateSwap s = _cachedSwaps[sIdx];
                    string dir = s.PayFixed ? "PayFix" : "RcvFix";
                    string plStr = s.CumulativePL >= 0f
                        ? "+" + s.CumulativePL.ToString("N0")
                        : s.CumulativePL.ToString("N0");

                    _infoLabels[i].text = string.Format(
                        "[SWAP] {0}  {1}  Notional: {2:N0}  {3:F1}%  P/L: {4}  {5}mo",
                        s.Id, dir, s.NotionalAmount, s.FixedRate * 100f, plStr, s.RemainingPeriods);
                    _priceLabels[i].text = string.Format("{0:N0}/per", s.LastSettlement);
                    _actionButtons[i].text = "Exit";
                    _actionButtons[i].isVisible = true;
                    _actionButtons[i].isEnabled = true;
                }
                else
                {
                    _infoLabels[i].text = "";
                    _priceLabels[i].text = "";
                    _actionButtons[i].isVisible = false;
                }
            }

            if (totalItems > MAX_ROWS)
                _scrollHintLabel.text = string.Format("Showing {0}-{1} of {2}  (scroll to see more)",
                    _scrollOffset + 1, Math.Min(_scrollOffset + MAX_ROWS, totalItems), totalItems);
            else if (totalItems == 0)
                _scrollHintLabel.text = "No open positions";
            else
                _scrollHintLabel.text = "";

            string swapPLStr = engine.SwapPL >= 0f
                ? "+" + engine.SwapPL.ToString("N0")
                : engine.SwapPL.ToString("N0");
            _footerLabel.text = string.Format(
                "Assets: {0:N0}  |  Debt Face: {1:N0}  |  Swap P/L: {2}  |  {3} positions",
                totalPV, engine.TotalDebtFace, swapPLStr, totalItems);
        }

        private void OnScrollWheel(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (_activeTab == 0) return;
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int totalItems;
            if (_activeTab == 1)
                totalItems = engine.PortfolioCount;
            else if (_activeTab == 2)
                totalItems = engine.IssuedCount + engine.IssueTemplateCount;
            else if (_activeTab == 3)
                totalItems = engine.SwapCount;
            else
                totalItems = engine.PortfolioCount + engine.IssuedCount + engine.SwapCount;
            int maxOffset = Math.Max(0, totalItems - MAX_ROWS);
            if (eventParam.wheelDelta < 0f)
                _scrollOffset = Math.Min(maxOffset, _scrollOffset + 1);
            else if (eventParam.wheelDelta > 0f)
                _scrollOffset = Math.Max(0, _scrollOffset - 1);

            eventParam.Use();
            RefreshData();
        }

        private void OnActionClick(int index)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            if (_activeTab == 1)
            {
                int portfolioIdx = _scrollOffset + index;
                if (engine.SellBond(portfolioIdx))
                {
                    int maxOffset = Math.Max(0, engine.PortfolioCount - MAX_ROWS);
                    if (_scrollOffset > maxOffset)
                        _scrollOffset = maxOffset;
                    RefreshData();
                }
            }
            else if (_activeTab == 2)
            {
                int itemIdx = _scrollOffset + index;
                int issuedCount = _cachedIssuedBonds.Count;

                if (itemIdx < issuedCount)
                {
                    if (engine.RepaySingleBond(itemIdx))
                    {
                        int totalItems = engine.IssuedCount + engine.IssueTemplateCount;
                        int maxOff = Math.Max(0, totalItems - MAX_ROWS);
                        if (_scrollOffset > maxOff)
                            _scrollOffset = maxOff;
                        RefreshData();
                    }
                    else
                        Debug.Log("[MyFirstMod] Cannot repay bond - not enough funds.");
                }
                else
                {
                    int templateIdx = itemIdx - issuedCount;
                    if (engine.IssueBond(templateIdx))
                        RefreshData();
                    else
                        Debug.Log("[MyFirstMod] Cannot issue bond - at capacity or rating D.");
                }
            }
            else if (_activeTab == 3)
            {
                int swapIdx = _scrollOffset + index;
                if (engine.TerminateSwap(swapIdx))
                {
                    int maxOffset = Math.Max(0, engine.SwapCount - MAX_ROWS);
                    if (_scrollOffset > maxOffset)
                        _scrollOffset = maxOffset;
                    RefreshData();
                }
            }
            else if (_activeTab == 4)
            {
                int itemIdx = _scrollOffset + index;
                int portfolioCount = _cachedBonds.Count;
                int issuedCount = _cachedIssuedBonds.Count;

                if (itemIdx < portfolioCount)
                {
                    if (engine.SellBond(itemIdx))
                    {
                        int total = engine.PortfolioCount + engine.IssuedCount + engine.SwapCount;
                        int maxOff = Math.Max(0, total - MAX_ROWS);
                        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
                        RefreshData();
                    }
                }
                else if (itemIdx < portfolioCount + issuedCount)
                {
                    int issuedIdx = itemIdx - portfolioCount;
                    if (engine.RepaySingleBond(issuedIdx))
                    {
                        int total = engine.PortfolioCount + engine.IssuedCount + engine.SwapCount;
                        int maxOff = Math.Max(0, total - MAX_ROWS);
                        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
                        RefreshData();
                    }
                    else
                        Debug.Log("[MyFirstMod] Cannot repay bond - not enough funds.");
                }
                else
                {
                    int swapIdx = itemIdx - portfolioCount - issuedCount;
                    if (engine.TerminateSwap(swapIdx))
                    {
                        int total = engine.PortfolioCount + engine.IssuedCount + engine.SwapCount;
                        int maxOff = Math.Max(0, total - MAX_ROWS);
                        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
                        RefreshData();
                    }
                }
            }
            else
            {
                if (engine.BuyBond(index))
                    RefreshData();
                else
                    Debug.Log("[MyFirstMod] Buy failed - not enough funds or invalid index.");
            }
        }

        private void OnBuy1MClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int bought = engine.Buy10x1MBonds();
            if (bought > 0)
                RefreshData();
            else
                Debug.Log("[MyFirstMod] Buy 10x 1M 5yr failed - not enough funds.");
        }

        private void OnBuy10MClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int bought = engine.Buy10x10MBonds();
            if (bought > 0)
                RefreshData();
            else
                Debug.Log("[MyFirstMod] Buy 10x 10M 5yr failed - not enough funds.");
        }

        private void OnBuy1BClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            if (engine.Buy1BBond())
                RefreshData();
            else
                Debug.Log("[MyFirstMod] Buy 1B 5yr failed - not enough funds.");
        }

        private void OnSellAllClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int sold = engine.SellAllBonds();
            _scrollOffset = 0;
            if (sold > 0)
                Debug.Log("[MyFirstMod] Sold all " + sold.ToString() + " bonds");
            RefreshData();
        }

        private void OnPay25Click(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int retired = engine.PayDebtPercent(0.25f);
            if (retired > 0)
                Debug.Log("[MyFirstMod] Early repayment: retired " + retired.ToString() + " bonds (25% target)");
            RefreshData();
        }

        private void OnPay50Click(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int retired = engine.PayDebtPercent(0.50f);
            if (retired > 0)
                Debug.Log("[MyFirstMod] Early repayment: retired " + retired.ToString() + " bonds (50% target)");
            RefreshData();
        }

        private void OnCloseClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            isVisible = false;
        }

        private void OnMarketTab(UIComponent component, UIMouseEventParameter eventParam)
        {
            _activeTab = 0;
            _scrollOffset = 0;
            UpdateTabHighlights();
            RefreshData();
        }

        private void OnPortfolioTab(UIComponent component, UIMouseEventParameter eventParam)
        {
            _activeTab = 1;
            _scrollOffset = 0;
            UpdateTabHighlights();
            RefreshData();
        }

        private void OnCityDebtTab(UIComponent component, UIMouseEventParameter eventParam)
        {
            _activeTab = 2;
            _scrollOffset = 0;
            UpdateTabHighlights();
            RefreshData();
        }

        private void OnHedgingTab(UIComponent component, UIMouseEventParameter eventParam)
        {
            _activeTab = 3;
            _scrollOffset = 0;
            UpdateTabHighlights();
            RefreshData();
        }

        private void OnPositionsTab(UIComponent component, UIMouseEventParameter eventParam)
        {
            _activeTab = 4;
            _scrollOffset = 0;
            UpdateTabHighlights();
            RefreshData();
        }

        private void OnAutoHedgeClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            if (engine.AutoHedge())
                RefreshData();
            else
                Debug.Log("[MyFirstMod] Auto-hedge failed - no unhedged debt or swap limit reached.");
        }

        private void OnExitAllSwapsClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int exited = engine.TerminateAllSwaps();
            _scrollOffset = 0;
            if (exited > 0)
                Debug.Log("[MyFirstMod] Terminated " + exited.ToString() + " swap(s)");
            RefreshData();
        }

        private void OnSell25SwapsClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int affected = engine.SellAllSwapsTranche(0.25f);
            if (affected > 0)
                Debug.Log("[MyFirstMod] Sold 25% tranche of " + affected.ToString() + " swap(s)");
            RefreshData();
        }

        private void OnSell50SwapsClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int affected = engine.SellAllSwapsTranche(0.50f);
            if (affected > 0)
                Debug.Log("[MyFirstMod] Sold 50% tranche of " + affected.ToString() + " swap(s)");
            RefreshData();
        }

        private void UpdateTabHighlights()
        {
            _marketTabBtn.normalBgSprite = _activeTab == 0 ? "ButtonMenuFocused" : "ButtonMenu";
            _portfolioTabBtn.normalBgSprite = _activeTab == 1 ? "ButtonMenuFocused" : "ButtonMenu";
            _cityDebtTabBtn.normalBgSprite = _activeTab == 2 ? "ButtonMenuFocused" : "ButtonMenu";
            _hedgingTabBtn.normalBgSprite = _activeTab == 3 ? "ButtonMenuFocused" : "ButtonMenu";
            _positionsTabBtn.normalBgSprite = _activeTab == 4 ? "ButtonMenuFocused" : "ButtonMenu";
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }
    }
}
