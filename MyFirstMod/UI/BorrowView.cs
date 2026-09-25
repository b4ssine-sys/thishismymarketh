using System;
using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // Borrow: the issuance ticket with a live what-if preview (WO-36), the
    // 36-month maturity ladder (WO-37), and the city's outstanding bonds.
    public sealed class BorrowView : WorkspaceView
    {
        private const float MinSpread = -0.03f;
        private const float MaxSpread = 0.06f;
        private const float SpreadStep = 0.0005f;   // 5bp
        private const int LadderMonths = MaturityLadder.DefaultMonths;
        private const float LadderHeight = 80f;
        private const int IssuedRows = 5; // the engine's bond slots

        private int _template = IssueTemplates.EmergencyNote;
        private float _spread;
        private bool _followClearingPrice = true;
        private bool _settingSlider;
        private EngineSnapshot _last;

        private BoundLabel _templateLabel, _templateNote, _yieldLabel;
        private UISlider _slider;
        private BoundLabel _cover, _fill, _coupon, _dscr, _rating, _warning, _blocked;
        private UIButton _issueButton;

        private readonly UIPanel[] _ladderBars = new UIPanel[LadderMonths];
        private BoundLabel _ladderNote;

        private readonly BoundLabel[] _issuedRows = new BoundLabel[IssuedRows];
        private readonly UIButton[] _repayButtons = new UIButton[IssuedRows];
        private readonly string[] _issuedIds = new string[IssuedRows];
        private UIButton _pay25, _pay50;

        public BorrowView(BondMarketWindow window, UIComponent parent) : base(window, parent) { }

        public override string Title { get { return "Borrow"; } }

        // Open the ticket on a template (from the Treasury recommendation or the
        // briefing), at the clearing price.
        public void SelectTemplate(int template)
        {
            if (IssueTemplates.IsValid(template)) _template = template;
            _followClearingPrice = true;
            Invalidate();
        }

        protected override void BuildControls()
        {
            UIPanel ticket = Widgets.Card(Root, 0f, 0f, 420f, 424f);
            Widgets.Label(ticket, 10f, 2f, 400f, 20f, 0.75f, "Issuance ticket",
                "Price a bond before you issue it. You choose the yield; investors decide how much of it they buy.").textColor = Theme.Muted;

            Widgets.Button(ticket, "<", 10f, 26f, 30f, 28f, "Previous bond type", OnPrevTemplate);
            _templateLabel = Widgets.Figure(ticket, 46f, 26f, 328f, 28f, 0.9f, "Bond type, face value and term.");
            _templateLabel.Label.textAlignment = UIHorizontalAlignment.Center;
            Widgets.Button(ticket, ">", 380f, 26f, 30f, 28f, "Next bond type", OnNextTemplate);
            _templateNote = Widgets.Figure(ticket, 10f, 56f, 400f, 20f, 0.7f, null);
            _templateNote.Color(Theme.Muted, 1);

            _yieldLabel = Widgets.Figure(ticket, 10f, 80f, 400f, 22f, 0.85f,
                "The yield you offer investors. Fair value is the market's rate for this term; offering more than the city's price attracts more bids.");

            _slider = ticket.AddUIComponent<UISlider>();
            _slider.size = new Vector2(250f, 14f);
            _slider.relativePosition = new Vector3(10f, 110f);
            _slider.backgroundSprite = "ScrollbarTrack";
            _slider.minValue = MinSpread;
            _slider.maxValue = MaxSpread;
            _slider.stepSize = SpreadStep;
            UISprite thumb = _slider.AddUIComponent<UISprite>();
            thumb.spriteName = "ScrollbarThumb";
            thumb.size = new Vector2(12f, 18f);
            _slider.thumbObject = thumb;
            _slider.tooltip = "Drag to change the offered yield (5bp steps).";
            _slider.eventValueChanged += OnSlider;

            Widgets.Button(ticket, "-5bp", 268f, 104f, 44f, 26f, "Offer 5bp less", OnMinus);
            Widgets.Button(ticket, "+5bp", 316f, 104f, 44f, 26f, "Offer 5bp more", OnPlus);
            Widgets.Button(ticket, "Clear", 364f, 104f, 46f, 26f,
                "Set the yield where investors bid for exactly the whole issue (cover 1.0x).", OnClearing);

            float y = 142f;
            _cover = Widgets.Figure(ticket, 10f, y, 400f, 22f, 0.85f,
                "Expected bid-to-cover: bids divided by bonds offered. Below 0.75x the auction fails; between 0.75x and 1x it fills partly.");
            _fill = Widgets.Figure(ticket, 10f, y + 26f, 400f, 22f, 0.8f,
                "How much of the issue investors take, and the cash the city receives after the 75bp underwriting fee.");
            _coupon = Widgets.Figure(ticket, 10f, y + 50f, 400f, 22f, 0.8f,
                "What the placed bonds cost the city in coupons each year.");
            _dscr = Widgets.Figure(ticket, 10f, y + 74f, 400f, 22f, 0.8f,
                "Debt service coverage before and after this deal.");
            _rating = Widgets.Figure(ticket, 10f, y + 98f, 400f, 22f, 0.8f,
                "The city's credit rating before and after this deal.");
            _warning = Widgets.Figure(ticket, 10f, y + 124f, 400f, 40f, 0.75f, null);
            _warning.Color(Theme.Warn, 1);

            _issueButton = Widgets.Button(ticket, "Issue", 10f, 344f, 400f, 32f, null, OnIssue);
            _blocked = Widgets.Figure(ticket, 10f, 380f, 400f, 40f, 0.75f, null);
            _blocked.Color(Theme.Bad, 1);

            UIPanel ladderCard = Widgets.Card(Root, 436f, 0f, 400f, 132f);
            Widgets.Label(ladderCard, 10f, 2f, 380f, 20f, 0.75f, "Payments due, next 36 months",
                "Coupon and principal owed each month. Amber: projected cash falls short within two months.").textColor = Theme.Muted;
            for (int i = 0; i < LadderMonths; i++)
            {
                _ladderBars[i] = Widgets.Box(ladderCard, 10f + i * 10.5f, 24f + LadderHeight, 9f, 0f, Theme.BarFill, "");
            }
            _ladderNote = Widgets.Figure(ladderCard, 10f, 108f, 380f, 20f, 0.7f,
                "Whether projected cash covers every payment on the ladder.");

            UIPanel debtCard = Widgets.Card(Root, 436f, 140f, 400f, 284f);
            Widgets.Label(debtCard, 10f, 2f, 380f, 20f, 0.75f, "Outstanding city bonds",
                "Owed: principal still outstanding plus any arrears. Repay retires a bond in full.").textColor = Theme.Muted;
            for (int i = 0; i < IssuedRows; i++)
            {
                int row = i;
                _issuedRows[i] = Widgets.Figure(debtCard, 10f, 26f + i * 42f, 300f, 38f, 0.72f,
                    "A city bond: its state, what is owed (principal plus arrears), its coupon and the months to maturity.");
                _repayButtons[i] = Widgets.Button(debtCard, "Repay", 316f, 30f + i * 42f, 74f, 28f,
                    "Pay off this bond in full now.", delegate(UIComponent c, UIMouseEventParameter p) { OnRepay(row); });
            }
            _pay25 = Widgets.Button(debtCard, "Pay down 25%", 10f, 244f, 185f, 30f,
                "Spend up to 25% of all debt owed, retiring whole bonds first.", delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.PayDebtPercent(0.25f)); });
            _pay50 = Widgets.Button(debtCard, "Pay down 50%", 205f, 244f, 185f, 30f,
                "Spend up to 50% of all debt owed, retiring whole bonds first.", delegate(UIComponent c, UIMouseEventParameter p) { Submit(EngineCommand.PayDebtPercent(0.50f)); });
        }

        protected override void Render(EngineSnapshot s)
        {
            _last = s;
            if (!s.IsTemplateAvailable(_template)) _template = FirstAvailable(s, _template, 1);
            RenderTicket(s);
            RenderLadder(s);
            RenderIssued(s);
        }

        private void RenderTicket(EngineSnapshot s)
        {
            float clearing = s.SpreadForFullCover.Length > _template ? s.SpreadForFullCover[_template] : 0f;
            if (_followClearingPrice && !float.IsNaN(clearing)) _spread = Clamp(RoundToStep(clearing + 0.00049f));

            _templateLabel.Text(string.Format("{0}  -  {1:N0}  -  {2} yr",
                s.GetTemplateName(_template), s.GetTemplateFace(_template), s.GetTemplatePeriods(_template) / 12));
            RevenueSource src = s.GetTemplateRevenue(_template);
            _templateNote.Text(src == RevenueSource.None ? "General obligation: backed by all city revenue."
                : "Revenue bond: backed by " + BondPricing.RevenueLabel(src) + " income.");

            int periods = s.GetTemplatePeriods(_template);
            float offered = s.TemplateOfferedYield(_template) + _spread;
            if (offered < AuctionPricing.MinOfferedYield) offered = AuctionPricing.MinOfferedYield;
            float fair = s.AuctionFairYield(periods);
            _yieldLabel.Text(string.Format("Offered yield {0:F2}%  (fair value {1:F2}%, {2:+0;-0}bp)",
                offered * 100f, fair * 100f, (offered - fair) * 10000f));

            _settingSlider = true;
            if (Math.Abs(_slider.value - _spread) > 0.00001f) _slider.value = _spread;
            _settingSlider = false;

            IssuancePreviewResult p = s.PreviewIssue(_template, _spread);
            _cover.Text(string.Format("Expected bid-to-cover: {0:F2}x", p.Cover));
            _cover.Color(Theme.Cover(p.Cover), p.Cover >= 1f ? 2 : (p.Cover >= PrimaryAuction.MinCover ? 1 : 0));
            _fill.Text(p.Fills
                ? string.Format("Fills {0:F0}%: city receives {1:N0} (fee {2:N0})", p.FilledFraction * 100f, p.Proceeds, p.Fee)
                : "Would not fill: the city receives nothing");
            _coupon.Text(string.Format("Coupon cost: {0:N0} a year", p.AnnualCoupon));
            _dscr.Text(s.Explanation.Unlevered && p.PlacedFace <= 0f ? "Coverage: no debt"
                : string.Format("Coverage (DSCR): {0} -> {1}", Dscr(s.Metrics), Dscr(p.After)));
            _rating.Text(string.Format("Rating: {0} -> {1}",
                BondPricing.RatingLabel(p.RatingBefore), BondPricing.RatingLabel(p.RatingAfter)));
            _rating.Color(p.RatingAfter > p.RatingBefore ? Theme.Warn : Theme.Text, p.RatingAfter > p.RatingBefore ? 1 : 0);

            string warning = "";
            if (p.OverCapacity) warning = "Too large: the market cannot absorb this much more city debt.";
            else if (!p.Fills) warning = "At this yield the auction fails. Offer more, or press Clear.";
            else if (p.FilledFraction < 0.999f) warning = "Partial fill: raise the yield to sell the whole issue.";
            _warning.Text(warning);

            bool available = s.IsTemplateAvailable(_template);
            bool open = s.CanIssueBonds && available;
            _issueButton.text = string.Format("Issue at {0:F2}%", offered * 100f);
            Widgets.Enable(_issueButton, open, null, "Send this order; it runs on the next simulation tick.");
            _blocked.Text(open ? "" : "Issuance closed: " + BlockReason(s, available));
        }

        private void RenderLadder(EngineSnapshot s)
        {
            float maxDue = 1f;
            for (int i = 0; i < s.Ladder.Length && i < LadderMonths; i++)
                if (s.Ladder[i].Due > maxDue) maxDue = s.Ladder[i].Due;

            int amber = 0;
            for (int i = 0; i < LadderMonths; i++)
            {
                UIPanel bar = _ladderBars[i];
                if (i >= s.Ladder.Length)
                {
                    bar.height = 0f;
                    continue;
                }
                LadderMonth m = s.Ladder[i];
                float h = m.Due <= 0.01f ? 0f : Math.Max(2f, LadderHeight * m.Due / maxDue);
                bar.height = h;
                bar.relativePosition = new Vector3(10f + i * 10.5f, 24f + LadderHeight - h);
                Color32 c = m.Imminent ? Theme.Warn : (m.Shortfall ? Theme.Bad : Theme.BarFill);
                bar.color = c;
                if (m.Imminent) amber++;
                bar.tooltip = string.Format("Month {0}: due {1:N0} (coupon {2:N0}, principal {3:N0}, arrears {4:N0}); projected cash after {5:N0}",
                    m.Offset, m.Due, m.Coupon, m.Principal, m.Arrears, m.ProjectedCash);
            }

            int first = MaturityLadder.FirstShortfall(s.Ladder);
            if (s.IssuedCount == 0) _ladderNote.Text("No debt outstanding.");
            else if (amber > 0) _ladderNote.Text("Amber: cash falls short within two months. Act now.");
            else if (first > 0) _ladderNote.Text(string.Format("Projected shortfall in month {0}.", first));
            else _ladderNote.Text("Every payment is covered by projected cash.");
            _ladderNote.Color(amber > 0 ? Theme.Warn : (first > 0 ? Theme.Bad : Theme.Muted), amber > 0 ? 2 : (first > 0 ? 1 : 0));
        }

        private void RenderIssued(EngineSnapshot s)
        {
            for (int i = 0; i < IssuedRows; i++)
            {
                if (i < s.Issued.Length)
                {
                    BondView b = s.Issued[i];
                    _issuedIds[i] = b.Id;
                    string state = b.State == BondState.Active
                        ? (b.PlacedFraction >= 0.99f ? "active" : string.Format("{0:F0}% placed", b.PlacedFraction * 100f))
                        : b.State.ToString().ToLowerInvariant();
                    _issuedRows[i].Text(string.Format("{0} [{1}]\nOwed {2:N0}  -  {3:F2}%  -  {4} mo left{5}",
                        b.Name, state, b.OutstandingPrincipal + b.Arrears, b.CouponRate * 100f, b.RemainingPeriods,
                        b.Arrears > 0.01f ? string.Format("  -  arrears {0:N0}", b.Arrears) : ""));
                    bool distressed = b.State == BondState.Delinquent || b.State == BondState.Defaulted;
                    _issuedRows[i].Color(distressed ? Theme.Warn : Theme.Text, distressed ? 1 : 0);
                    _repayButtons[i].isVisible = true;
                }
                else
                {
                    _issuedIds[i] = null;
                    _issuedRows[i].Text(i == 0 && s.Issued.Length == 0 ? "No bonds outstanding." : "");
                    _repayButtons[i].isVisible = false;
                }
            }
            bool hasDebt = s.Issued.Length > 0;
            _pay25.isEnabled = hasDebt;
            _pay50.isEnabled = hasDebt;
        }

        private static string Dscr(CreditMetrics m)
        {
            return m.AnnualDebtService <= 1f ? "none" : m.DSCR.ToString("F2") + "x";
        }

        private static string BlockReason(EngineSnapshot s, bool available)
        {
            if (!available) return "revenue bonds are switched off in Risk > Settings.";
            if (s.IssuedCount >= s.MaxIssuedBonds) return "all five bond slots are in use.";
            if (s.Rating == CreditRating.D) return "the city is rated D.";
            if (s.HasArrears) return "a bond is in default or arrears.";
            if (s.DemandScore < CimDemandEngine.MIN_ISSUABLE_DEMAND) return "investor demand is too low.";
            if (s.RemainingCapacity < 1000f) return "the market cannot absorb more city debt.";
            return "the post-default lock-out has not elapsed.";
        }

        private static int FirstAvailable(EngineSnapshot s, int from, int step)
        {
            int n = IssueTemplates.Count;
            for (int k = 1; k <= n; k++)
            {
                int t = ((from + step * k) % n + n) % n;
                if (s.IsTemplateAvailable(t)) return t;
            }
            return IssueTemplates.EmergencyNote;
        }

        private static float Clamp(float v) { return v < MinSpread ? MinSpread : (v > MaxSpread ? MaxSpread : v); }
        private static float RoundToStep(float v) { return (float)Math.Floor(v / SpreadStep) * SpreadStep; }

        private void SetSpread(float spread)
        {
            _spread = Clamp(spread);
            _followClearingPrice = false;
            Invalidate();
        }

        private void OnSlider(UIComponent c, float value)
        {
            if (_settingSlider) return;
            SetSpread(value);
        }

        private void OnMinus(UIComponent c, UIMouseEventParameter p) { SetSpread(_spread - SpreadStep); }
        private void OnPlus(UIComponent c, UIMouseEventParameter p) { SetSpread(_spread + SpreadStep); }

        private void OnClearing(UIComponent c, UIMouseEventParameter p)
        {
            _followClearingPrice = true;
            Invalidate();
        }

        private void OnPrevTemplate(UIComponent c, UIMouseEventParameter p)
        {
            if (_last != null) SelectTemplate(FirstAvailable(_last, _template, -1));
        }

        private void OnNextTemplate(UIComponent c, UIMouseEventParameter p)
        {
            if (_last != null) SelectTemplate(FirstAvailable(_last, _template, 1));
        }

        private void OnIssue(UIComponent c, UIMouseEventParameter p)
        {
            Submit(EngineCommand.IssueBond(_template, _spread));
            Window.ShowPending("Issuing " + IssueTemplates.Name(_template) + "...");
        }

        private void OnRepay(int row)
        {
            string id = _issuedIds[row];
            if (id != null) Submit(EngineCommand.RepayBond(id));
        }
    }
}
