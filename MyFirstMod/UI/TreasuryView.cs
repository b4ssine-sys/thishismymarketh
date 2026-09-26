using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // Treasury: the rating badge and what decides it (WO-35), cash runway, the next
    // three payments due, one recommendation, and the latest quarterly report.
    public sealed class TreasuryView : WorkspaceView
    {
        private BoundLabel _badge, _ratingCaption, _status;
        private BoundLabel _dscrValue, _burdenValue, _reservesValue;
        private Bar _dscrBar, _burdenBar, _reservesBar;
        private BoundLabel _upText, _downText;
        private BoundLabel _cash, _runway;
        private readonly BoundLabel[] _payments = new BoundLabel[3];
        private BoundLabel _advice;
        private UIButton _adviceButton;
        private Advice _currentAdvice;
        private readonly BoundLabel[] _report = new BoundLabel[4];
        private ChartSprite _ratingChart;
        private readonly float[] _chartValues = new float[60];
        private int _chartPeriod = -1;

        private static readonly string[] ReportTips =
        {
            "The quarter's rating, debt service coverage and debt burden.",
            "Annual operating revenue, expenses and the surplus between them.",
            "Population, share of citizens employed, and citizen confidence.",
            "The rating agency's one-line outlook."
        };

        private const float DscrScale = 3f;
        private const float BurdenScale = 0.5f;
        private const float ReservesScale = 12f;

        public TreasuryView(BondMarketWindow window, UIComponent parent) : base(window, parent) { }

        public override string Title { get { return "Treasury"; } }

        protected override void BuildControls()
        {
            UIPanel badgeCard = Widgets.Card(Root, 0f, 0f, 150f, 104f);
            _badge = Widgets.Figure(badgeCard, 0f, 6f, 150f, 64f, 3.0f,
                "The city's credit rating. It sets the yield investors demand on city bonds.");
            _badge.Label.textAlignment = UIHorizontalAlignment.Center;
            _ratingCaption = Widgets.Figure(badgeCard, 0f, 72f, 150f, 26f, 0.75f, null);
            _ratingCaption.Label.textAlignment = UIHorizontalAlignment.Center;

            _status = Widgets.Figure(Root, 160f, 0f, 250f, 104f, 0.8f,
                "Credit standing and the default penalty on the city's yield.");

            float y = 116f;
            _dscrValue = Widgets.Figure(Root, 0f, y, 400f, 20f, 0.8f,
                "Debt service coverage: annual operating surplus divided by annual debt service. Higher is safer.");
            _dscrBar = new Bar(Root, 0f, y + 22f, 400f, 10f,
                "Green tick: the coverage that earns the next notch up. Amber tick: the floor that holds this rating.");
            y += 48f;
            _burdenValue = Widgets.Figure(Root, 0f, y, 400f, 20f, 0.8f,
                "Debt burden: annual debt service as a share of operating revenue. Lower is safer.");
            _burdenBar = new Bar(Root, 0f, y + 22f, 400f, 10f,
                "Green tick: the burden that earns the next notch up. Amber tick: the ceiling that holds this rating.");
            y += 48f;
            _reservesValue = Widgets.Figure(Root, 0f, y, 400f, 20f, 0.8f,
                "Months of reserves: treasury cash divided by a month of operating expense. 6+ lifts the rating a notch; under 1 cuts it.");
            _reservesBar = new Bar(Root, 0f, y + 22f, 400f, 10f,
                "Green tick: reserves that earn the next notch up. Amber tick: reserves below which the rating drops.");
            y += 52f;
            _upText = Widgets.Figure(Root, 0f, y, 400f, 40f, 0.8f, "What would lift the rating one notch.");
            _upText.Color(Theme.Good, 1);
            _downText = Widgets.Figure(Root, 0f, y + 44f, 400f, 40f, 0.8f, "What would cost the rating one notch.");
            _downText.Color(Theme.Warn, 1);

            Widgets.Label(Root, 0f, 356f, 400f, 16f, 0.65f, "Rating over the last five years (dotted line: BBB, investment grade)",
                null).textColor = Theme.Muted;
            _ratingChart = new ChartSprite(Root, 0f, 374f, 400, 48,
                "The city's rating each month for the last five years. Higher is better.");

            UIPanel cashCard = Widgets.Card(Root, 420f, 0f, 416f, 66f);
            _cash = Widgets.Figure(cashCard, 10f, 4f, 396f, 28f, 1.1f,
                "Cash in the treasury at the last simulation tick.");
            _runway = Widgets.Figure(cashCard, 10f, 34f, 396f, 26f, 0.8f,
                "Cash runway at the current monthly operating surplus or deficit.");

            UIPanel payCard = Widgets.Card(Root, 420f, 74f, 416f, 92f);
            Widgets.Label(payCard, 10f, 2f, 396f, 20f, 0.75f, "Next payments due",
                "Coupon and principal the city owes, from the maturity ladder.").textColor = Theme.Muted;
            for (int i = 0; i < _payments.Length; i++)
                _payments[i] = Widgets.Figure(payCard, 10f, 24f + i * 22f, 396f, 20f, 0.8f,
                    "A scheduled debt payment and the cash projected to remain afterwards.");

            UIPanel adviceCard = Widgets.Card(Root, 420f, 174f, 416f, 116f);
            Widgets.Label(adviceCard, 10f, 2f, 396f, 20f, 0.75f, "Recommendation", null).textColor = Theme.Muted;
            _advice = Widgets.Figure(adviceCard, 10f, 22f, 396f, 56f, 0.8f, "The most useful thing to do next.");
            _adviceButton = Widgets.Button(adviceCard, "", 10f, 82f, 220f, 26f, null, OnAdvice);

            UIPanel reportCard = Widgets.Card(Root, 420f, 298f, 416f, 126f);
            Widgets.Label(reportCard, 10f, 2f, 396f, 20f, 0.75f, "Latest quarterly report",
                "Quarter-end figures. They explain the rating; they do not change on their own between quarters.").textColor = Theme.Muted;
            for (int i = 0; i < _report.Length; i++)
                _report[i] = Widgets.Figure(reportCard, 10f, 24f + i * 24f, 396f, 22f, 0.75f, ReportTips[i]);
        }

        protected override void Render(EngineSnapshot s)
        {
            RatingExplanation e = s.Explanation;
            _badge.Text(BondPricing.RatingLabel(s.Rating));
            _badge.Color(Theme.Rating(s.Rating), (int)s.Rating);
            _ratingCaption.Text(e.HardFloorD ? "In default" : "Credit rating");
            _status.Text(s.CreditStatusLabel + (s.DefaultPenalty > 0
                ? string.Format("\nDefault penalty: +{0:F2}% on yield", s.DefaultPenalty * 0.25f) : "")
                + string.Format("\nCity borrows at {0:F2}%", s.RequiredYield * 100f));

            if (e.Unlevered)
            {
                _dscrValue.Text(s.NOI > 0f ? "Coverage: no debt yet (strong)" : "Coverage: no debt yet, operating deficit");
                _dscrBar.Set(s.NOI > 0f ? 1f : 0.33f, Theme.BarFill, -1f, -1f);
                _burdenValue.Text("Debt burden: 0% (no debt)");
                _burdenBar.Set(0f, Theme.BarFill, -1f, -1f);
            }
            else
            {
                _dscrValue.Number("Debt service coverage (DSCR): ", s.DSCR, 2, "x");
                _dscrBar.Set(s.DSCR / DscrScale, ColourFor(e.DscrDown, s.DSCR, true),
                    e.DscrUp.Possible ? e.DscrUp.Target / DscrScale : -1f,
                    e.DscrDown.Possible ? e.DscrDown.Target / DscrScale : -1f);
                _burdenValue.Percent("Debt burden: ", s.DebtBurden, 1);
                _burdenBar.Set(s.DebtBurden / BurdenScale, ColourFor(e.BurdenDown, s.DebtBurden, false),
                    e.BurdenUp.Possible ? e.BurdenUp.Target / BurdenScale : -1f,
                    e.BurdenDown.Possible ? e.BurdenDown.Target / BurdenScale : -1f);
            }
            _reservesValue.Number("Months of reserves: ", s.MonthsOfReserves, 1, "");
            _reservesBar.Set(s.MonthsOfReserves / ReservesScale, ColourFor(e.ReservesDown, s.MonthsOfReserves, true),
                e.ReservesUp.Possible ? e.ReservesUp.Target / ReservesScale : -1f,
                e.ReservesDown.Possible ? e.ReservesDown.Target / ReservesScale : -1f);
            _upText.Text(e.UpText);
            _downText.Text(e.DownText);
            if (s.PeriodCounter != _chartPeriod) DrawRatingChart(s);

            _cash.Number("Treasury: ", s.CashBalance, 0, "");
            _runway.Text("Runway: " + Wording.Runway(s.CashBalance, s.NOI / BondPricing.PeriodsPerYear));

            int shown = 0;
            for (int i = 0; i < s.Ladder.Length && shown < _payments.Length; i++)
            {
                LadderMonth m = s.Ladder[i];
                if (m.Due <= 0.01f) continue;
                _payments[shown].Text(string.Format("In {0} month(s): {1:N0}{2}  (cash after: {3:N0})",
                    m.Offset, m.Due, m.Principal > 0f ? " incl. principal" : "", m.ProjectedCash));
                _payments[shown].Color(m.Shortfall ? Theme.Warn : Theme.Text, m.Shortfall ? 1 : 0);
                shown++;
            }
            for (int i = shown; i < _payments.Length; i++)
                _payments[i].Text(i == 0 ? "Nothing due: the city has no debt." : "");

            _currentAdvice = Advisor.Recommend(s);
            _advice.Text(_currentAdvice.Text);
            bool hasAction = _currentAdvice.Action != AdviceAction.None;
            _adviceButton.isVisible = hasAction;
            if (hasAction && _adviceButton.text != _currentAdvice.ActionLabel) _adviceButton.text = _currentAdvice.ActionLabel;

            if (s.Reports.Length == 0)
            {
                _report[0].Text("The first report arrives after three months.");
                for (int i = 1; i < _report.Length; i++) _report[i].Text("");
            }
            else
            {
                QuarterlyReport rp = s.Reports[s.Reports.Length - 1];
                _report[0].Text(string.Format("Q{0}: {1}, DSCR {2:F2}, burden {3:F1}%",
                    rp.Quarter, BondPricing.RatingLabel(rp.Rating), rp.DSCR, rp.DebtBurden * 100f));
                _report[1].Text(string.Format("Revenue {0:N0}, expenses {1:N0}, surplus {2}",
                    rp.GrossIncome, rp.TotalExpenses, Wording.SignedMoney(rp.NOI)));
                _report[2].Text(string.Format("Population {0:N0}, jobs {1:F0}%, confidence {2:F0}%",
                    rp.Population, rp.EmploymentRate * 100f, rp.CitizenConfidence * 100f));
                _report[3].Text(rp.Outlook);
            }
        }

        // WO-43: redrawn into its texture once per period.
        private void DrawRatingChart(EngineSnapshot s)
        {
            _chartPeriod = s.PeriodCounter;
            ChartSprite c = _ratingChart;
            ChartRaster.Clear(c.Pixels, ChartSprite.Pack(Theme.BarTrack));
            const float top = 7f; // AAA plots highest
            ChartRaster.Gridline(c.Pixels, c.Width, c.Height, top - (int)CreditRating.BBB, 0f, top, ChartSprite.Pack(Theme.Muted));
            int n = s.RatingHistory.Length < _chartValues.Length ? s.RatingHistory.Length : _chartValues.Length;
            for (int i = 0; i < n; i++) _chartValues[i] = top - (int)s.RatingHistory[s.RatingHistory.Length - n + i];
            ChartRaster.Steps(c.Pixels, c.Width, c.Height, _chartValues, n, 0f, top, ChartSprite.Pack(Theme.Accent));
            c.Upload();
        }

        // Amber when within a small margin of the threshold that holds the rating.
        private static Color32 ColourFor(FactorGap down, float value, bool higherIsBetter)
        {
            if (!down.Possible) return Theme.BarFill;
            float margin = higherIsBetter ? value - down.Target : down.Target - value;
            float scale = down.Target > 0.0001f ? down.Target : 1f;
            return margin / scale < 0.1f ? Theme.Warn : Theme.BarFill;
        }

        private void OnAdvice(UIComponent c, UIMouseEventParameter p)
        {
            if (_currentAdvice == null) return;
            switch (_currentAdvice.Action)
            {
                case AdviceAction.OpenBorrow: Window.OpenBorrow(_currentAdvice.TemplateIndex); break;
                case AdviceAction.PayDown: Window.OpenBorrow(-1); break;
                case AdviceAction.OpenRisk: Window.OpenWorkspace(BondMarketWindow.RiskIndex); break;
                case AdviceAction.OpenInvest: Window.OpenWorkspace(BondMarketWindow.InvestIndex); break;
            }
        }
    }
}
