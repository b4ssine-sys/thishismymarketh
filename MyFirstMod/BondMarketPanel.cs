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

        private const float WIDTH = 720f;
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
        private bool _showingPortfolio;

        private UIPanel _listPanel;
        private UILabel[] _infoLabels;
        private UILabel[] _priceLabels;
        private UIButton[] _actionButtons;

        private UILabel _footerLabel;

        private float _refreshTimer;
        private const float REFRESH_INTERVAL = 4f;

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
            float tabW = 120f;

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
            _portfolioTabBtn.relativePosition = new Vector3(12f + tabW + 4f, tabY);
            _portfolioTabBtn.text = "Portfolio";
            _portfolioTabBtn.textScale = 0.85f;
            _portfolioTabBtn.normalBgSprite = "ButtonMenu";
            _portfolioTabBtn.hoveredBgSprite = "ButtonMenuHovered";
            _portfolioTabBtn.pressedBgSprite = "ButtonMenuPressed";
            _portfolioTabBtn.focusedBgSprite = "ButtonMenuFocused";
            _portfolioTabBtn.eventClick += OnPortfolioTab;

            _showingPortfolio = false;
            UpdateTabHighlights();
        }

        private void CreateBondList()
        {
            float listY = HEADER_HEIGHT + TAB_HEIGHT + 8f;
            _listPanel = AddUIComponent<UIPanel>();
            _listPanel.size = new Vector2(WIDTH - 24f, ROW_HEIGHT * MAX_ROWS + 4f);
            _listPanel.relativePosition = new Vector3(12f, listY);

            _infoLabels = new UILabel[MAX_ROWS];
            _priceLabels = new UILabel[MAX_ROWS];
            _actionButtons = new UIButton[MAX_ROWS];

            for (int i = 0; i < MAX_ROWS; i++)
            {
                float y = i * ROW_HEIGHT;

                _infoLabels[i] = _listPanel.AddUIComponent<UILabel>();
                _infoLabels[i].autoSize = false;
                _infoLabels[i].size = new Vector2(400f, ROW_HEIGHT);
                _infoLabels[i].relativePosition = new Vector3(0f, y);
                _infoLabels[i].textScale = 0.75f;
                _infoLabels[i].verticalAlignment = UIVerticalAlignment.Middle;
                _infoLabels[i].text = "";

                _priceLabels[i] = _listPanel.AddUIComponent<UILabel>();
                _priceLabels[i].autoSize = false;
                _priceLabels[i].size = new Vector2(140f, ROW_HEIGHT);
                _priceLabels[i].relativePosition = new Vector3(400f, y);
                _priceLabels[i].textScale = 0.8f;
                _priceLabels[i].textAlignment = UIHorizontalAlignment.Right;
                _priceLabels[i].verticalAlignment = UIVerticalAlignment.Middle;
                _priceLabels[i].text = "";

                int idx = i;
                _actionButtons[i] = _listPanel.AddUIComponent<UIButton>();
                _actionButtons[i].size = new Vector2(100f, ROW_HEIGHT - 4f);
                _actionButtons[i].relativePosition = new Vector3(550f, y + 2f);
                _actionButtons[i].textScale = 0.8f;
                _actionButtons[i].normalBgSprite = "ButtonMenu";
                _actionButtons[i].hoveredBgSprite = "ButtonMenuHovered";
                _actionButtons[i].pressedBgSprite = "ButtonMenuPressed";
                _actionButtons[i].disabledBgSprite = "ButtonMenuDisabled";
                _actionButtons[i].text = "";
                _actionButtons[i].isVisible = false;
                _actionButtons[i].eventClick += delegate(UIComponent c, UIMouseEventParameter p) { OnActionClick(idx); };
            }
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
            float incomeDisplay = engine.GrossIncome / 100f;
            float expenseDisplay = engine.TotalExpenses / 100f;
            float debtBurdenPct = engine.DebtBurden * 100f;

            _summaryLabel.text = string.Format(
                "Rating: {0}  |  Yield: {1:F1}%  |  DSCR: {2:F2}\n" +
                "Income: {3:N0}/tick  |  Expenses: {4:N0}/tick  |  Debt Burden: {5:F1}%",
                ratingStr, yieldPct, dscrVal, incomeDisplay, expenseDisplay, debtBurdenPct);

            if (_showingPortfolio)
                RefreshPortfolio(engine);
            else
                RefreshMarket(engine);
        }

        private void RefreshMarket(BondMarketEngine engine)
        {
            engine.GetMarketSnapshot(_cachedBonds, _cachedPrices);

            int count = _cachedBonds.Count;
            if (count > MAX_ROWS) count = MAX_ROWS;

            for (int i = 0; i < MAX_ROWS; i++)
            {
                if (i < count)
                {
                    Bond b = _cachedBonds[i];
                    float price = _cachedPrices[i];

                    _infoLabels[i].text = string.Format(
                        "{0}  Face:{1:N0}  Cpn:{2:F1}%  Per:{3}",
                        b.Name, b.FaceValue, b.CouponRate * 100f, b.RemainingPeriods);
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
            engine.GetPortfolioSnapshot(_cachedBonds, _cachedPrices);

            int count = _cachedBonds.Count;
            if (count > MAX_ROWS) count = MAX_ROWS;

            float totalValue = 0f;
            float totalPL = 0f;

            for (int i = 0; i < MAX_ROWS; i++)
            {
                if (i < count)
                {
                    Bond b = _cachedBonds[i];
                    float price = _cachedPrices[i];
                    float pl = price - b.PurchasePrice;
                    totalValue += price;
                    totalPL += pl;

                    string plStr = pl >= 0 ? "+" + pl.ToString("N0") : pl.ToString("N0");
                    _infoLabels[i].text = string.Format(
                        "{0}  Paid:{1:N0}  Per:{2}  P/L:{3}",
                        b.Name, b.PurchasePrice, b.RemainingPeriods, plStr);
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

            string totalPLStr = totalPL >= 0 ? "+" + totalPL.ToString("N0") : totalPL.ToString("N0");
            _footerLabel.text = string.Format("Portfolio Value: {0:N0}  |  Total P/L: {1}  |  {2} bonds",
                totalValue, totalPLStr, engine.PortfolioCount);
        }

        private void OnActionClick(int index)
        {
            BondMarketEngine engine = BondMarketEngine.Instance;
            if (engine == null) return;

            if (_showingPortfolio)
            {
                if (engine.SellBond(index))
                    RefreshData();
                else
                    Debug.Log("[MyFirstMod] Sell failed at index " + index.ToString());
            }
            else
            {
                if (engine.BuyBond(index))
                    RefreshData();
                else
                    Debug.Log("[MyFirstMod] Buy failed - not enough funds or invalid index.");
            }
        }

        private void OnCloseClick(UIComponent component, UIMouseEventParameter eventParam)
        {
            isVisible = false;
        }

        private void OnMarketTab(UIComponent component, UIMouseEventParameter eventParam)
        {
            _showingPortfolio = false;
            UpdateTabHighlights();
            RefreshData();
        }

        private void OnPortfolioTab(UIComponent component, UIMouseEventParameter eventParam)
        {
            _showingPortfolio = true;
            UpdateTabHighlights();
            RefreshData();
        }

        private void UpdateTabHighlights()
        {
            if (!_showingPortfolio)
            {
                _marketTabBtn.normalBgSprite = "ButtonMenuFocused";
                _portfolioTabBtn.normalBgSprite = "ButtonMenu";
            }
            else
            {
                _marketTabBtn.normalBgSprite = "ButtonMenu";
                _portfolioTabBtn.normalBgSprite = "ButtonMenuFocused";
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }
    }
}
