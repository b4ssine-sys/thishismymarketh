using Xunit;

namespace MyFirstMod.Tests
{
    public class CreditModelTests
    {
        private const int PPY = 12; // BondPricing.PeriodsPerYear

        private static Bond PlacedBond(string id, float outstanding, float coupon)
        {
            var b = new Bond(id, "IB", outstanding, coupon, 60);
            b.PlacedFraction = 1f;
            b.OutstandingPrincipal = outstanding;
            b.State = BondState.Active;
            return b;
        }

        // UT-P2-01: unlevered city, strong surplus -> DSCR 20, burden 0, AAA.
        [Fact]
        public void UT_P2_01_UnleveredCity_IsAAA()
        {
            var book = new DebtBook();
            var m = CreditModel.CalculateMetrics(
                avgIncomePerPeriod: 50000f, avgExpensePerPeriod: 30000f,
                debtBook: book, currentCashReserves: 10_000_000f,
                periodsPerYear: PPY);

            Assert.Equal(0f, m.AnnualDebtService, 2);
            Assert.Equal(20f, m.DSCR, 3);
            Assert.Equal(0f, m.DebtBurden, 4);
            Assert.Equal(CreditRating.AAA, RatingEngine.EvaluateRating(m, false));
        }

        // WO-18: unlevered city running a deficit is never D.
        [Fact]
        public void WO18_UnleveredDeficit_NotD()
        {
            var book = new DebtBook();
            var m = CreditModel.CalculateMetrics(
                avgIncomePerPeriod: 1000f, avgExpensePerPeriod: 5000f,
                debtBook: book, currentCashReserves: 100_000f,
                periodsPerYear: PPY);

            Assert.Equal(0f, m.AnnualDebtService, 2);
            Assert.Equal(1f, m.DSCR, 3);
            Assert.Equal(0f, m.DebtBurden, 4);
            CreditRating r = RatingEngine.EvaluateRating(m, false);
            Assert.NotEqual(CreditRating.D, r);
        }

        // WO-18: cold-start city (zero income, zero expense) is never D.
        [Fact]
        public void WO18_ColdStart_NotD()
        {
            var book = new DebtBook();
            var m = CreditModel.CalculateMetrics(
                avgIncomePerPeriod: 0f, avgExpensePerPeriod: 0f,
                debtBook: book, currentCashReserves: 0f,
                periodsPerYear: PPY);

            Assert.Equal(0f, m.AnnualDebtService, 2);
            Assert.Equal(1f, m.DSCR, 3);
            CreditRating r = RatingEngine.EvaluateRating(m, false);
            Assert.NotEqual(CreditRating.D, r);
        }

        // UT-P2-02: DSCR 2.5, moderate burden, 6 months reserves -> base AA, +1 -> AAA.
        [Fact]
        public void UT_P2_02_StandardCoverage_WithLiquidity_UpgradesToAAA()
        {
            var m = new CreditMetrics
            {
                DSCR = 2.50f,
                DebtBurden = 0.10f,
                MonthsOfReserves = 6.0f,
                AnnualDebtService = 100000f,
                AnnualNOI = 250000f
            };
            Assert.Equal(CreditRating.AAA, RatingEngine.EvaluateRating(m, false));
        }

        // UT-P2-03: DSCR 1.5, thin liquidity (0.5 mo) -> base A, -1 -> BBB.
        [Fact]
        public void UT_P2_03_DistressedLiquidity_DowngradesToBBB()
        {
            var m = new CreditMetrics
            {
                DSCR = 1.50f,
                DebtBurden = 0.15f,
                MonthsOfReserves = 0.5f
            };
            Assert.Equal(CreditRating.BBB, RatingEngine.EvaluateRating(m, false));
        }

        // WO-17 cadence contract: annualization factor is periodsPerYear, not
        // ticksPerPeriod * periodsPerYear. This is the test whose absence let
        // P0-1 through.
        [Fact]
        public void WO17_CadenceContract_AnnualizationIsPeriodsPerYear()
        {
            var book = new DebtBook();
            book.Add(PlacedBond("IB1", 100000f, 0.06f)); // annual DS = 6,000

            var m = CreditModel.CalculateMetrics(
                avgIncomePerPeriod: 1000f, avgExpensePerPeriod: 0f,
                debtBook: book, currentCashReserves: 500000f,
                periodsPerYear: PPY);

            Assert.Equal(1000f * PPY, m.AnnualOperatingRevenue, 1); // 12,000
            Assert.Equal(6000f, m.AnnualDebtService, 2);
            Assert.Equal(m.AnnualNOI / m.AnnualDebtService, m.DSCR, 4);
            Assert.Equal(m.AnnualDebtService / m.AnnualOperatingRevenue, m.DebtBurden, 4);
            Assert.Equal(2f, m.DSCR, 3); // 12,000 / 6,000
        }

        // WO-17 cadence invariance: different periodsPerYear values produce
        // proportional annualized figures, confirming no hidden tick factor.
        [Fact]
        public void WO17_CadenceInvariance_NoHiddenTickFactor()
        {
            var book = new DebtBook();
            book.Add(PlacedBond("IB1", 100000f, 0.06f));

            // Monthly periods (12/yr): 1000/period -> 12,000/yr revenue
            var a = CreditModel.CalculateMetrics(1000f, 0f, book, 0f, 12);
            // Quarterly periods (4/yr): 3000/period -> 12,000/yr revenue
            var b = CreditModel.CalculateMetrics(3000f, 0f, book, 0f, 4);

            Assert.Equal(a.AnnualOperatingRevenue, b.AnnualOperatingRevenue, 1);
            Assert.Equal(a.DSCR, b.DSCR, 4);
        }

        // UT-P2-05: expenses exceed revenue -> non-positive coverage -> D.
        [Fact]
        public void UT_P2_05_NegativeCashFlow_IsDefaultGrade()
        {
            var book = new DebtBook();
            book.Add(PlacedBond("IB1", 100000f, 0.06f));

            var m = CreditModel.CalculateMetrics(
                avgIncomePerPeriod: 100f, avgExpensePerPeriod: 5000f,
                debtBook: book, currentCashReserves: 0f,
                periodsPerYear: PPY);

            Assert.True(m.DSCR <= 0f);
            CreditRating r = RatingEngine.EvaluateRating(m, false);
            Assert.True(r == CreditRating.D || r == CreditRating.CCC);
        }

        // Hard floor: any active arrears forces D regardless of ratios.
        [Fact]
        public void ActiveArrears_ForceD()
        {
            var m = new CreditMetrics { DSCR = 5f, DebtBurden = 0.01f, MonthsOfReserves = 12f,
                AnnualDebtService = 1000f };
            Assert.Equal(CreditRating.D, RatingEngine.EvaluateRating(m, true));
        }

        // Liquidity notch never pushes below CCC via the base path (D is floor-only).
        [Fact]
        public void WeakButServiced_StaysCCC_NotD()
        {
            var m = new CreditMetrics { DSCR = 0.5f, DebtBurden = 0.9f, MonthsOfReserves = 0.1f,
                AnnualDebtService = 1000f };
            Assert.Equal(CreditRating.CCC, RatingEngine.EvaluateRating(m, false));
        }

        // WO-18: coverage collapse only triggers D when there is debt to cover.
        [Fact]
        public void WO18_CoverageCollapseRequiresDebt()
        {
            // With debt service: DSCR < 0.2 -> D
            var withDebt = new CreditMetrics { DSCR = 0.1f, DebtBurden = 0.9f,
                MonthsOfReserves = 0.5f, AnnualDebtService = 50000f };
            Assert.Equal(CreditRating.D, RatingEngine.EvaluateRating(withDebt, false));

            // Without debt service: DSCR < 0.2 should NOT trigger D
            var noDebt = new CreditMetrics { DSCR = 0.1f, DebtBurden = 0f,
                MonthsOfReserves = 0.5f, AnnualDebtService = 0f };
            Assert.NotEqual(CreditRating.D, RatingEngine.EvaluateRating(noDebt, false));
        }
    }
}
