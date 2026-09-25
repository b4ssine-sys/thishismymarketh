using System;
using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // Invest: issuer cards with their rating trend, the market's offerings, and
    // the city's holdings with unrealized P&L. Replaces Market, Portfolio and
    // Positions.
    public sealed class InvestView : WorkspaceView
    {
        private const int Cards = 6;
        private const int Rows = 6;

        private readonly BoundLabel[] _cardName = new BoundLabel[Cards];
        private readonly BoundLabel[] _cardRating = new BoundLabel[Cards];
        private readonly BoundLabel[] _cardSpread = new BoundLabel[Cards];

        private readonly BoundLabel[] _marketRows = new BoundLabel[Rows];
        private readonly UIButton[] _buyButtons = new UIButton[Rows];
        private readonly string[] _marketIds = new string[Rows];
        private BoundLabel _marketHint;
        private int _marketOffset;

        private readonly BoundLabel[] _holdingRows = new BoundLabel[Rows];
        private readonly UIButton[] _sellButtons = new UIButton[Rows];
        private readonly string[] _holdingIds = new string[Rows];
        private BoundLabel _holdingsTotal;
        private int _holdingOffset;
        private UIButton _sellAll;

        public InvestView(BondMarketWindow window, UIComponent parent) : base(window, parent) { }

        public override string Title { get { return "Invest"; } }

        protected override void BuildControls()
        {
            for (int i = 0; i < Cards; i++)
            {
                UIPanel card = Widgets.Card(Root, i * 140f, 0f, 134f, 74f);
                _cardName[i] = Widgets.Figure(card, 6f, 2f, 122f, 30f, 0.7f, "Issuer and sector.");
                _cardRating[i] = Widgets.Figure(card, 6f, 32f, 122f, 22f, 0.95f,
                    "Issuer rating and its move at the last annual review. Ratings drift back toward the sector's home rating.");
                _cardSpread[i] = Widgets.Figure(card, 6f, 52f, 122f, 20f, 0.7f,
                    "Credit spread over the benchmark curve for this issuer's paper.");
            }

            UIPanel market = Widgets.Card(Root, 0f, 82f, 410f, 342f);
            market.eventMouseWheel += OnMarketWheel;
            Widgets.Label(market, 10f, 2f, 390f, 20f, 0.75f, "Market offerings",
                "Other issuers' bonds. Price is mid; buying pays the ask (mid plus the half-spread).").textColor = Theme.Muted;
            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                _marketRows[i] = Widgets.Figure(market, 10f, 24f + i * 44f, 316f, 40f, 0.72f,
                    "Issuer and rating; face value, coupon, months to maturity, mid price and the half-spread you pay to buy.");
                _buyButtons[i] = Widgets.Button(market, "Buy", 330f, 30f + i * 44f, 70f, 28f,
                    "Buy this bond at the ask.", delegate(UIComponent c, UIMouseEventParameter p) { OnBuy(row); });
            }
            _marketHint = Widgets.Figure(market, 10f, 290f, 390f, 18f, 0.65f, null);
            _marketHint.Color(Theme.Muted, 1);
            Widgets.Button(market, "10 x 1M", 10f, 310f, 120f, 26f,
                "Buy ten 1M five-year lots; each pays spread and price impact.",
                delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.BuyBulk10x1M()); });
            Widgets.Button(market, "10 x 10M", 140f, 310f, 120f, 26f,
                "Buy ten 10M five-year lots; each pays spread and price impact.",
                delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.BuyBulk10x10M()); });
            Widgets.Button(market, "1 x 1B", 270f, 310f, 130f, 26f,
                "Buy one 1B five-year lot, if the market is deep enough.",
                delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.BuyBulk1B()); });

            UIPanel holdings = Widgets.Card(Root, 426f, 82f, 410f, 342f);
            holdings.eventMouseWheel += OnHoldingWheel;
            Widgets.Label(holdings, 10f, 2f, 390f, 20f, 0.75f, "City holdings",
                "Bonds the city owns. Selling receives the bid (mid less the half-spread).").textColor = Theme.Muted;
            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                _holdingRows[i] = Widgets.Figure(holdings, 10f, 24f + i * 44f, 316f, 40f, 0.72f,
                    "Issuer and rating; what the city paid, what it is worth now, profit including coupons, months to maturity.");
                _sellButtons[i] = Widgets.Button(holdings, "Sell", 330f, 30f + i * 44f, 70f, 28f,
                    "Sell this bond at the bid.", delegate(UIComponent c, UIMouseEventParameter p) { OnSell(row); });
            }
            _holdingsTotal = Widgets.Figure(holdings, 10f, 290f, 390f, 18f, 0.7f,
                "Market value of holdings and lifetime profit (realized plus unrealized).");
            _sellAll = Widgets.Button(holdings, "Sell all", 10f, 310f, 390f, 26f,
                "Sell every holding at the bid.", delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.SellAllBonds()); });
        }

        protected override void Render(EngineSnapshot s)
        {
            for (int i = 0; i < Cards; i++)
            {
                if (i < s.Issuers.Length)
                {
                    IssuerView v = s.Issuers[i];
                    _cardName[i].Text(v.Name);
                    string trend = v.Rating < v.PreviousRating ? "  up" : (v.Rating > v.PreviousRating ? "  down" : "");
                    _cardRating[i].Text(BondPricing.RatingLabel(v.Rating) + trend);
                    _cardRating[i].Color(Theme.Rating(v.Rating), (int)v.Rating);
                    _cardSpread[i].Number("Spread ", v.Spread * 10000f, 0, "bp");
                }
                else
                {
                    _cardName[i].Text("");
                    _cardRating[i].Text("");
                    _cardSpread[i].Text("");
                }
            }

            _marketOffset = ClampOffset(_marketOffset, s.Market.Length);
            for (int i = 0; i < Rows; i++)
            {
                int k = _marketOffset + i;
                if (k < s.Market.Length)
                {
                    BondView b = s.Market[k];
                    _marketIds[i] = b.Id;
                    float spreadBp = Friction.HalfSpread(b.IssuerRating, (float)b.RemainingPeriods / BondPricing.PeriodsPerYear) * 10000f;
                    _marketRows[i].Text(string.Format("{0} [{1}]\nFace {2:N0}  -  {3:F2}%  -  {4} mo  -  price {5:N0} (+{6:F0}bp)",
                        b.IssuerName.Length > 0 ? b.IssuerName : b.Name, BondPricing.RatingLabel(b.IssuerRating),
                        b.FaceValue, b.CouponRate * 100f, b.RemainingPeriods, b.Price, spreadBp));
                    _buyButtons[i].isVisible = true;
                }
                else
                {
                    _marketIds[i] = null;
                    _marketRows[i].Text("");
                    _buyButtons[i].isVisible = false;
                }
            }
            _marketHint.Text(s.Market.Length > Rows
                ? string.Format("Showing {0}-{1} of {2}; scroll for more.", _marketOffset + 1, Math.Min(_marketOffset + Rows, s.Market.Length), s.Market.Length)
                : "");

            _holdingOffset = ClampOffset(_holdingOffset, s.Portfolio.Length);
            float value = 0f, unrealized = 0f;
            for (int j = 0; j < s.Portfolio.Length; j++)
            {
                value += s.Portfolio[j].Price;
                unrealized += s.Portfolio[j].Price + s.Portfolio[j].CouponsReceived - s.Portfolio[j].PurchasePrice;
            }
            for (int i = 0; i < Rows; i++)
            {
                int k = _holdingOffset + i;
                if (k < s.Portfolio.Length)
                {
                    BondView b = s.Portfolio[k];
                    _holdingIds[i] = b.Id;
                    float pl = b.Price + b.CouponsReceived - b.PurchasePrice;
                    _holdingRows[i].Text(string.Format("{0} [{1}]\nPaid {2:N0}  -  worth {3:N0}  -  P&L {4}  -  {5} mo",
                        b.IssuerName.Length > 0 ? b.IssuerName : b.Name, BondPricing.RatingLabel(b.IssuerRating),
                        b.PurchasePrice, b.Price, Wording.SignedMoney(pl), b.RemainingPeriods));
                    _holdingRows[i].Color(pl < 0f ? Theme.Warn : Theme.Text, pl < 0f ? 1 : 0);
                    _sellButtons[i].isVisible = true;
                }
                else
                {
                    _holdingIds[i] = null;
                    _holdingRows[i].Text(i == 0 && s.Portfolio.Length == 0 ? "The city holds no bonds." : "");
                    _sellButtons[i].isVisible = false;
                }
            }
            _holdingsTotal.Text(string.Format("Value {0:N0}  -  lifetime P&L {1}", value, Wording.SignedMoney(s.RealizedPL + unrealized)));
            _sellAll.isEnabled = s.Portfolio.Length > 0;
        }

        private static int ClampOffset(int offset, int count)
        {
            int max = Math.Max(0, count - Rows);
            return offset > max ? max : (offset < 0 ? 0 : offset);
        }

        private void OnMarketWheel(UIComponent c, UIMouseEventParameter p)
        {
            _marketOffset += p.wheelDelta < 0f ? 1 : -1;
            p.Use();
            Invalidate();
        }

        private void OnHoldingWheel(UIComponent c, UIMouseEventParameter p)
        {
            _holdingOffset += p.wheelDelta < 0f ? 1 : -1;
            p.Use();
            Invalidate();
        }

        private void OnBuy(int row)
        {
            if (_marketIds[row] != null) Submit(EngineCommand.BuyBond(_marketIds[row]));
        }

        private void OnSell(int row)
        {
            if (_holdingIds[row] != null) Submit(EngineCommand.SellBond(_holdingIds[row]));
        }
    }
}
