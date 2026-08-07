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
            float tabW = 110f;
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
            _cityDebtTabBtn.text = "City Debt";
            _cityDebtTabBtn.textScale = 0.85f;
            _cityDebtTabBtn.normalBgSprite = "ButtonMenu";
            _cityDebtTabBtn.hoveredBgSprite = "ButtonMenuHovered";
            _cityDebtTabBtn.pressedBgSprite = "ButtonMenuPressed";
            _cityDebtTabBtn.focusedBgSprite = "ButtonMenuFocused";
            _cityDebtTabBtn.eventClick += OnCityDebtTab;

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
            _buy1MBtn.size = new Vector2(130f, TAB_HEIGHT);
            _buy1MBtn.relativePosition = new Vector3(WIDTH - 142f, tabY);
            _buy1MBtn.text = "Buy 10x 1M 5yr";
            _buy1MBtn.textScale = 0.75f;
            _buy1MBtn.normalBgSprite = "ButtonMenu";
            _buy1MBtn.hoveredBgSprite = "ButtonMenuHovered";
            _buy1MBtn.pressedBgSprite = "ButtonMenuPressed";
            _buy1MBtn.disabledBgSprite = "ButtonMenuDisabled";
            _buy1MBtn.eventClick += OnBuy1MClick;
            _buy1MBtn.isVisible = false;

            _buy10MBtn = AddUIComponent<UIButton>();
            _buy10MBtn.size = new Vector2(135f, TAB_HEIGHT);
            _buy10MBtn.relativePosition = new Vector3(WIDTH - 142f - 139f, tabY);
            _buy10MBtn.text = "Buy 10x 10M 5yr";
            _buy10MBtn.textScale = 0.75f;
            _buy10MBtn.normalBgSprite = "ButtonMenu";
            _buy10MBtn.hoveredBgSprite = "ButtonMenuHovered";
            _buy10MBtn.pressedBgSprite = "ButtonMenuPressed";
            _buy10MBtn.disabledBgSprite = "ButtonMenuDisabled";
            _buy10MBtn.eventClick += OnBuy10MClick;
            _buy10MBtn.isVisible = false;

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

            if (_activeTab == 2)
            {
                _summaryLabel.text = string.Format(
                    "Rating: {0}  |  Yield: {1:F1}%  |  DSCR: {2:F2}\n" +
                    "Debt: {3}/{4} bonds  |  Owed: {5:N0}  |  {6}",
                    ratingStr, yieldPct, dscrVal,
                    engine.IssuedCount, engine.MaxIssuedBonds,
                    engine.TotalDebtOwed, engine.CreditStatusLabel);
            }
            else
            {
                float incomeDisplay = engine.GrossIncome / 100f;
                float expenseDisplay = engine.TotalExpenses / 100f;
                float debtBurdenPct = engine.DebtBurden * 100f;

                _summaryLabel.text = string.Format(
                    "Rating: {0}  |  Yield: {1:F1}%  |  DSCR: {2:F2}\n" +
                    "Income: {3:N0}/tick  |  Expenses: {4:N0}/tick  |  Debt Burden: {5:F1}%",
                    ratingStr, yieldPct, dscrVal, incomeDisplay, expenseDisplay, debtBurdenPct);
            }

            if (_activeTab == 0)
                RefreshMarket(engine);
            else if (_activeTab == 1)
                RefreshPortfolio(engine);
            else
                RefreshCityDebt(engine);
        }

        private void RefreshMarket(BondMarketEngine engine)
        {
            _sellAllBtn.isVisible = false;
            _buy1MBtn.isVisible = true;
            _buy10MBtn.isVisible = true;
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
            _scrollHintLabel.text = "";

            int templateCount = engine.IssueTemplateCount;
            bool canIssue = engine.CanIssueBonds;
            float yieldPct = engine.RequiredYield * 100f;

            for (int i = 0; i < MAX_ROWS; i++)
            {
                if (i < templateCount)
                {
                    string tName = engine.GetTemplateName(i);
                    float tFace = engine.GetTemplateFace(i);
                    int tPeriods = engine.GetTemplatePeriods(i);
                    float perPeriodCoupon = (tFace * engine.RequiredYield) / BondPricing.PeriodsPerYear;

                    _infoLabels[i].text = string.Format(
                        "{0}   {1:N0}   {2:F1}%   {3} per   {4:N0}/per",
                        tName, tFace, yieldPct, tPeriods, perPeriodCoupon);
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

            if (!canIssue && engine.Rating == CreditRating.D)
                _scrollHintLabel.text = "RATING D - BOND MARKET ACCESS DENIED";
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

        private void OnScrollWheel(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (_activeTab != 1) return;
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            int maxOffset = Math.Max(0, engine.PortfolioCount - MAX_ROWS);
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
                if (engine.IssueBond(index))
                    RefreshData();
                else
                    Debug.Log("[MyFirstMod] Cannot issue bond - at capacity or rating D.");
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

        private void UpdateTabHighlights()
        {
            _marketTabBtn.normalBgSprite = _activeTab == 0 ? "ButtonMenuFocused" : "ButtonMenu";
            _portfolioTabBtn.normalBgSprite = _activeTab == 1 ? "ButtonMenuFocused" : "ButtonMenu";
            _cityDebtTabBtn.normalBgSprite = _activeTab == 2 ? "ButtonMenuFocused" : "ButtonMenu";
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }
    }
}
