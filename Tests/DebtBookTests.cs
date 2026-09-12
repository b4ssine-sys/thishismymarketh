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
    }
}
