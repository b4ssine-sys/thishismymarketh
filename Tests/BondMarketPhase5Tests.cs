using System;
using Xunit;

namespace MyFirstMod.Tests
{
    // Phase 5: PRNG determinism/persistence (G-1), issuer credit (P1-2),
    // auction (P1-7), friction (P1-8).
    public class BondMarketPhase5Tests
    {
        // ---- G-1: DeterministicRandom ----

        [Fact]
        public void Prng_SameSeed_SameSequence()
        {
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++)
                Assert.Equal(a.NextDouble(), b.NextDouble(), 10);
        }

        [Fact]
        public void Prng_StateRoundTrips()
        {
            var a = new DeterministicRandom(7);
            for (int i = 0; i < 10; i++) a.NextDouble(); // advance
            uint[] state = a.GetState();

            var b = new DeterministicRandom(999);
            b.SetState(state);

            for (int i = 0; i < 50; i++)
                Assert.Equal(a.NextDouble(), b.NextDouble(), 10); // resume identically
        }

        [Fact]
        public void Prng_OutputsAreInRange()
        {
            var r = new DeterministicRandom(123);
            for (int i = 0; i < 1000; i++)
            {
                double d = r.NextDouble();
                Assert.True(d >= 0.0 && d < 1.0);
                int n = r.Next(6);
                Assert.InRange(n, 0, 5);
            }
        }

        // ---- P1-2: IssuerModel ----

        [Fact]
        public void IssuerSpread_WidensAsCreditWorsens()
        {
            Assert.True(IssuerModel.IssuerSpread(CreditRating.AAA) < IssuerModel.IssuerSpread(CreditRating.BBB));
            Assert.True(IssuerModel.IssuerSpread(CreditRating.BBB) < IssuerModel.IssuerSpread(CreditRating.CCC));
        }

        [Fact]
        public void DefaultHazard_IncreasesWithRiskAndMultiplier()
        {
            float bbb = IssuerModel.DefaultHazard(CreditRating.BBB, IssuerModel.HAZARD_STANDARD);
            float b = IssuerModel.DefaultHazard(CreditRating.B, IssuerModel.HAZARD_STANDARD);
            Assert.True(b > bbb);
            Assert.True(IssuerModel.DefaultHazard(CreditRating.BBB, IssuerModel.HAZARD_VOLATILE)
                       > IssuerModel.DefaultHazard(CreditRating.BBB, IssuerModel.HAZARD_STANDARD));
            Assert.Equal(0f, IssuerModel.DefaultHazard(CreditRating.AAA, IssuerModel.HAZARD_STANDARD), 6);
        }

        [Fact]
        public void RecoveryRate_EssentialServicesRecoverHigher()
        {
            Assert.True(IssuerModel.RecoveryRateFor(IssuerArchetype.WaterDistrict)
                       > IssuerModel.RecoveryRateFor(IssuerArchetype.PortAuthority));
            Assert.InRange(IssuerModel.RecoveryRateFor(IssuerArchetype.PowerGrid), 0f, 1f);
        }

        [Fact]
        public void Migrate_HighHazard_EventuallyDefaults_AndDIsAbsorbing()
        {
            var rng = new DeterministicRandom(2024);
            var m = IssuerModel.MakeIssuer("Junk", IssuerArchetype.PortAuthority);
            m.Rating = CreditRating.CCC;
            bool everDefaulted = false;
            for (int yr = 0; yr < 40 && !everDefaulted; yr++)
            {
                bool d;
                m.Rating = IssuerModel.MigrateAnnual(m.Rating, m.HomeRating, IssuerModel.HAZARD_VOLATILE, rng, out d);
                if (d) everDefaulted = true;
            }
            Assert.True(everDefaulted, "a CCC issuer under volatile hazard should default within 40 years");

            // D is absorbing under migration (resolution is the caller's job).
            bool d2;
            var after = IssuerModel.MigrateAnnual(CreditRating.D, CreditRating.BBB, IssuerModel.HAZARD_STANDARD, rng, out d2);
            Assert.Equal(CreditRating.D, after);
        }

        [Fact]
        public void Migrate_StaysWithinBounds_AndDoesNotDriftToD()
        {
            var rng = new DeterministicRandom(55);
            var rating = CreditRating.A;
            for (int i = 0; i < 500; i++)
            {
                bool d;
                rating = IssuerModel.MigrateAnnual(rating, CreditRating.A, 0f, rng, out d); // zero hazard
                Assert.False(d);                     // no default with zero hazard
                Assert.NotEqual(CreditRating.D, rating); // drift never reaches D
            }
        }

        // ---- P1-7: PrimaryAuction ----

        [Fact]
        public void BidToCover_IsOneAtFairValue_WhenDemandFull()
        {
            Assert.Equal(1f, PrimaryAuction.BidToCover(0f, 1f), 3); // exp(0)*1 = 1
        }

        [Fact]
        public void BidToCover_RisesWithConcession()
        {
            float lo = PrimaryAuction.BidToCover(0f, 0.6f);
            float hi = PrimaryAuction.BidToCover(40f, 0.6f);
            Assert.True(hi > lo);
        }

        [Fact]
        public void Auction_FailsBelowMinCover_FillsWhenCovered()
        {
            // Weak demand + no concession -> undersubscribed -> fails.
            var fail = PrimaryAuction.Evaluate(0.05f, 0.05f, 0.2f);
            Assert.False(fail.Filled);
            Assert.Equal(0f, fail.FilledFraction, 4);

            // Strong demand + generous concession -> covered -> full fill at offered yield.
            var ok = PrimaryAuction.Evaluate(0.07f, 0.05f, 0.9f);
            Assert.True(ok.Filled);
            Assert.Equal(0.07f, ok.ClearingYield, 5); // uniform price (B-2)
            Assert.True(ok.FilledFraction > 0f);
        }

        [Fact]
        public void Auction_TightDeal200bp_RaisesLessThanPar()
        {
            float fairYield = 0.05f;
            float tightYield = fairYield - 0.02f; // 200bp tight
            float normalYield = fairYield + 0.005f; // 5bp concession (at-market)
            float demand = 0.8f;

            var tight = PrimaryAuction.Evaluate(tightYield, fairYield, demand);
            var normal = PrimaryAuction.Evaluate(normalYield, fairYield, demand);

            Assert.True(normal.Filled, "At-market deal should fill");
            Assert.True(normal.FilledFraction > tight.FilledFraction,
                "200bp-tight deal must raise measurably less than an at-market deal");

            if (tight.Filled)
                Assert.True(tight.FilledFraction < 0.5f,
                    "A tight deal that fills should fill well below par");
        }

        [Fact]
        public void Auction_PartialFill_BetweenMinCoverAndOne()
        {
            float fair = 0.05f;
            float demand = 0.85f;
            var result = PrimaryAuction.Evaluate(fair, fair, demand);

            Assert.True(result.Filled);
            Assert.True(result.FilledFraction >= PrimaryAuction.MinCover);
            Assert.True(result.FilledFraction < 1f);
        }

        // ---- P1-8: Friction ----

        [Fact]
        public void UnderwritingFee_Is75bpOfPar()
        {
            Assert.Equal(750f, Friction.UnderwritingFee(100000f), 1);
        }

        [Fact]
        public void HalfSpread_WidensWithRiskAndDuration()
        {
            Assert.True(Friction.HalfSpread(CreditRating.BBB, 5f) > Friction.HalfSpread(CreditRating.AAA, 5f));
            Assert.True(Friction.HalfSpread(CreditRating.A, 20f) > Friction.HalfSpread(CreditRating.A, 1f));
        }

        [Fact]
        public void Impact_IsSqrtOfOrderOverDepth()
        {
            float full = Friction.ImpactFraction(1000f, 1000f);   // ratio 1 -> k bp
            float tenth = Friction.ImpactFraction(100f, 1000f);   // ratio 0.1 -> ~k*0.316
            Assert.Equal(Friction.ImpactK / 10000f, full, 5);
            Assert.True(tenth < full && tenth > 0f);
        }

        [Fact]
        public void ExecutionPrice_BuysPayUp_SellsReceiveLess()
        {
            float mid = 1000f;
            float buy = Friction.ExecutionPrice(mid, true, CreditRating.BBB, 5f, 100f, 1000f);
            float sell = Friction.ExecutionPrice(mid, false, CreditRating.BBB, 5f, 100f, 1000f);
            Assert.True(buy > mid);
            Assert.True(sell < mid);
        }

        [Fact]
        public void DepthPerPeriod_Is20PercentOfOutstanding()
        {
            Assert.Equal(20000f, Friction.DepthPerPeriod(100000f), 1);
        }

        // WO-22 mandatory: migration distribution chi-square test. At AAA boundary
        // (cur=0) with home=AAA and zero hazard, the only possible outcomes are
        // stay-AAA or downgrade-to-AA. The pre-fix bug let the upgrade branch
        // (roll < pUp, cur > 0 fails) fall through to the downgrade branch, inflating
        // downgrades by ~44%. This test runs N trials and verifies the observed
        // upgrade/stay/downgrade counts match the expected probabilities within a
        // chi-square threshold.
        [Fact]
        public void WO22_MigrateAAA_DistributionMatchesExpected()
        {
            int N = 10000;
            var rng = new DeterministicRandom(12345);
            int upgrades = 0, stays = 0, downgrades = 0;

            for (int i = 0; i < N; i++)
            {
                bool defaulted;
                CreditRating result = IssuerModel.MigrateAnnual(
                    CreditRating.AAA, CreditRating.AAA, 0f, rng, out defaulted);
                Assert.False(defaulted);
                if ((int)result < (int)CreditRating.AAA) upgrades++;
                else if (result == CreditRating.AAA) stays++;
                else downgrades++;
            }

            // At AAA with home=AAA: pUp = 0.08, pDown = 0.08 (no mean-reversion bias).
            // But cur=0 means upgrade returns current (AAA), so effective:
            //   P(stay) = pUp + (1 - pUp - pDown) = 1 - pDown = 0.92
            //   P(downgrade) = pDown = 0.08
            //   P(upgrade) = 0
            Assert.Equal(0, upgrades);
            float expectedStay = N * 0.92f;
            float expectedDown = N * 0.08f;

            float chiSq = ((stays - expectedStay) * (stays - expectedStay)) / expectedStay
                        + ((downgrades - expectedDown) * (downgrades - expectedDown)) / expectedDown;

            // Chi-square critical value for 1 df at p=0.001 is 10.83.
            Assert.True(chiSq < 10.83f,
                $"Chi-square {chiSq:F2} exceeds critical value 10.83 (p<0.001). " +
                $"stays={stays} (exp {expectedStay}), down={downgrades} (exp {expectedDown})");
        }

        // WO-22: verify CCC boundary clamp prevents fall-through in the opposite direction.
        [Fact]
        public void WO22_MigrateCCC_DowngradeClampedToStay()
        {
            int N = 5000;
            var rng = new DeterministicRandom(99);
            int stayedCCC = 0;

            for (int i = 0; i < N; i++)
            {
                bool defaulted;
                CreditRating result = IssuerModel.MigrateAnnual(
                    CreditRating.CCC, CreditRating.CCC, 0f, rng, out defaulted);
                if (defaulted) continue;
                if (result == CreditRating.CCC) stayedCCC++;
                Assert.True((int)result <= (int)CreditRating.CCC,
                    "CCC should never migrate below CCC (only to D via hazard)");
            }
        }
    }
}
