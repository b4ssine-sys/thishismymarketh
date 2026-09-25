// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    // The words and the one recommendation the new screens show.
    public class PresentationTests
    {
        private static EngineSnapshot Healthy()
        {
            var s = new EngineSnapshot();
            s.Metrics = CreditModel.FromAnnual(1200000f, 1000000f, 0f, 300000f, 12);
            s.Rating = RatingEngine.EvaluateRating(s.Metrics, false);
            s.Explanation = RatingExplainer.Explain(s.Metrics, false);
            s.MonthsOfReserves = s.Metrics.MonthsOfReserves;
            s.CanIssueBonds = true;
            s.RecommendedHedge = "No debt to hedge";
            return s;
        }

        [Fact]
        public void Advisor_DebtFreeCity_SuggestsAnEmergencyNote()
        {
            Advice a = Advisor.Recommend(Healthy());
            Assert.Equal(AdviceAction.OpenBorrow, a.Action);
            Assert.Equal(IssueTemplates.EmergencyNote, a.TemplateIndex);
        }

        [Fact]
        public void Advisor_ArrearsComeFirst()
        {
            var s = Healthy();
            s.HasArrears = true;
            Assert.Equal(AdviceAction.PayDown, Advisor.Recommend(s).Action);
        }

        [Fact]
        public void Advisor_ProjectedShortfall_BeforeAnythingElse()
        {
            var s = Healthy();
            s.Ladder = new[] { new LadderMonth { Offset = 1 }, new LadderMonth { Offset = 2, Due = 5000f, Shortfall = true } };
            Advice a = Advisor.Recommend(s);
            Assert.Equal(AdviceAction.PayDown, a.Action);
            Assert.Contains("2 month(s)", a.Text);
        }

        [Fact]
        public void Advisor_CannotIssue_DoesNotSuggestIssuing()
        {
            var s = Healthy();
            s.CanIssueBonds = false;
            Assert.NotEqual(AdviceAction.OpenBorrow, Advisor.Recommend(s).Action);
        }

        [Fact]
        public void Wording_Runway()
        {
            Assert.Equal("Growing about 2,000 a month", Wording.Runway(10000f, 2000f));
            Assert.Equal("About 5 month(s) at -2,000 a month", Wording.Runway(10000f, -2000f));
        }

        [Theory]
        [InlineData(0.0f, "Rates trending up")]
        [InlineData(1.5708f, "Rates near a peak")]
        [InlineData(3.1416f, "Rates trending down")]
        [InlineData(4.7124f, "Rates near a trough")]
        public void Wording_RateCycle(float phase, string expected)
        {
            Assert.Equal(expected, Wording.RateCycle(phase));
        }

        [Theory]
        [InlineData(0.03f, 0.045f, "Steep curve")]
        [InlineData(0.04f, 0.045f, "Normal curve")]
        [InlineData(0.04f, 0.041f, "Flat curve")]
        [InlineData(0.05f, 0.04f, "Inverted curve")]
        public void Wording_CurveShape(float shortSpot, float longSpot, string expected)
        {
            Assert.Equal(expected, Wording.CurveShape(shortSpot, longSpot));
        }

        [Fact]
        public void Wording_HedgeStatus()
        {
            Assert.Equal("No debt to hedge", Wording.HedgeStatus(0f, 0f));
            Assert.Equal("50% of 200,000 debt hedged", Wording.HedgeStatus(200000f, 100000f));
            Assert.Equal("Over-hedged: 150% of debt", Wording.HedgeStatus(200000f, 300000f));
        }

        [Fact]
        public void Feed_ShowsAlertOrderAndMarket_InThatOrder()
        {
            var s = new EngineSnapshot();
            s.Alerts = new[] { new Alert { Period = 7, Text = "Rates rose" } };
            s.RecentResults = new[] { new CommandResult { Success = false, Message = "auction failed" } };
            s.Transactions = new[] { new CimTransaction { BuyVolume = 10f, SellVolume = 4f, Detail = "Fully subscribed" } };
            string[] lines = FeedModel.Lines(s);
            Assert.Equal(3, lines.Length);
            Assert.Equal("Month 7: Rates rose", lines[0]);
            Assert.Equal("Not done: auction failed", lines[1]);
            Assert.StartsWith("Citizens bought 10", lines[2]);
        }

        [Fact]
        public void Feed_EmptySnapshot_HasNoLines()
        {
            Assert.Empty(FeedModel.Lines(new EngineSnapshot()));
        }
    }
}

#endif
