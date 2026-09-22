// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using System.IO;
using Xunit;

namespace MyFirstMod.Tests
{
    // Audit Schema v5 spec, section 9 (serializer items): v8 round-trip (issuer
    // identity + roster + PRNG stream), v7 forward-compat, corrupt checksum
    // rejection, and v4 -> migrated-state. All pure, no game DLLs.
    public class StateSerializerTests
    {
        private static Bond IssuedBond(string id, float face, float coupon, int periods)
        {
            var b = new Bond(id, "Issue", face, coupon, periods);
            b.PlacedFraction = 0.5f;
            b.OutstandingPrincipal = face * 0.5f;
            b.Arrears = 123.4f;
            b.State = BondState.Delinquent;
            b.PeriodsInArrears = 1;
            b.DefaultedAtPeriod = -1;
            b.IssuePeriod = 7;
            return b;
        }

        private static BondMarketState SampleState()
        {
            var s = new BondMarketState();
            s.NextBondId = 11; s.NextSwapId = 3; s.TickCounter = 9; s.PeriodCounter = 42;
            s.DefaultPenalty = 5; s.TotalDefaults = 2; s.RealizedPL = 123.5f; s.SwapPL = -40.25f;
            s.WindowIndex = 17; s.Initialized = true; s.TransactionSeq = 88; s.PressureHistoryIndex = 4;
            s.PeriodsSinceReport = 1; s.QuarterNumber = 6; s.QuarterDefaults = 1;
            s.TotalCitizenProceeds = 9999.5f; s.LastDefaultPeriod = 40;
            s.ShortRate = 0.037f; s.CyclePhase = 1.23f;

            s.CashFlowHistory = new float[60];
            for (int i = 0; i < 60; i++) s.CashFlowHistory[i] = i * 1.5f;
            s.PressureHistory = new float[12];
            for (int i = 0; i < 12; i++) s.PressureHistory[i] = i * 0.01f;

            s.Issued.Add(IssuedBond("IB1", 100000f, 0.05f, 60));
            s.Portfolio.Add(new Bond("B1", "Port", 25000f, 0.04f, 12));
            var mkt = new Bond("M1", "Mkt", 50000f, 0.06f, 8);
            mkt.IssuerName = "Port Authority";        // v8: issuer identity on market paper
            mkt.IssuerRating = CreditRating.BBB;
            s.Market.Add(mkt);

            // v8: issuer roster + persisted PRNG stream (Phase 5).
            s.Issuers.Add(IssuerModel.MakeIssuer("Regional Water District", IssuerArchetype.WaterDistrict));
            var trans = IssuerModel.MakeIssuer("State Transit Auth", IssuerArchetype.TransitAuthority);
            trans.Rating = CreditRating.BB;           // migrated away from home
            trans.Defaulted = false;
            s.Issuers.Add(trans);
            s.RngState = new uint[] { 111u, 222u, 333u, 444u };

            var sw = new InterestRateSwap("SW1", 200000f, 0.045f, 24, true);
            sw.RemainingPeriods = 20; sw.CumulativePL = 15.5f; sw.LastSettlement = 2.1f;
            s.Swaps.Add(sw);

            var tx = new CimTransaction { Sequence = 1, BuyVolume = 10f, SellVolume = 4f, Pressure = 0.3f, Detail = "hi" };
            s.Transactions.Add(tx);

            var rp = new QuarterlyReport { Quarter = 6, Rating = CreditRating.BBB, CreditStatus = "OK",
                DSCR = 1.4f, NOI = 500f, Population = 12345, Outlook = "STABLE", RequiredYield = 0.06f };
            s.Reports.Add(rp);
            return s;
        }

        [Fact]
        public void V7_RoundTrips()
        {
            var s = SampleState();
            byte[] bytes = StateSerializer.Serialize(s);

            Assert.True(StateSerializer.TryDeserialize(bytes, out BondMarketState s2));
            Assert.NotNull(s2);

            Assert.Equal(s.NextBondId, s2.NextBondId);
            Assert.Equal(s.PeriodCounter, s2.PeriodCounter);
            Assert.Equal(s.LastDefaultPeriod, s2.LastDefaultPeriod);
            Assert.Equal(s.TotalCitizenProceeds, s2.TotalCitizenProceeds, 3);
            Assert.Equal(0.037f, s2.ShortRate, 4);   // Phase 4 rate state round-trips
            Assert.Equal(1.23f, s2.CyclePhase, 4);

            Assert.Equal(60, s2.CashFlowHistory.Length);
            Assert.Equal(58.5f, s2.CashFlowHistory[39], 3);
            Assert.Equal(12, s2.PressureHistory.Length);

            Assert.Single(s2.Issued);
            Bond ib = s2.Issued[0];
            Assert.Equal("IB1", ib.Id);
            Assert.Equal(0.5f, ib.PlacedFraction, 4);
            Assert.Equal(50000f, ib.OutstandingPrincipal, 2);
            Assert.Equal(123.4f, ib.Arrears, 2);
            Assert.Equal(BondState.Delinquent, ib.State);
            Assert.Equal(1, ib.PeriodsInArrears);
            Assert.Equal(7, ib.IssuePeriod);

            Assert.Single(s2.Portfolio);
            Assert.Single(s2.Market);
            Assert.Single(s2.Swaps);
            Assert.Equal(200000f, s2.Swaps[0].NotionalAmount, 2);
            Assert.Single(s2.Transactions);
            Assert.Equal("hi", s2.Transactions[0].Detail);
            Assert.Single(s2.Reports);
            Assert.Equal(CreditRating.BBB, s2.Reports[0].Rating);

            // v8: issuer identity on the market bond round-trips.
            Assert.Equal("Port Authority", s2.Market[0].IssuerName);
            Assert.Equal(CreditRating.BBB, s2.Market[0].IssuerRating);

            // v8: issuer roster round-trips with migrated rating and archetype.
            Assert.Equal(2, s2.Issuers.Count);
            Assert.Equal("Regional Water District", s2.Issuers[0].Name);
            Assert.Equal(IssuerArchetype.WaterDistrict, s2.Issuers[0].Archetype);
            Assert.Equal("State Transit Auth", s2.Issuers[1].Name);
            Assert.Equal(IssuerArchetype.TransitAuthority, s2.Issuers[1].Archetype);
            Assert.Equal(CreditRating.BB, s2.Issuers[1].Rating);
            Assert.Equal(CreditRating.BBB, s2.Issuers[1].HomeRating);

            // v8: PRNG stream round-trips.
            Assert.Equal(4, s2.RngState.Length);
            Assert.Equal(111u, s2.RngState[0]);
            Assert.Equal(444u, s2.RngState[3]);
        }

        // A v7 stream (sectioned, but with no issuer identity, roster, or PRNG
        // section) must load through the shared reader: bonds default to empty
        // issuer + A rating, and Issuers / RngState come back empty.
        [Fact]
        public void V7Fixture_LoadsForward_WithDefaultedIssuerFields()
        {
            byte[] v7 = BuildV7Fixture();

            Assert.True(StateSerializer.TryDeserialize(v7, out BondMarketState s));
            Assert.NotNull(s);
            Assert.Equal(0.037f, s.ShortRate, 4);      // Phase 4 rate state still read
            Assert.Equal(1.23f, s.CyclePhase, 4);

            Assert.Single(s.Market);
            Assert.Equal("", s.Market[0].IssuerName);  // defaulted, not garbage
            Assert.Equal(CreditRating.A, s.Market[0].IssuerRating);

            Assert.Empty(s.Issuers);                   // no roster in a v7 save
            Assert.Empty(s.RngState);                  // no persisted PRNG stream
        }

        // Builds a minimal but well-formed v7 sectioned stream (one market bond),
        // using the same section framing the serializer emits, so the forward-compat
        // path is exercised against a real v7 layout rather than a re-serialized v8.
        private static byte[] BuildV7Fixture()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((byte)7); // version

            // scalars section: the 17 core fields + Phase 4 ShortRate/CyclePhase,
            // and NO trailing PRNG length word (that is the v8-era addition).
            using (var sec = new MemoryStream())
            {
                var sw = new BinaryWriter(sec);
                sw.Write(11); sw.Write(3); sw.Write(9);          // NextBondId, NextSwapId, TickCounter
                sw.Write(42); sw.Write(5); sw.Write(2);          // PeriodCounter, DefaultPenalty, TotalDefaults
                sw.Write(0f); sw.Write(0f); sw.Write(0);         // RealizedPL, SwapPL, WindowIndex
                sw.Write(true); sw.Write(0); sw.Write(0);        // Initialized, TransactionSeq, PressureHistoryIndex
                sw.Write(0); sw.Write(6); sw.Write(1);           // PeriodsSinceReport, QuarterNumber, QuarterDefaults
                sw.Write(0f); sw.Write(-1);                      // TotalCitizenProceeds, LastDefaultPeriod
                sw.Write(0.037f); sw.Write(1.23f);              // Phase 4 ShortRate, CyclePhase
                WriteFixtureSection(w, sec);
            }

            WriteFixtureFloatArray(w, new float[60]); // CashFlowHistory
            WriteFixtureFloatArray(w, new float[12]); // PressureHistory
            WriteFixtureBondList(w, 0);               // Portfolio
            WriteFixtureBondList(w, 0);               // Issued
            WriteFixtureBondList(w, 0);               // Redeemed
            WriteFixtureBondList(w, 1);               // Market (one v7 bond, 15 fields)

            WriteFixtureCountOnly(w); // Swaps
            WriteFixtureCountOnly(w); // Transactions
            WriteFixtureCountOnly(w); // Reports
            // NO issuer section: that is the v8 addition.

            w.Flush();
            return ms.ToArray();
        }

        private static uint Fnv1a(byte[] data)
        {
            uint h = 2166136261u;
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= 16777619u; }
            return h;
        }

        private static void WriteFixtureSection(BinaryWriter w, MemoryStream sec)
        {
            byte[] payload = sec.ToArray();
            w.Write(payload.Length);
            w.Write(Fnv1a(payload));
            w.Write(payload);
        }

        private static void WriteFixtureFloatArray(BinaryWriter w, float[] arr)
        {
            using (var sec = new MemoryStream())
            {
                var sw = new BinaryWriter(sec);
                sw.Write(arr.Length);
                for (int i = 0; i < arr.Length; i++) sw.Write(arr[i]);
                WriteFixtureSection(w, sec);
            }
        }

        private static void WriteFixtureBondList(BinaryWriter w, int count)
        {
            using (var sec = new MemoryStream())
            {
                var sw = new BinaryWriter(sec);
                sw.Write(count);
                for (int i = 0; i < count; i++)
                {
                    sw.Write("M1"); sw.Write("Mkt"); sw.Write(50000f); sw.Write(0.06f);
                    sw.Write(8); sw.Write(8); sw.Write(0f); sw.Write(0f);
                    sw.Write(1f); sw.Write(50000f); sw.Write(0f); sw.Write((int)BondState.Active);
                    sw.Write(0); sw.Write(-1); sw.Write(0);
                    // v7: no issuer fields trailing the 15 core fields.
                }
                WriteFixtureSection(w, sec);
            }
        }

        private static void WriteFixtureCountOnly(BinaryWriter w)
        {
            using (var sec = new MemoryStream())
            {
                var sw = new BinaryWriter(sec);
                sw.Write(0);
                WriteFixtureSection(w, sec);
            }
        }

        [Fact]
        public void V7_Serialize_IsDeterministic()
        {
            var s = SampleState();
            byte[] a = StateSerializer.Serialize(s);
            byte[] b = StateSerializer.Serialize(s);
            Assert.Equal(a, b); // byte-identical round-trip of the same state
        }

        [Fact]
        public void CorruptChecksum_FailsAndLeavesNoState()
        {
            var s = SampleState();
            byte[] bytes = StateSerializer.Serialize(s);

            // Flip a byte inside the first section's payload (past version + the
            // first [len][checksum] header at offsets 1..8).
            bytes[10] ^= 0xFF;

            Assert.False(StateSerializer.TryDeserialize(bytes, out BondMarketState s2));
            Assert.Null(s2);
        }

        [Fact]
        public void Garbage_And_Empty_FailCleanly()
        {
            Assert.False(StateSerializer.TryDeserialize(null, out _));
            Assert.False(StateSerializer.TryDeserialize(new byte[0], out _));
            Assert.False(StateSerializer.TryDeserialize(new byte[] { 99, 1, 2, 3 }, out _)); // unknown version
            Assert.False(StateSerializer.TryDeserialize(new byte[] { 7, 1, 2 }, out _));     // v7 but truncated
        }

        // A hand-built v4 stream (the pre-Schema-v5 flat format) must migrate so the
        // single old SoldFraction becomes PlacedFraction, with outstanding = placed
        // face and a fresh Active lifecycle (spec section 5).
        [Fact]
        public void V4Fixture_MigratesSoldFractionToPlacement()
        {
            byte[] v4 = BuildV4Fixture(soldFraction: 0.5f, face: 100000f, coupon: 0.05f, periods: 60);

            Assert.True(StateSerializer.TryDeserialize(v4, out BondMarketState s));
            Assert.NotNull(s);
            Assert.Single(s.Issued);

            Bond ib = s.Issued[0];
            Assert.Equal(0.5f, ib.PlacedFraction, 4);              // placed = old SoldFraction
            Assert.Equal(50000f, ib.OutstandingPrincipal, 2);     // owed = placed face
            Assert.Equal(0f, ib.Arrears, 4);
            Assert.Equal(BondState.Active, ib.State);             // no distress to reconstruct
            Assert.Equal(-1, s.LastDefaultPeriod);
            Assert.Equal(0.04f, s.ShortRate, 4);                 // pre-Phase-4 default rate
        }

        // Writes the exact v4 flat layout the legacy reader expects, with one issued
        // bond and everything else empty.
        private static byte[] BuildV4Fixture(float soldFraction, float face, float coupon, int periods)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((byte)4); // version

            // scalar header (11 values)
            w.Write(5);      // nextBondId
            w.Write(0);      // nextSwapId
            w.Write(3);      // tickCounter
            w.Write(0);      // defaultPenalty
            w.Write(0);      // totalDefaults
            w.Write(10.0f);  // realizedPL
            w.Write(0.0f);   // swapPL
            w.Write(12);     // windowIndex
            w.Write(true);   // initialized
            w.Write(0);      // transactionSeq
            w.Write(0);      // pressureHistoryIndex

            for (int i = 0; i < 60; i++) w.Write(0f); // cashFlowHistory
            for (int i = 0; i < 12; i++) w.Write(0f); // pressureHistory

            WriteV4BondList(w, 0, soldFraction, face, coupon, periods); // portfolio (empty)
            WriteV4BondList(w, 1, soldFraction, face, coupon, periods); // issued (1 bond)
            WriteV4BondList(w, 0, soldFraction, face, coupon, periods); // market (empty)

            w.Write(0); // swap count
            w.Write(0); // transaction count

            // version >= 2 report block
            w.Write(0); // periodsSinceReport
            w.Write(0); // quarterNumber
            w.Write(0); // quarterDefaults
            w.Write(0); // report count

            // version >= 3
            w.Write(0f); // totalCitizenProceeds

            w.Flush();
            return ms.ToArray();
        }

        private static void WriteV4BondList(BinaryWriter w, int count, float soldFraction, float face, float coupon, int periods)
        {
            w.Write(count);
            for (int i = 0; i < count; i++)
            {
                w.Write("IB1");      // Id
                w.Write("Legacy");   // Name
                w.Write(face);
                w.Write(coupon);
                w.Write(periods);    // TotalPeriods
                w.Write(periods);    // RemainingPeriods
                w.Write(0f);         // PurchasePrice
                w.Write(0f);         // CouponsReceived
                w.Write(soldFraction); // the single v4 field
            }
        }
    }
}

#endif
