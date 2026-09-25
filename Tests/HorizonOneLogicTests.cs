// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    // Horizon 1 (WO-35 ... WO-38): the pure logic behind the new screens.
    public class HorizonOneLogicTests
    {
        private const int PPY = 12;

        private static CreditMetrics Levered(float dscr, float burden, float reserves)
        {
            var m = new CreditMetrics();
            m.AnnualOperatingRevenue = 1000000f;
            m.AnnualDebtService = burden * m.AnnualOperatingRevenue;
            m.AnnualNOI = dscr * m.AnnualDebtService;
            m.AnnualOperatingExpense = m.AnnualOperatingRevenue - m.AnnualNOI;
            m.DSCR = dscr;
            m.DebtBurden = burden;
            m.MonthsOfReserves = reserves;
            return m;
        }

        // ---- WO-35: the rating explains itself ----

        [Fact]
        public void Explainer_DscrOrReservesLiftAToAA()
        {
            var e = RatingExplainer.Explain(Levered(1.90f, 0.10f, 3f), false);
            Assert.Equal(CreditRating.A, e.Rating);
            Assert.Equal(CreditRating.AA, e.UpRating);
            Assert.True(e.DscrUp.Possible);
            Assert.Equal(0.10f, e.DscrUp.Delta, 3);
            Assert.True(e.ReservesUp.Possible);
            Assert.Equal(3.0f, e.ReservesUp.Delta, 3);
            Assert.False(e.BurdenUp.Possible); // burden alone cannot lift DSCR 1.9 to AA
            Assert.Equal("+0.10 DSCR or +3.0 months of reserves to reach AA.", e.UpText);
        }

        [Fact]
        public void Explainer_ReportsMarginBeforeDowngrade()
        {
            var e = RatingExplainer.Explain(Levered(1.90f, 0.10f, 3f), false);
            Assert.Equal(CreditRating.BBB, e.DownRating);
            Assert.Equal(0.40f, e.DscrDown.Delta, 3);     // DSCR 1.50 holds A
            Assert.Equal(0.08f, e.BurdenDown.Delta, 3);   // burden 18% holds A
            Assert.Equal(2.0f, e.ReservesDown.Delta, 3);  // under 1 month costs a notch
            Assert.StartsWith("Drops to BBB if DSCR falls 0.40", e.DownText);
        }

        [Fact]
        public void Explainer_UnleveredDeficitCity_NeedsASurplus()
        {
            var m = CreditModel.FromAnnual(600000f, 700000f, 0f, 150000f, PPY); // 2.6 months
            var e = RatingExplainer.Explain(m, false);
            Assert.True(e.Unlevered);
            Assert.True(e.SurplusUp.Possible);
            Assert.False(e.DscrUp.Possible);
            Assert.Contains("operating surplus", e.UpText);
        }

        [Fact]
        public void Explainer_WhenNoSingleFactorIsEnough_ShowsTheCombinedMove()
        {
            var e = RatingExplainer.Explain(Levered(0.5f, 0.5f, 8f), false);
            Assert.Equal(CreditRating.B, e.Rating);
            Assert.True(e.UpNeedsBoth);
            Assert.Equal("+0.40 DSCR and -10.0 pts debt burden together to reach BB.", e.UpText);
        }

        [Fact]
        public void Explainer_ArrearsMeanD_AndSayHowToLeave()
        {
            var e = RatingExplainer.Explain(Levered(2f, 0.1f, 3f), true);
            Assert.Equal(CreditRating.D, e.Rating);
            Assert.Equal("Clear every arrear to leave D.", e.UpText);
        }

        // Acceptance: every rating shows the distance to the next notch up and
        // down, and each reported gap, applied on its own, really moves the rating.
        [Fact]
        public void Explainer_EveryRatingShowsDistanceUpAndDown_AndGapsAreReal()
        {
            float[] dscrs = { 0.5f, 0.95f, 1.1f, 1.3f, 1.6f, 2.1f, 2.7f };
            float[] burdens = { 0.05f, 0.10f, 0.15f, 0.20f, 0.30f, 0.38f, 0.5f };
            float[] reserves = { 0.5f, 3f, 8f };
            int checkedCases = 0;
            foreach (float d in dscrs)
            foreach (float b in burdens)
            foreach (float r in reserves)
            {
                var m = Levered(d, b, r);
                var e = RatingExplainer.Explain(m, false);
                if (e.Rating == CreditRating.D) continue;
                checkedCases++;

                if (e.Rating != CreditRating.AAA)
                {
                    Assert.True(e.DscrUp.Possible || e.BurdenUp.Possible || e.ReservesUp.Possible,
                        string.Format("no way up from {0} at dscr={1} burden={2} reserves={3}", e.Rating, d, b, r));
                    if (e.UpNeedsBoth)
                    {
                        var p = m; p.DSCR = e.DscrUp.Target; p.DebtBurden = e.BurdenUp.Target;
                        Assert.True(RatingEngine.EvaluateRating(p, false) < e.Rating);
                    }
                    else
                    {
                        if (e.DscrUp.Possible) { var p = m; p.DSCR = e.DscrUp.Target; Assert.True(RatingEngine.EvaluateRating(p, false) < e.Rating); }
                        if (e.BurdenUp.Possible) { var p = m; p.DebtBurden = e.BurdenUp.Target; Assert.True(RatingEngine.EvaluateRating(p, false) < e.Rating); }
                    }
                    if (e.ReservesUp.Possible) { var p = m; p.MonthsOfReserves = e.ReservesUp.Target; Assert.True(RatingEngine.EvaluateRating(p, false) < e.Rating); }
                }

                Assert.True(e.DscrDown.Possible || e.BurdenDown.Possible || e.ReservesDown.Possible,
                    string.Format("no margin shown for {0} at dscr={1} burden={2} reserves={3}", e.Rating, d, b, r));
                if (e.DscrDown.Possible) { var p = m; p.DSCR = e.DscrDown.Target - 0.001f; Assert.True(RatingEngine.EvaluateRating(p, false) > e.Rating); }
                if (e.BurdenDown.Possible) { var p = m; p.DebtBurden = e.BurdenDown.Target + 0.001f; Assert.True(RatingEngine.EvaluateRating(p, false) > e.Rating); }
                if (e.ReservesDown.Possible) { var p = m; p.MonthsOfReserves = e.ReservesDown.Target - 0.02f; Assert.True(RatingEngine.EvaluateRating(p, false) > e.Rating); }
            }
            Assert.True(checkedCases > 100);
        }

        // ---- WO-36: issuance ticket ----

        private static IssuanceInputs Deal(float offered, float fair, float demand)
        {
            var x = new IssuanceInputs();
            x.Face = 100000f;
            x.Periods = 60;
            x.OfferedYield = offered;
            x.FairYield = fair;
            x.DemandScore = demand;
            x.RemainingCapacity = 1000000f;
            x.CashBalance = 200000f;
            x.Metrics = CreditModel.FromAnnual(1200000f, 1000000f, 0f, 200000f, PPY);
            x.PeriodsPerYear = PPY;
            return x;
        }

        [Fact]
        public void Preview_200bpTight_ShowsLowCover_AndDoesNotFill()
        {
            var r = IssuancePreview.Run(Deal(0.03f, 0.05f, 0.8f));
            Assert.True(r.Cover < 0.05f);
            Assert.False(r.Fills);
            Assert.Equal(0f, r.Proceeds);
        }

        [Fact]
        public void Preview_WellPricedDeal_ShowsProceedsCouponAndRatingAfter()
        {
            var r = IssuancePreview.Run(Deal(0.055f, 0.05f, 0.8f));
            Assert.True(r.Fills);
            Assert.Equal(1f, r.FilledFraction);
            Assert.Equal(750f, r.Fee, 1);                 // 75bp of 100,000
            Assert.Equal(99250f, r.Proceeds, 1);
            Assert.Equal(5500f, r.AnnualCoupon, 1);
            Assert.Equal(5500f, r.After.AnnualDebtService, 1);
            Assert.Equal(r.After.AnnualNOI / 5500f, r.After.DSCR, 2); // now levered
            Assert.True(r.After.DebtBurden > 0f);
            Assert.Equal(RatingEngine.EvaluateRating(r.After, false), r.RatingAfter);
        }

        [Fact]
        public void Preview_OverCapacity_DoesNotFill()
        {
            var x = Deal(0.06f, 0.05f, 0.9f);
            x.RemainingCapacity = 50000f;
            var r = IssuancePreview.Run(x);
            Assert.True(r.OverCapacity);
            Assert.False(r.Fills);
        }

        [Fact]
        public void SpreadForFullCover_GivesCoverOfOne()
        {
            float required = 0.02f, adj = 0.001f, fair = 0.045f, demand = 0.6f;
            float spread = AuctionPricing.SpreadForFullCover(required, adj, fair, demand);
            float offered = AuctionPricing.OfferedYield(required, adj, spread);
            Assert.Equal(1f, PrimaryAuction.EstimateCover(offered, fair, demand), 3);
        }

        // ---- WO-37: maturity ladder ----

        private static BondView Issued(float principal, float coupon, int remaining)
        {
            return BondView.From(new Bond("IB1", "Note", principal, coupon, remaining), 0f);
        }

        [Fact]
        public void Ladder_CouponsThenPrincipalAtMaturity()
        {
            var ladder = MaturityLadder.Build(new[] { Issued(120000f, 0.06f, 3) }, 1000000f, 0f, 6, 2, PPY);
            Assert.Equal(600f, ladder[0].Coupon, 2);
            Assert.Equal(0f, ladder[0].Principal);
            Assert.Equal(120000f, ladder[2].Principal);
            Assert.Equal(0f, ladder[3].Due);
            Assert.False(ladder[2].Shortfall);
        }

        [Fact]
        public void Ladder_ShortfallTurnsAmberWithinTheWarningLead()
        {
            // 1,000 of cash, no operating flow: the month-3 principal cannot be paid.
            var ladder = MaturityLadder.Build(new[] { Issued(50000f, 0.06f, 3) }, 1000f, 0f, 6, 2, PPY);
            Assert.Equal(3, MaturityLadder.FirstShortfall(ladder));
            Assert.False(ladder[2].Imminent); // three months out: flagged, not yet amber

            var nearer = MaturityLadder.Build(new[] { Issued(50000f, 0.06f, 2) }, 1000f, 0f, 6, 2, PPY);
            Assert.True(nearer[1].Shortfall);
            Assert.True(nearer[1].Imminent);  // two months out: amber
        }

        // ---- WO-38: alerts ----

        [Fact]
        public void Alerts_OnePerMonth_HighestPriorityWins()
        {
            var p = new AlertPolicy();
            p.Raise(AlertKind.BigRateMove, "rates");
            p.Raise(AlertKind.Downgrade, "down");
            Alert a = p.EndPeriod(7);
            Assert.Equal(AlertKind.Downgrade, a.Kind);
            Assert.Equal(7, a.Period);
            Assert.Null(p.EndPeriod(8)); // nothing carried over
        }

        [Fact]
        public void Alerts_PersistentCondition_FiresOnceUntilItClears()
        {
            var p = new AlertPolicy();
            p.Condition(AlertKind.ShortfallAhead, true, "short");
            Assert.Equal(AlertKind.ShortfallAhead, p.EndPeriod(1).Kind);
            p.Condition(AlertKind.ShortfallAhead, true, "short");
            Assert.Null(p.EndPeriod(2));
            p.Condition(AlertKind.ShortfallAhead, false, null);
            p.Condition(AlertKind.ShortfallAhead, true, "short again");
            Assert.Equal("short again", p.EndPeriod(4).Text);
        }

        [Fact]
        public void Alerts_SuppressedCondition_IsOfferedAgainNextMonth()
        {
            var p = new AlertPolicy();
            p.Raise(AlertKind.CouponShortfall, "missed");
            p.Condition(AlertKind.BigRateMove, true, "rates");
            Assert.Equal(AlertKind.CouponShortfall, p.EndPeriod(1).Kind);
            p.Condition(AlertKind.BigRateMove, true, "rates");
            Assert.Equal(AlertKind.BigRateMove, p.EndPeriod(2).Kind);
            p.Condition(AlertKind.BigRateMove, true, "rates");
            Assert.Null(p.EndPeriod(3));
        }

        [Fact]
        public void Alerts_SequenceIncreases()
        {
            var p = new AlertPolicy();
            p.Raise(AlertKind.FailedAuction, "a");
            int s1 = p.EndPeriod(1).Sequence;
            p.Raise(AlertKind.FailedAuction, "b");
            Assert.Equal(s1 + 1, p.EndPeriod(2).Sequence);
        }
    }
}

#endif
