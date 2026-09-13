using System.IO;
using Xunit;

namespace MyFirstMod.Tests
{
    // Audit Schema v5 spec, section 9 (serializer items): v7 round-trip, corrupt
    // checksum rejection, and v4 -> migrated-state. All pure, no game DLLs.
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
            s.Market.Add(new Bond("M1", "Mkt", 50000f, 0.06f, 8));

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
