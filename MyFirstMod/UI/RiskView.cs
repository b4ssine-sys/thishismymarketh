using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // Risk: the rate-exposure gauge, the market regime, the city's swaps, and the
    // settings (including WO-39's text size and briefing replay). Replaces Hedging
    // and Settings.
    public sealed class RiskView : WorkspaceView
    {
        private const int SwapRows = 5;
        private const float GaugeMax = 1.5f;   // 150% hedged

        private Bar _gauge;
        private BoundLabel _gaugeText, _penalty;
        private BoundLabel _short, _curve, _cycle, _vol;

        private readonly BoundLabel[] _swapRows = new BoundLabel[SwapRows];
        private readonly UIButton[] _exitButtons = new UIButton[SwapRows];
        private readonly string[] _swapIds = new string[SwapRows];
        private BoundLabel _recommend;
        private UIButton _autoHedge, _sell25, _sell50, _exitAll;

        private BoundLabel _hazard, _rateVol, _trading, _revenue, _textSize;
        private EngineSnapshot _last;

        public RiskView(BondMarketWindow window, UIComponent parent) : base(window, parent) { }

        public override string Title { get { return "Risk"; } }

        protected override void BuildControls()
        {
            UIPanel gauge = Widgets.Card(Root, 0f, 0f, 410f, 96f);
            Widgets.Label(gauge, 10f, 2f, 390f, 20f, 0.75f, "Rate exposure",
                "How much of the city's debt is hedged with pay-fixed swaps. The tick marks 100%; past it the city is over-hedged and pays a rate penalty.").textColor = Theme.Muted;
            _gauge = new Bar(gauge, 10f, 30f, 390f, 14f, "Hedged share of debt (0-150%).");
            _gaugeText = Widgets.Figure(gauge, 10f, 50f, 390f, 20f, 0.8f, "Swap notional as a share of the city's debt.");
            _penalty = Widgets.Figure(gauge, 10f, 70f, 390f, 20f, 0.75f,
                "Over-hedging raises the city's own borrowing rate by 1.5% per 100% over, capped at 3%.");

            UIPanel regime = Widgets.Card(Root, 426f, 0f, 410f, 96f);
            Widgets.Label(regime, 10f, 2f, 390f, 20f, 0.75f, "Market regime",
                "The exogenous rate environment. Nothing the city does moves it.").textColor = Theme.Muted;
            _short = Widgets.Figure(regime, 10f, 22f, 390f, 18f, 0.75f, "The short rate swaps settle against.");
            _curve = Widgets.Figure(regime, 10f, 40f, 390f, 18f, 0.75f, "Spot yields at 2 and 10 years, and the curve's shape.");
            _cycle = Widgets.Figure(regime, 10f, 58f, 390f, 18f, 0.75f, "Where the business cycle is taking rates.");
            _vol = Widgets.Figure(regime, 10f, 76f, 390f, 18f, 0.75f, "Revenue volatility: how uneven the city's cash flow is.");

            UIPanel swaps = Widgets.Card(Root, 0f, 104f, 836f, 186f);
            Widgets.Label(swaps, 10f, 2f, 500f, 20f, 0.75f, "Interest rate swaps",
                "Pay-fixed swaps lock the city's borrowing cost; they settle monthly against the short rate.").textColor = Theme.Muted;
            for (int i = 0; i < SwapRows; i++)
            {
                int row = i;
                _swapRows[i] = Widgets.Figure(swaps, 10f, 24f + i * 24f, 700f, 22f, 0.72f,
                    "Direction, notional, fixed rate, months left, profit to date and the last monthly settlement.");
                _exitButtons[i] = Widgets.Button(swaps, "Exit", 740f, 24f + i * 24f, 80f, 22f,
                    "Close this swap at its mark-to-market value.", delegate(UIComponent c, UIMouseEventParameter p) { OnExit(row); });
            }
            _recommend = Widgets.Figure(swaps, 10f, 148f, 360f, 30f, 0.75f, "Hedging recommendation for the city's debt.");
            _autoHedge = Widgets.Button(swaps, "Auto-hedge", 380f, 150f, 110f, 28f,
                "Enter a pay-fixed swap on the unhedged debt at the par swap rate.",
                delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.AutoHedge()); });
            _sell25 = Widgets.Button(swaps, "Sell 25%", 500f, 150f, 100f, 28f, "Sell a quarter of every swap.",
                delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.SellAllSwapsTranche(0.25f)); });
            _sell50 = Widgets.Button(swaps, "Sell 50%", 610f, 150f, 100f, 28f, "Sell half of every swap.",
                delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.SellAllSwapsTranche(0.50f)); });
            _exitAll = Widgets.Button(swaps, "Exit all", 720f, 150f, 100f, 28f, "Close every swap.",
                delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.TerminateAllSwaps()); });

            UIPanel settings = Widgets.Card(Root, 0f, 298f, 836f, 126f);
            Widgets.Label(settings, 10f, 2f, 500f, 20f, 0.75f, "Settings",
                "Saved with the city, except text size and the briefing, which follow you across cities.").textColor = Theme.Muted;
            _hazard = SettingRow(settings, 10f, 24f, "Change", "How often market issuers default.", OnHazard);
            _rateVol = SettingRow(settings, 10f, 48f, "Change", "How much interest rates move.", OnRateVol);
            _trading = SettingRow(settings, 10f, 72f, "Toggle", "Citizens buy and sell city bonds.", OnTrading);
            _revenue = SettingRow(settings, 10f, 96f, "Toggle",
                "Revenue bonds backed by a utility's income (awaiting Gate B validation).", OnRevenue);
            _textSize = Widgets.Figure(settings, 430f, 24f, 200f, 22f, 0.75f, "Size of text in this window.");
            Widgets.Button(settings, "A-", 640f, 24f, 50f, 22f, "Smaller text", OnSmaller);
            Widgets.Button(settings, "A+", 700f, 24f, 50f, 22f, "Larger text", OnLarger);
            Widgets.Button(settings, "Show the briefing again", 430f, 60f, 320f, 26f,
                "Replay the three-card introduction.", delegate(UIComponent c, UIMouseEventParameter p) { Window.ShowBriefing(); });
        }

        private BoundLabel SettingRow(UIComponent parent, float x, float y, string button, string tooltip, MouseEventHandler onClick)
        {
            BoundLabel label = Widgets.Figure(parent, x, y, 300f, 22f, 0.75f, tooltip);
            Widgets.Button(parent, button, x + 310f, y, 90f, 22f, tooltip, onClick);
            return label;
        }

        protected override void Render(EngineSnapshot s)
        {
            _last = s;
            float ratio = s.TotalDebtFace > 0f ? s.TotalHedgedNotional / s.TotalDebtFace : (s.TotalHedgedNotional > 0f ? GaugeMax : 0f);
            _gauge.Set(ratio / GaugeMax, s.OverHedgeRatio > 0f ? Theme.Warn : Theme.BarFill, 1f / GaugeMax, -1f);
            _gaugeText.Text(Wording.HedgeStatus(s.TotalDebtFace, s.TotalHedgedNotional));
            _penalty.Text(s.OverHedgeRatio > 0f
                ? string.Format("Over-hedge penalty on the city's borrowing rate: +{0:F2}%", System.Math.Min(s.OverHedgeRatio * 1.5f, 3f))
                : "");
            _penalty.Color(Theme.Warn, 1);

            float spot2 = s.AuctionFairYield(24), spot10 = s.AuctionFairYield(120);
            _short.Text(string.Format("Short rate {0:F2}%  -  city borrows at {1:F2}%", s.ShortRate * 100f, s.RequiredYield * 100f));
            _curve.Text(string.Format("2 yr {0:F2}%  -  10 yr {1:F2}%  -  {2}", spot2 * 100f, spot10 * 100f,
                Wording.CurveShape(s.ShortRate, spot10)));
            _cycle.Text(Wording.RateCycle(s.CyclePhase));
            _vol.Percent("Revenue volatility: ", s.RevenueVolatility, 0);

            for (int i = 0; i < SwapRows; i++)
            {
                if (i < s.Swaps.Length)
                {
                    SwapView w = s.Swaps[i];
                    _swapIds[i] = w.Id;
                    _swapRows[i].Text(string.Format("{0}  {1}  -  notional {2:N0}  -  fixed {3:F2}%  -  {4} mo  -  P&L {5}  -  last {6}{7}",
                        w.Id, w.PayFixed ? "pay fixed" : "receive fixed", w.NotionalAmount, w.FixedRate * 100f,
                        w.RemainingPeriods, Wording.SignedMoney(w.CumulativePL), Wording.SignedMoney(w.LastSettlement),
                        w.UnpaidSettlement > 0f ? string.Format("  -  owes {0:N0}", w.UnpaidSettlement) : ""));
                    _exitButtons[i].isVisible = true;
                }
                else
                {
                    _swapIds[i] = null;
                    _swapRows[i].Text(i == 0 && s.Swaps.Length == 0 ? "No swaps." : "");
                    _exitButtons[i].isVisible = false;
                }
            }
            _recommend.Text(s.RecommendedHedge);
            bool hasSwaps = s.Swaps.Length > 0;
            _autoHedge.isEnabled = s.SwapCount < s.MaxActiveSwaps && s.IssuedCount > 0;
            _sell25.isEnabled = hasSwaps;
            _sell50.isEnabled = hasSwaps;
            _exitAll.isEnabled = hasSwaps;

            _hazard.Text("Issuer defaults: " + HazardName(s.HazardMultiplier));
            _rateVol.Text("Rate volatility: " + VolName(s.RateVolatilityScale));
            _trading.Text("Citizen trading: " + (s.CitizenTradingEnabled ? "on" : "off"));
            _revenue.Text("Revenue bonds: " + (s.RevenueBondsEnabled ? "on" : "off"));
            _textSize.Number("Text size ", UiPrefs.TextScale * 100f, 0, "%");
        }

        private static string HazardName(float h)
        {
            if (h <= IssuerModel.HAZARD_HISTORICAL + 0.1f) return "Historical (rare)";
            if (h >= IssuerModel.HAZARD_VOLATILE - 0.1f) return "Volatile";
            return "Standard";
        }

        private static string VolName(float v)
        {
            if (v <= 0.6f) return "Calm";
            if (v >= 1.8f) return "Turbulent";
            return "Normal";
        }

        private void OnHazard(UIComponent c, UIMouseEventParameter p)
        {
            if (_last == null) return;
            float h = _last.HazardMultiplier;
            float next = h <= IssuerModel.HAZARD_HISTORICAL + 0.1f ? IssuerModel.HAZARD_STANDARD
                : (h >= IssuerModel.HAZARD_VOLATILE - 0.1f ? IssuerModel.HAZARD_HISTORICAL : IssuerModel.HAZARD_VOLATILE);
            Submit(EngineCommand.SetHazardMultiplier(next));
        }

        private void OnRateVol(UIComponent c, UIMouseEventParameter p)
        {
            if (_last == null) return;
            float v = _last.RateVolatilityScale;
            Submit(EngineCommand.SetRateVolatility(v <= 0.6f ? 1f : (v >= 1.8f ? 0.5f : 2f)));
        }

        private void OnTrading(UIComponent c, UIMouseEventParameter p)
        {
            if (_last != null) Submit(EngineCommand.SetCitizenTrading(!_last.CitizenTradingEnabled));
        }

        private void OnRevenue(UIComponent c, UIMouseEventParameter p)
        {
            if (_last != null) Submit(EngineCommand.SetRevenueBonds(!_last.RevenueBondsEnabled));
        }

        private void OnSmaller(UIComponent c, UIMouseEventParameter p) { Window.SetTextScale(UiPrefs.TextScale - 0.1f); }
        private void OnLarger(UIComponent c, UIMouseEventParameter p) { Window.SetTextScale(UiPrefs.TextScale + 0.1f); }

        private void OnExit(int row)
        {
            if (_swapIds[row] != null) Submit(EngineCommand.TerminateSwap(_swapIds[row]));
        }
    }
}
