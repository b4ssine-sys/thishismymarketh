// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    // Phase 2 unit test matrix (memo section 4). Proves the annualized credit
    // model (P1-1) and the calibrated rating grid + liquidity notch.
    public class CreditModelTests
    {
        private const int TicksPerPeriod = 15; // mirrors BondMarketEngine.TICKS_PER_PERIOD
        private const int PPY = 12;            // BondPricing.PeriodsPerYear

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
                avgIncomePerTick: 50000f, avgExpensePerTick: 30000f,
                debtBook: book, currentCashReserves: 10_000_000f,
                ticksPerPeriod: TicksPerPeriod, periodsPerYear: PPY);

            Assert.Equal(0f, m.AnnualDebtService, 2);
            Assert.Equal(20f, m.DSCR, 3);
            Assert.Equal(0f, m.DebtBurden, 4);
            Assert.Equal(CreditRating.AAA, RatingEngine.EvaluateRating(m, false));
        }

        // UT-P2-02: DSCR 2.5, moderate burden, 6 months reserves -> base AA, +1 -> AAA.
        [Fact]
        public void UT_P2_02_StandardCoverage_WithLiquidity_UpgradesToAAA()
        {
            var m = new CreditMetrics
            {
                DSCR = 2.50f,
                DebtBurden = 0.10f,   // > 0.08 so base is AA, not AAA
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
                DebtBurden = 0.15f,   // A band
                MonthsOfReserves = 0.5f
            };
            Assert.Equal(CreditRating.BBB, RatingEngine.EvaluateRating(m, false));
        }

        // UT-P2-04: P1-1 regression - horizons are unified. DSCR equals the
        // analytic annual ratio and annual debt service is period-independent
        // (coupon * outstanding), NOT off by the 15x tick/period mix.
        [Fact]
        public void UT_P2_04_HorizonsAreUnified()
        {
            var book = new DebtBook();
            book.Add(PlacedBond("IB1", 100000f, 0.06f)); // annual DS = 6,000

            var m = CreditModel.CalculateMetrics(
                avgIncomePerTick: 1000f, avgExpensePerTick: 0f,
                debtBook: book, currentCashReserves: 500000f,
                ticksPerPeriod: TicksPerPeriod, periodsPerYear: PPY);

            float ticksPerYear = TicksPerPeriod * PPY; // 180
            Assert.Equal(1000f * ticksPerYear, m.AnnualOperatingRevenue, 1);
            Assert.Equal(6000f, m.AnnualDebtService, 2); // independent of tick cadence
            Assert.Equal(m.AnnualNOI / m.AnnualDebtService, m.DSCR, 4);
            Assert.Equal(m.AnnualDebtService / m.AnnualOperatingRevenue, m.DebtBurden, 4);
            Assert.Equal(30f, m.DSCR, 3); // 180,000 / 6,000
        }

        // UT-P2-04b: the same annual position yields the SAME DSCR whatever the
        // internal tick cadence, as long as the annual rates match. This is the
        // property the old per-tick/per-period mix violated by 15x.
        [Fact]
        public void UT_P2_04b_DSCR_InvariantToTickCadence()
        {
            var book = new DebtBook();
            book.Add(PlacedBond("IB1", 100000f, 0.06f));

            // Cadence A: 15 ticks/period, 100/tick.
            var a = CreditModel.CalculateMetrics(100f, 0f, book, 0f, 15, PPY);
            // Cadence B: 1 tick/period, 1500/tick -> identical annual revenue.
            var b = CreditModel.CalculateMetrics(1500f, 0f, book, 0f, 1, PPY);

            Assert.Equal(a.AnnualOperatingRevenue, b.AnnualOperatingRevenue, 1);
            Assert.Equal(a.DSCR, b.DSCR, 4);
        }

        // UT-P2-05: expenses exceed revenue -> non-positive coverage -> D (hard floor).
        [Fact]
        public void UT_P2_05_NegativeCashFlow_IsDefaultGrade()
        {
            var book = new DebtBook();
            book.Add(PlacedBond("IB1", 100000f, 0.06f));

            var m = CreditModel.CalculateMetrics(
                avgIncomePerTick: 100f, avgExpensePerTick: 5000f, // deep operating deficit
                debtBook: book, currentCashReserves: 0f,
                ticksPerPeriod: TicksPerPeriod, periodsPerYear: PPY);

            Assert.True(m.DSCR <= 0f);
            CreditRating r = RatingEngine.EvaluateRating(m, false);
            Assert.True(r == CreditRating.D || r == CreditRating.CCC);
        }

        // Hard floor: any active arrears forces D regardless of ratios.
        [Fact]
        public void ActiveArrears_ForceD()
        {
            var m = new CreditMetrics { DSCR = 5f, DebtBurden = 0.01f, MonthsOfReserves = 12f };
            Assert.Equal(CreditRating.D, RatingEngine.EvaluateRating(m, true));
        }

        // Liquidity notch never pushes below CCC via the base path (D is floor-only).
        [Fact]
        public void WeakButServiced_StaysCCC_NotD()
        {
            var m = new CreditMetrics { DSCR = 0.5f, DebtBurden = 0.9f, MonthsOfReserves = 0.1f };
            // DSCR >= 0.2 so not the hard floor; base falls through to CCC, and the
            // liquidity downgrade is clamped at CCC.
            Assert.Equal(CreditRating.CCC, RatingEngine.EvaluateRating(m, false));
        }
    }
}

#endif
