using Xunit;

namespace MyFirstMod.Tests
{
    // Audit Schema v5 spec, section 9 (the DebtBook-level items). These run with
    // no game DLLs and are where the P0-2/P0-3/P0-4 exploits are actually proven
    // fixed, plus the lifecycle and invariants.
    public class DebtBookTests
    {
        private const int PPY = 12;
        private const float Spread = 0.03f;   // arrears penalty above coupon
        private const int Grace = 3;
        private const int Lockout = 12;

        private static Bond NewIssued(string id, float face, float coupon, int periods)
        {
            var b = new Bond(id, "Issue", face, coupon, periods);
            b.PlacedFraction = 0f;       // freshly issued: nothing placed
            b.OutstandingPrincipal = 0f; // and nothing owed yet
            return b;
        }

        // I7 + P0-3 regression: repaying principal must NOT move PlacedFraction, so
        // retired principal can never reappear as unsold inventory.
        [Fact]
        public void RepayingPrincipal_DoesNotChangePlacement_NorReviveInventory()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);

            float proceeds = book.PlacePrimary(50000f); // place half
            Assert.InRange(proceeds, 49999f, 50001f);
            Assert.InRange(b.PlacedFraction, 0.49f, 0.51f);
            Assert.InRange(b.OutstandingPrincipal, 49999f, 50001f);
            Assert.InRange(b.UnplacedNotional, 49999f, 50001f);

            // Partial paydown of 25k (less than the 50k owed -> partial, not retire).
            float spent = book.RepayByBudget(1, 25000f, Lockout, out int retired, out bool partial);

            Assert.InRange(spent, 24999f, 25001f);
            Assert.True(partial);
            Assert.Equal(0, retired);
            Assert.InRange(b.PlacedFraction, 0.49f, 0.51f);      // I7: unchanged
            Assert.InRange(b.OutstandingPrincipal, 24999f, 25001f);
            Assert.InRange(b.UnplacedNotional, 49999f, 50001f);  // still 50%, not 75%
            Assert.True(book.ValidateInvariants());
        }

        // P0-4 structural: the book exposes no way to reduce debt without payment.
        // Servicing against an empty treasury never lowers what is owed.
        [Fact]
        public void ServicingWithNoCash_NeverReducesDebt()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);
            float owedBefore = b.OutstandingPrincipal + b.Arrears;

            float paid = book.ServicePeriod(1, 0f, Grace, Spread, Lockout, PPY,
                out int missed, out int newDefaults);

            Assert.Equal(0f, paid);
            Assert.InRange(b.OutstandingPrincipal, 99999f, 100001f); // principal intact
            Assert.True(b.OutstandingPrincipal + b.Arrears >= owedBefore); // debt only grew
        }

        // P0-2: a missed payment keeps the bond and raises arrears.
        [Fact]
        public void MissedPayment_KeepsBond_AndRaisesArrears()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);

            float paid = book.ServicePeriod(1, 0f, Grace, Spread, Lockout, PPY,
                out int missed, out int newDefaults);

            Assert.Equal(0f, paid);
            Assert.Equal(1, missed);
            Assert.Equal(1, book.Count); // kept, not deleted
            float expectedCoupon = 100000f * 0.05f / PPY;
            Assert.InRange(b.Arrears, expectedCoupon - 1f, expectedCoupon + 1f);
            Assert.Equal(BondState.Delinquent, b.State);
        }

        // Arrears accrue a penalty above the coupon rate (default costs more than paying).
        [Fact]
        public void Arrears_AccrueAboveCouponRate()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 0); // matured shell
            b.PlacedFraction = 1f;
            b.OutstandingPrincipal = 0f;
            b.Arrears = 1000f;
            b.State = BondState.Delinquent;
            book.Add(b);

            book.ServicePeriod(5, 0f, Grace, Spread, Lockout, PPY,
                out int missed, out int newDefaults);

            // arrears *= 1 + (coupon + spread)/ppy = 1 + 0.08/12
            float expected = 1000f * (1f + (0.05f + Spread) / PPY);
            Assert.InRange(b.Arrears, expected - 0.1f, expected + 0.1f);
            Assert.True(b.Arrears > 1000f);
        }

        // Grace -> Default at exactly GracePeriods consecutive periods in arrears.
        [Fact]
        public void Delinquent_DefaultsAtExactlyGracePeriods()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);

            for (int p = 1; p < Grace; p++)
            {
                book.ServicePeriod(p, 0f, Grace, Spread, Lockout, PPY, out _, out _);
                Assert.Equal(BondState.Delinquent, b.State);
            }

            book.ServicePeriod(Grace, 0f, Grace, Spread, Lockout, PPY, out _, out int newDefaults);
            Assert.Equal(BondState.Defaulted, b.State);
            Assert.Equal(1, newDefaults);
            Assert.True(book.AnyDefaulted);
            Assert.True(book.IssuanceSuspended);
        }

        // Rating recovery stays blocked until arrears clear AND the lock-out elapses.
        [Fact]
        public void RatingRecovery_BlockedUntilArrearsClearedAndLockoutElapsed()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);

            // Drive to default.
            for (int p = 1; p <= Grace; p++)
                book.ServicePeriod(p, 0f, Grace, Spread, Lockout, PPY, out _, out _);
            Assert.True(book.AnyDefaulted);
            int defaultPeriod = book.LastDefaultPeriod;

            // Still in arrears -> blocked regardless of time.
            Assert.False(book.RatingRecoveryAllowed(defaultPeriod + Lockout + 5, Lockout));

            // Clear the whole obligation.
            float owed = book.AmountOwed("IB1");
            book.RepayByBudget(defaultPeriod + 1, owed + 1f, Lockout, out int retired, out _);
            Assert.Equal(1, retired);
            Assert.Equal(0, book.Count);
            Assert.False(book.AnyDefaulted);
            Assert.True(book.TotalArrears <= DebtBook.EPS);

            // Arrears cleared but lock-out not elapsed -> still blocked.
            Assert.False(book.RatingRecoveryAllowed(defaultPeriod + 1, Lockout));
            // Lock-out elapsed -> allowed.
            Assert.True(book.RatingRecoveryAllowed(defaultPeriod + Lockout, Lockout));
        }

        // TC-02 (plan section 5): a zero-cash MATURITY is a hard default - rating
        // to D immediately, no grace window (grace applies only to missed coupons).
        [Fact]
        public void MaturityWithNoCash_DefaultsImmediately()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 1); // matures next period
            book.Add(b);
            book.PlacePrimary(100000f);

            book.ServicePeriod(1, 0f, Grace, Spread, Lockout, PPY, out _, out int newDefaults);

            Assert.Equal(BondState.Defaulted, b.State); // immediate, despite Grace > 1
            Assert.Equal(1, newDefaults);
            Assert.True(book.AnyDefaulted);
            Assert.True(b.Arrears > 0f);          // principal rolled into arrears
            Assert.True(book.Count == 1);         // liability retained, not erased
        }

        // The O(1) Id index stays consistent through adds, removes and repayment.
        [Fact]
        public void IdIndex_StaysConsistentThroughChurn()
        {
            var book = new DebtBook();
            book.Add(NewIssued("IB1", 10000f, 0.05f, 12));
            book.Add(NewIssued("IB2", 20000f, 0.05f, 12));
            book.Add(NewIssued("IB3", 30000f, 0.05f, 12));
            book.PlacePrimary(60000f);

            Assert.NotNull(book.FindActive("IB2"));
            Assert.True(book.RemoveBond("IB2"));
            Assert.Null(book.FindActive("IB2"));
            Assert.NotNull(book.FindActive("IB1"));
            Assert.NotNull(book.FindActive("IB3"));
            Assert.Equal(2, book.Count);

            // Retire everything via a large budget.
            book.RepayByBudget(1, 1000000f, Lockout, out int retired, out _);
            Assert.Equal(2, retired);
            Assert.Null(book.FindActive("IB1"));
            Assert.Null(book.FindActive("IB3"));
            Assert.Equal(0, book.Count);
        }

        // I3/I4 invariant violation is detected.
        [Fact]
        public void ValidateInvariants_RejectsOverPrincipal()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            b.PlacedFraction = 1f;
            b.OutstandingPrincipal = 200000f; // > face
            book.Add(b);

            Assert.False(book.ValidateInvariants());
        }

        // Full placement then full repay retires the bond cleanly and leaves the
        // book empty and valid.
        [Fact]
        public void FullPlacementThenFullRepay_RetiresBond()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);

            float owed = book.AmountOwed("IB1");
            float spent = book.RepayByBudget(1, owed + 1f, Lockout, out int retired, out bool partial);

            Assert.InRange(spent, owed - 1f, owed + 1f);
            Assert.Equal(1, retired);
            Assert.False(partial);
            Assert.Equal(0, book.Count);
            Assert.True(book.ValidateInvariants());
        }

        [Fact]
        public void WO19_PushbackShortfall_DistributesProportionally()
        {
            var book = new DebtBook();
            var b1 = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b1);
            book.PlacePrimary(100000f);
            var b2 = NewIssued("IB2", 200000f, 0.05f, 60);
            book.Add(b2);
            book.PlacePrimary(200000f);

            float shortfall = 3000f;
            book.PushbackShortfall(shortfall);

            Assert.InRange(b1.Arrears, 999f, 1001f);
            Assert.InRange(b2.Arrears, 1999f, 2001f);
        }

        [Fact]
        public void WO19_PushbackShortfall_ZeroOrNegative_IsNoOp()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);

            book.PushbackShortfall(0f);
            Assert.Equal(0f, b.Arrears);
            book.PushbackShortfall(-500f);
            Assert.Equal(0f, b.Arrears);
        }

        [Fact]
        public void WO19_ServiceThenShortfall_DebtNeverShrinks()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);

            float totalBefore = b.OutstandingPrincipal + b.Arrears;
            float cashPaid = book.ServicePeriod(1, 50000f, Grace, Spread, Lockout, PPY,
                out _, out _);

            float shortfall = cashPaid * 0.5f;
            book.PushbackShortfall(shortfall);

            float totalAfter = b.OutstandingPrincipal + b.Arrears;
            Assert.True(totalAfter >= totalBefore - cashPaid + shortfall - 1f);
        }

        // WO-24 mandatory: service-order independence. Under the old sequential
        // servicing, bonds inserted first consumed the budget before later bonds,
        // so the set of defaults depended on array insertion order. Pro-rata
        // allocation must produce identical total spending regardless of order.
        [Fact]
        public void WO24_ServiceOrderIndependence_SameSpendRegardlessOfInsertionOrder()
        {
            float budget = 500f;

            var bookAB = new DebtBook();
            var a1 = NewIssued("IB1", 100000f, 0.06f, 24);
            var b1 = NewIssued("IB2", 200000f, 0.04f, 48);
            bookAB.Add(a1); bookAB.Add(b1);
            bookAB.PlacePrimary(300000f);

            float spentAB = bookAB.ServicePeriod(1, budget, Grace, Spread, Lockout, PPY,
                out int missedAB, out int defaultsAB);

            var bookBA = new DebtBook();
            var a2 = NewIssued("IB1", 100000f, 0.06f, 24);
            var b2 = NewIssued("IB2", 200000f, 0.04f, 48);
            bookBA.Add(b2); bookBA.Add(a2);
            bookBA.PlacePrimary(300000f);

            float spentBA = bookBA.ServicePeriod(1, budget, Grace, Spread, Lockout, PPY,
                out int missedBA, out int defaultsBA);

            Assert.InRange(spentAB, spentBA - 1f, spentBA + 1f);
            Assert.Equal(missedAB, missedBA);
            Assert.Equal(defaultsAB, defaultsBA);

            Assert.InRange(a1.OutstandingPrincipal, a2.OutstandingPrincipal - 1f, a2.OutstandingPrincipal + 1f);
            Assert.InRange(b1.OutstandingPrincipal, b2.OutstandingPrincipal - 1f, b2.OutstandingPrincipal + 1f);
            Assert.InRange(a1.Arrears, a2.Arrears - 1f, a2.Arrears + 1f);
            Assert.InRange(b1.Arrears, b2.Arrears - 1f, b2.Arrears + 1f);
        }

        // WO-24: under constrained budget, pro-rata allocates proportionally to dues.
        [Fact]
        public void WO24_ProRata_AllocatesProportionallyUnderConstraint()
        {
            var book = new DebtBook();
            var small = NewIssued("IB1", 50000f, 0.06f, 24);
            var large = NewIssued("IB2", 150000f, 0.06f, 24);
            book.Add(small); book.Add(large);
            book.PlacePrimary(200000f);

            float couponSmall = 50000f * 0.06f / PPY;
            float couponLarge = 150000f * 0.06f / PPY;
            float totalDue = couponSmall + couponLarge;
            float budget = totalDue * 0.5f;

            book.ServicePeriod(1, budget, Grace, Spread, Lockout, PPY, out _, out _);

            float smallPaid = small.InterestPaid;
            float largePaid = large.InterestPaid;
            if (smallPaid + largePaid > 0.01f)
            {
                float ratio = largePaid / smallPaid;
                Assert.InRange(ratio, 2.5f, 3.5f);
            }
        }

        // WO-25: InterestPaid and PrincipalRepaid track correctly.
        [Fact]
        public void WO25_InterestAndPrincipal_TrackedSeparately()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.06f, 1);
            book.Add(b);
            book.PlacePrimary(100000f);

            float cashPaid = book.ServicePeriod(1, 200000f, Grace, Spread, Lockout, PPY,
                out _, out _);

            Assert.True(b.InterestPaid > 0f, "Interest should be tracked");
            Assert.True(b.PrincipalRepaid > 0f, "Principal repayment should be tracked");
            Assert.InRange(b.InterestPaid + b.PrincipalRepaid, cashPaid - 1f, cashPaid + 1f);
        }

        // WO-30: early-retired bonds enter redemption history via RemoveBond.
        [Fact]
        public void WO30_RemoveBond_EntersRedeemedHistory()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 100000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(100000f);

            Assert.True(book.RemoveBond("IB1"));
            Assert.Equal(0, book.Count);
            Assert.Equal(1, book.Redeemed.Count);
            Assert.Equal("IB1", book.Redeemed[0].Id);
            Assert.Equal(BondState.Redeemed, book.Redeemed[0].State);
            Assert.Equal(0f, book.Redeemed[0].OutstandingPrincipal);
            Assert.True(book.ValidateInvariants());
        }

        // WO-30: RepayByBudget full retirements enter redeemed history.
        [Fact]
        public void WO30_RepayByBudget_FullRetirement_EntersRedeemedHistory()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 50000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(50000f);

            float owed = book.AmountOwed("IB1");
            book.RepayByBudget(1, owed + 1f, Lockout, out int retired, out bool partial);

            Assert.Equal(1, retired);
            Assert.Equal(0, book.Count);
            Assert.Equal(1, book.Redeemed.Count);
            Assert.Equal(BondState.Redeemed, book.Redeemed[0].State);
            Assert.True(book.ValidateInvariants());
        }

        // WO-30: ValidateInvariants checks redeemed bonds too.
        [Fact]
        public void WO30_ValidateInvariants_RejectsCorruptRedeemedBond()
        {
            var book = new DebtBook();
            var b = NewIssued("IB1", 50000f, 0.05f, 60);
            book.Add(b);
            book.PlacePrimary(50000f);
            book.RemoveBond("IB1");

            Assert.Equal(1, book.Redeemed.Count);
            book.Redeemed[0].OutstandingPrincipal = -500f;
            Assert.False(book.ValidateInvariants());
        }
    }
}
