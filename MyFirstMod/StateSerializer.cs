using System;
using System.Collections.Generic;
using System.IO;

namespace MyFirstMod
{
    // Pure, dependency-free snapshot of everything the bond market persists.
    // The engine packs its fields into this, hands it to StateSerializer, and on
    // load applies a validated instance back. Because it carries no Unity / game
    // types, serialization + migration are fully unit-testable on CI.
    public class BondMarketState
    {
        // scalars
        public int NextBondId;
        public int NextSwapId;
        public int TickCounter;
        public int PeriodCounter;
        public int DefaultPenalty;
        public int TotalDefaults;
        public float RealizedPL;
        public float SwapPL;
        public int WindowIndex;
        public bool Initialized;
        public int TransactionSeq;
        public int PressureHistoryIndex;
        public int PeriodsSinceReport;
        public int QuarterNumber;
        public int QuarterDefaults;
        public float TotalCitizenProceeds;
        public int LastDefaultPeriod = -1;
        // Phase 4: short rate + business-cycle phase (defaults chosen so pre-Phase-4
        // saves resume at the old constant rate).
        public float ShortRate = 0.04f;
        public float CyclePhase = 0f;
        // Phase 5 (G-1): the PRNG's serialized state (xorshift128, 4 words). Empty
        // means "no persisted stream" - the engine reseeds. Pre-Phase-5 saves leave
        // this empty and the engine seeds fresh on load.
        public uint[] RngState = new uint[0];

        // WO-10: player-adjustable settings, persisted so they survive save/load.
        public float HazardMultiplier = IssuerModel.HAZARD_STANDARD;
        public float RateVolatilityScale = 1f;
        public bool CitizenTradingEnabled = true;
        public bool RevenueBondsEnabled;

        // windows
        public float[] CashFlowHistory = new float[0];
        public float[] PressureHistory = new float[0];

        // collections
        public List<Bond> Portfolio = new List<Bond>();
        public List<Bond> Issued = new List<Bond>();
        public List<Bond> Redeemed = new List<Bond>();
        public List<Bond> Market = new List<Bond>();
        public List<InterestRateSwap> Swaps = new List<InterestRateSwap>();
        public List<CimTransaction> Transactions = new List<CimTransaction>();
        public List<QuarterlyReport> Reports = new List<QuarterlyReport>();
        // Phase 5 (P1-2): the market's issuer roster with its migrated credit. Empty
        // for pre-Phase-5 saves; the engine re-seeds the standard roster on load.
        public List<MarketIssuer> Issuers = new List<MarketIssuer>();
    }

    // Audit Schema v5 serializer contract (spec section 6): the go-forward format
    // is length-prefixed and split into per-section checksummed blocks; deserialize
    // validates every checksum and the invariants (section 7) before anything is
    // returned, so a corrupt or nonsense save fails cleanly and the caller keeps
    // its prior state. Legacy saves (v1-6, the flat pre-Schema-v5 format) are
    // migrated on read. v7 and v8 share the sectioned reader: v8 adds per-bond
    // issuer identity, the issuer roster, and the PRNG stream (Phase 5), and a v7
    // save loads by defaulting those. No exception ever escapes TryDeserialize.
    public static class StateSerializer
    {
        public const byte FORMAT_VERSION = 12;
        private const float EPS = 0.01f;

        // ---- FNV-1a 32-bit section checksum ----
        private static uint Fnv1a(byte[] data, int len)
        {
            uint h = 2166136261u;
            for (int i = 0; i < len; i++)
            {
                h ^= data[i];
                h *= 16777619u;
            }
            return h;
        }

        private static void WriteSection(BinaryWriter w, MemoryStream section)
        {
            byte[] payload = section.ToArray();
            w.Write(payload.Length);
            w.Write(Fnv1a(payload, payload.Length));
            w.Write(payload);
        }

        // Reads one [length][checksum][bytes] section, verifying the checksum.
        private static BinaryReader ReadSection(BinaryReader r)
        {
            int len = r.ReadInt32();
            if (len < 0 || len > (16 * 1024 * 1024)) throw new InvalidDataException("section length");
            uint expected = r.ReadUInt32();
            byte[] payload = r.ReadBytes(len);
            if (payload.Length != len) throw new EndOfStreamException("section truncated");
            if (Fnv1a(payload, payload.Length) != expected) throw new InvalidDataException("section checksum mismatch");
            return new BinaryReader(new MemoryStream(payload, false));
        }

        // ================= SERIALIZE (v8) =================

        public static byte[] Serialize(BondMarketState s)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(FORMAT_VERSION);

            // scalars
            using (MemoryStream sec = new MemoryStream())
            {
                BinaryWriter sw = new BinaryWriter(sec);
                sw.Write(s.NextBondId); sw.Write(s.NextSwapId); sw.Write(s.TickCounter);
                sw.Write(s.PeriodCounter); sw.Write(s.DefaultPenalty); sw.Write(s.TotalDefaults);
                sw.Write(s.RealizedPL); sw.Write(s.SwapPL); sw.Write(s.WindowIndex);
                sw.Write(s.Initialized); sw.Write(s.TransactionSeq); sw.Write(s.PressureHistoryIndex);
                sw.Write(s.PeriodsSinceReport); sw.Write(s.QuarterNumber); sw.Write(s.QuarterDefaults);
                sw.Write(s.TotalCitizenProceeds); sw.Write(s.LastDefaultPeriod);
                // Phase 4: appended at the end of the scalars section. Older readers
                // simply stop before these; newer readers pick them up if present.
                sw.Write(s.ShortRate); sw.Write(s.CyclePhase);
                // Phase 5 (G-1): PRNG stream, length-prefixed and appended after the
                // Phase 4 fields. Length 0 means "no persisted stream".
                uint[] rng = s.RngState != null ? s.RngState : new uint[0];
                sw.Write(rng.Length);
                for (int i = 0; i < rng.Length; i++) sw.Write(rng[i]);
                sw.Write(s.HazardMultiplier);
                sw.Write(s.RateVolatilityScale);
                sw.Write(s.CitizenTradingEnabled);
                sw.Write(s.RevenueBondsEnabled);
                WriteSection(w, sec);
            }

            WriteFloatArraySection(w, s.CashFlowHistory);
            WriteFloatArraySection(w, s.PressureHistory);
            WriteBondSection(w, s.Portfolio);
            WriteBondSection(w, s.Issued);
            WriteBondSection(w, s.Redeemed);
            WriteBondSection(w, s.Market);
            WriteSwapSection(w, s.Swaps);
            WriteTransactionSection(w, s.Transactions);
            WriteReportSection(w, s.Reports);
            WriteIssuerSection(w, s.Issuers);   // v8

            w.Flush();
            return ms.ToArray();
        }

        private static void WriteFloatArraySection(BinaryWriter w, float[] arr)
        {
            using (MemoryStream sec = new MemoryStream())
            {
                BinaryWriter sw = new BinaryWriter(sec);
                sw.Write(arr.Length);
                for (int i = 0; i < arr.Length; i++) sw.Write(arr[i]);
                WriteSection(w, sec);
            }
        }

        private static void WriteBondSection(BinaryWriter w, List<Bond> bonds)
        {
            using (MemoryStream sec = new MemoryStream())
            {
                BinaryWriter sw = new BinaryWriter(sec);
                sw.Write(bonds.Count);
                for (int i = 0; i < bonds.Count; i++) WriteBond(sw, bonds[i]);
                WriteSection(w, sec);
            }
        }

        private static void WriteBond(BinaryWriter sw, Bond b)
        {
            sw.Write(b.Id); sw.Write(b.Name); sw.Write(b.FaceValue); sw.Write(b.CouponRate);
            sw.Write(b.TotalPeriods); sw.Write(b.RemainingPeriods); sw.Write(b.PurchasePrice);
            sw.Write(b.CouponsReceived); sw.Write(b.PlacedFraction); sw.Write(b.OutstandingPrincipal);
            sw.Write(b.Arrears); sw.Write((int)b.State); sw.Write(b.PeriodsInArrears);
            sw.Write(b.DefaultedAtPeriod); sw.Write(b.IssuePeriod);
            // v8: issuer identity carried by market/portfolio bonds (Phase 5, P1-2).
            sw.Write(b.IssuerName != null ? b.IssuerName : "");
            sw.Write((int)b.IssuerRating);
            // v9: revenue source backing (Phase 6, WO-11).
            sw.Write((int)b.Revenue);
            // v12: interest/principal split (WO-25).
            sw.Write(b.InterestPaid);
            sw.Write(b.PrincipalRepaid);
        }

        // Sectioned-format bond reader shared by v7 and v8. The 15 core fields are
        // identical; v8 trails the issuer identity, which v7 saves default (empty
        // name, A rating - i.e. "the city stands behind it").
        private static Bond ReadBond(BinaryReader r, byte version)
        {
            string id = r.ReadString(); string name = r.ReadString();
            float face = r.ReadSingle(); float coupon = r.ReadSingle();
            int totalP = r.ReadInt32(); int remainP = r.ReadInt32();
            float purchase = r.ReadSingle(); float couponsRcvd = r.ReadSingle();
            float placed = r.ReadSingle(); float outstanding = r.ReadSingle();
            float arrears = r.ReadSingle(); int state = r.ReadInt32();
            int pia = r.ReadInt32(); int defAt = r.ReadInt32(); int issueP = r.ReadInt32();

            Bond b = new Bond(id, name, face, coupon, totalP);
            b.RemainingPeriods = remainP; b.PurchasePrice = purchase; b.CouponsReceived = couponsRcvd;
            b.PlacedFraction = placed; b.OutstandingPrincipal = outstanding; b.Arrears = arrears;
            b.State = (BondState)state; b.PeriodsInArrears = pia; b.DefaultedAtPeriod = defAt; b.IssuePeriod = issueP;

            if (version >= 8)
            {
                b.IssuerName = r.ReadString();
                b.IssuerRating = (CreditRating)r.ReadInt32();
            }
            if (version >= 9)
            {
                b.Revenue = (RevenueSource)r.ReadInt32();
            }
            if (version >= 12)
            {
                b.InterestPaid = r.ReadSingle();
                b.PrincipalRepaid = r.ReadSingle();
            }
            return b;
        }

        private static void WriteSwapSection(BinaryWriter w, List<InterestRateSwap> swaps)
        {
            using (MemoryStream sec = new MemoryStream())
            {
                BinaryWriter sw = new BinaryWriter(sec);
                sw.Write(swaps.Count);
                for (int i = 0; i < swaps.Count; i++)
                {
                    InterestRateSwap s = swaps[i];
                    sw.Write(s.Id); sw.Write(s.NotionalAmount); sw.Write(s.FixedRate);
                    sw.Write(s.TotalPeriods); sw.Write(s.RemainingPeriods); sw.Write(s.PayFixed);
                    sw.Write(s.CumulativePL); sw.Write(s.LastSettlement); sw.Write(s.UnpaidSettlement);
                }
                WriteSection(w, sec);
            }
        }

        private static InterestRateSwap ReadSwap(BinaryReader r, byte version)
        {
            string id = r.ReadString(); float notional = r.ReadSingle(); float fixedRate = r.ReadSingle();
            int totalP = r.ReadInt32(); int remainP = r.ReadInt32(); bool payFixed = r.ReadBoolean();
            float cumPL = r.ReadSingle(); float lastS = r.ReadSingle();
            InterestRateSwap s = new InterestRateSwap(id, notional, fixedRate, totalP, payFixed);
            s.RemainingPeriods = remainP; s.CumulativePL = cumPL; s.LastSettlement = lastS;
            if (version >= 11) s.UnpaidSettlement = r.ReadSingle();
            return s;
        }

        private static void WriteTransactionSection(BinaryWriter w, List<CimTransaction> txs)
        {
            using (MemoryStream sec = new MemoryStream())
            {
                BinaryWriter sw = new BinaryWriter(sec);
                sw.Write(txs.Count);
                for (int i = 0; i < txs.Count; i++)
                {
                    CimTransaction tx = txs[i];
                    sw.Write(tx.Sequence); sw.Write(tx.BuyVolume); sw.Write(tx.SellVolume);
                    sw.Write(tx.Pressure); sw.Write(tx.Detail != null ? tx.Detail : "");
                }
                WriteSection(w, sec);
            }
        }

        private static CimTransaction ReadTransaction(BinaryReader r)
        {
            CimTransaction tx = new CimTransaction();
            tx.Sequence = r.ReadInt32(); tx.BuyVolume = r.ReadSingle(); tx.SellVolume = r.ReadSingle();
            tx.Pressure = r.ReadSingle(); tx.Detail = r.ReadString();
            return tx;
        }

        private static void WriteReportSection(BinaryWriter w, List<QuarterlyReport> reports)
        {
            using (MemoryStream sec = new MemoryStream())
            {
                BinaryWriter sw = new BinaryWriter(sec);
                sw.Write(reports.Count);
                for (int i = 0; i < reports.Count; i++) WriteReport(sw, reports[i]);
                WriteSection(w, sec);
            }
        }

        private static void WriteReport(BinaryWriter sw, QuarterlyReport rp)
        {
            sw.Write(rp.Quarter); sw.Write((int)rp.Rating); sw.Write(rp.CreditStatus != null ? rp.CreditStatus : "");
            sw.Write(rp.DSCR); sw.Write(rp.DebtBurden); sw.Write(rp.GrossIncome); sw.Write(rp.TotalExpenses);
            sw.Write(rp.NOI); sw.Write(rp.DefaultProbability); sw.Write(rp.IssuedBonds); sw.Write(rp.MaxBonds);
            sw.Write(rp.DebtFace); sw.Write(rp.DebtOwed); sw.Write(rp.AvgSubscription); sw.Write(rp.CouponsPaid);
            sw.Write(rp.QuarterDefaults); sw.Write(rp.TotalDefaults); sw.Write(rp.BenchmarkRate); sw.Write(rp.RequiredYield);
            sw.Write(rp.DemandScore); sw.Write(rp.SmoothedPressure); sw.Write(rp.AbsorptionCapacity); sw.Write(rp.Population);
            sw.Write(rp.PortfolioBonds); sw.Write(rp.SwapCount); sw.Write(rp.HedgedNotional); sw.Write(rp.RealizedPL);
            sw.Write(rp.SwapPL); sw.Write(rp.RevenueVolatility); sw.Write(rp.Outlook != null ? rp.Outlook : "");
            sw.Write(rp.Happiness); sw.Write(rp.EmploymentRate); sw.Write(rp.PopulationGrowth); sw.Write(rp.CitizenConfidence);
            sw.Write(rp.BondAppeal); sw.Write(rp.FinancialHealth); sw.Write(rp.CitizenProceeds);
        }

        private static QuarterlyReport ReadReportV7(BinaryReader r)
        {
            QuarterlyReport rp = new QuarterlyReport();
            rp.Quarter = r.ReadInt32(); rp.Rating = (CreditRating)r.ReadInt32(); rp.CreditStatus = r.ReadString();
            rp.DSCR = r.ReadSingle(); rp.DebtBurden = r.ReadSingle(); rp.GrossIncome = r.ReadSingle(); rp.TotalExpenses = r.ReadSingle();
            rp.NOI = r.ReadSingle(); rp.DefaultProbability = r.ReadSingle(); rp.IssuedBonds = r.ReadInt32(); rp.MaxBonds = r.ReadInt32();
            rp.DebtFace = r.ReadSingle(); rp.DebtOwed = r.ReadSingle(); rp.AvgSubscription = r.ReadSingle(); rp.CouponsPaid = r.ReadSingle();
            rp.QuarterDefaults = r.ReadInt32(); rp.TotalDefaults = r.ReadInt32(); rp.BenchmarkRate = r.ReadSingle(); rp.RequiredYield = r.ReadSingle();
            rp.DemandScore = r.ReadSingle(); rp.SmoothedPressure = r.ReadSingle(); rp.AbsorptionCapacity = r.ReadSingle(); rp.Population = r.ReadInt32();
            rp.PortfolioBonds = r.ReadInt32(); rp.SwapCount = r.ReadInt32(); rp.HedgedNotional = r.ReadSingle(); rp.RealizedPL = r.ReadSingle();
            rp.SwapPL = r.ReadSingle(); rp.RevenueVolatility = r.ReadSingle(); rp.Outlook = r.ReadString();
            rp.Happiness = r.ReadSingle(); rp.EmploymentRate = r.ReadSingle(); rp.PopulationGrowth = r.ReadSingle(); rp.CitizenConfidence = r.ReadSingle();
            rp.BondAppeal = r.ReadSingle(); rp.FinancialHealth = r.ReadSingle(); rp.CitizenProceeds = r.ReadSingle();
            return rp;
        }

        private static void WriteIssuerSection(BinaryWriter w, List<MarketIssuer> issuers)
        {
            using (MemoryStream sec = new MemoryStream())
            {
                BinaryWriter sw = new BinaryWriter(sec);
                int n = issuers != null ? issuers.Count : 0;
                sw.Write(n);
                for (int i = 0; i < n; i++)
                {
                    MarketIssuer m = issuers[i];
                    sw.Write(m.Name != null ? m.Name : "");
                    sw.Write((int)m.Archetype);
                    sw.Write((int)m.HomeRating);
                    sw.Write((int)m.Rating);
                    sw.Write(m.Defaulted);
                }
                WriteSection(w, sec);
            }
        }

        private static List<MarketIssuer> ReadIssuerSection(BinaryReader r)
        {
            BinaryReader sr = ReadSection(r);
            int n = sr.ReadInt32();
            List<MarketIssuer> list = new List<MarketIssuer>();
            for (int i = 0; i < n; i++)
            {
                MarketIssuer m = new MarketIssuer();
                m.Name = sr.ReadString();
                m.Archetype = (IssuerArchetype)sr.ReadInt32();
                m.HomeRating = (CreditRating)sr.ReadInt32();
                m.Rating = (CreditRating)sr.ReadInt32();
                m.Defaulted = sr.ReadBoolean();
                list.Add(m);
            }
            return list;
        }

        // ================= DESERIALIZE (staged, atomic) =================

        // Returns true and a validated state on success; false and null on any
        // failure (corrupt data, bad checksum, unknown version, failed invariants).
        // Never throws, never mutates anything the caller holds.
        public static bool TryDeserialize(byte[] data, out BondMarketState state)
        {
            state = null;
            if (data == null || data.Length < 1) return false;
            try
            {
                BinaryReader r = new BinaryReader(new MemoryStream(data, false));
                byte version = r.ReadByte();

                BondMarketState staging;
                if (version >= 7 && version <= 12)
                    staging = ReadSectioned(r, version);
                else if (version >= 1 && version <= 6)
                    staging = ReadLegacyFlat(r, version);
                else
                    return false;

                if (!ValidateState(staging)) return false;

                state = staging; // single atomic hand-off
                return true;
            }
            catch
            {
                state = null;
                return false;
            }
        }

        // Shared reader for the sectioned format (v7 and v8). Every trailing field
        // added in v8 is read only when present (a longer scalars section, or a
        // higher version), so a v7 save loads through the same path and defaults them.
        private static BondMarketState ReadSectioned(BinaryReader r, byte version)
        {
            BondMarketState s = new BondMarketState();

            BinaryReader sc = ReadSection(r);
            s.NextBondId = sc.ReadInt32(); s.NextSwapId = sc.ReadInt32(); s.TickCounter = sc.ReadInt32();
            s.PeriodCounter = sc.ReadInt32(); s.DefaultPenalty = sc.ReadInt32(); s.TotalDefaults = sc.ReadInt32();
            s.RealizedPL = sc.ReadSingle(); s.SwapPL = sc.ReadSingle(); s.WindowIndex = sc.ReadInt32();
            s.Initialized = sc.ReadBoolean(); s.TransactionSeq = sc.ReadInt32(); s.PressureHistoryIndex = sc.ReadInt32();
            s.PeriodsSinceReport = sc.ReadInt32(); s.QuarterNumber = sc.ReadInt32(); s.QuarterDefaults = sc.ReadInt32();
            s.TotalCitizenProceeds = sc.ReadSingle(); s.LastDefaultPeriod = sc.ReadInt32();
            // Phase 4: read the appended rate state only if the scalars section
            // carries it (backward compatible with pre-Phase-4 v7 saves).
            if (sc.BaseStream.Position + 8 <= sc.BaseStream.Length)
            {
                s.ShortRate = sc.ReadSingle();
                s.CyclePhase = sc.ReadSingle();
            }
            // Phase 5 (G-1): the PRNG stream, length-prefixed, trails the Phase 4
            // fields. Read only when the section still carries at least the length
            // word (pre-Phase-5 saves stop before it and leave RngState empty).
            if (sc.BaseStream.Position + 4 <= sc.BaseStream.Length)
            {
                int rngLen = sc.ReadInt32();
                if (rngLen < 0 || rngLen > 64) throw new InvalidDataException("rng state length");
                uint[] rng = new uint[rngLen];
                for (int i = 0; i < rngLen; i++) rng[i] = sc.ReadUInt32();
                s.RngState = rng;
            }
            if (sc.BaseStream.Position + 9 <= sc.BaseStream.Length)
            {
                s.HazardMultiplier = sc.ReadSingle();
                s.RateVolatilityScale = sc.ReadSingle();
                s.CitizenTradingEnabled = sc.ReadBoolean();
                if (version >= 10)
                    s.RevenueBondsEnabled = sc.ReadBoolean();
            }

            s.CashFlowHistory = ReadFloatArraySection(r);
            s.PressureHistory = ReadFloatArraySection(r);
            s.Portfolio = ReadBondSection(r, version);
            s.Issued = ReadBondSection(r, version);
            s.Redeemed = ReadBondSection(r, version);
            s.Market = ReadBondSection(r, version);

            BinaryReader sw = ReadSection(r);
            int swapCount = sw.ReadInt32();
            for (int i = 0; i < swapCount; i++) s.Swaps.Add(ReadSwap(sw, version));

            BinaryReader tr = ReadSection(r);
            int txCount = tr.ReadInt32();
            for (int i = 0; i < txCount; i++) s.Transactions.Add(ReadTransaction(tr));

            BinaryReader rr = ReadSection(r);
            int repCount = rr.ReadInt32();
            for (int i = 0; i < repCount; i++) s.Reports.Add(ReadReportV7(rr));

            // v8: issuer roster section trails the reports. v7 saves have no such
            // section and leave Issuers empty (the engine reseeds the roster).
            if (version >= 8)
                s.Issuers = ReadIssuerSection(r);

            return s;
        }

        private static float[] ReadFloatArraySection(BinaryReader r)
        {
            BinaryReader sr = ReadSection(r);
            int n = sr.ReadInt32();
            if (n < 0 || n > 100000) throw new InvalidDataException("array length");
            float[] arr = new float[n];
            for (int i = 0; i < n; i++) arr[i] = sr.ReadSingle();
            return arr;
        }

        private static List<Bond> ReadBondSection(BinaryReader r, byte version)
        {
            BinaryReader sr = ReadSection(r);
            int n = sr.ReadInt32();
            List<Bond> list = new List<Bond>();
            for (int i = 0; i < n; i++) list.Add(ReadBond(sr, version));
            return list;
        }

        // ---- legacy flat format (v1-6), migrated (spec section 5) ----

        private static BondMarketState ReadLegacyFlat(BinaryReader r, byte version)
        {
            BondMarketState s = new BondMarketState();

            s.NextBondId = r.ReadInt32(); s.NextSwapId = r.ReadInt32(); s.TickCounter = r.ReadInt32();
            s.DefaultPenalty = r.ReadInt32(); s.TotalDefaults = r.ReadInt32();
            s.RealizedPL = r.ReadSingle(); s.SwapPL = r.ReadSingle(); s.WindowIndex = r.ReadInt32();
            s.Initialized = r.ReadBoolean(); s.TransactionSeq = r.ReadInt32(); s.PressureHistoryIndex = r.ReadInt32();

            float[] cash = new float[60];
            for (int i = 0; i < 60; i++) cash[i] = r.ReadSingle();
            s.CashFlowHistory = cash;
            float[] pressure = new float[12];
            for (int i = 0; i < 12; i++) pressure[i] = r.ReadSingle();
            s.PressureHistory = pressure;

            s.Portfolio = ReadLegacyBondList(r, version);
            s.Issued = ReadLegacyBondList(r, version);
            s.Market = ReadLegacyBondList(r, version);

            int swapCount = r.ReadInt32();
            for (int i = 0; i < swapCount; i++) s.Swaps.Add(ReadSwap(r, version));

            int txCount = r.ReadInt32();
            for (int i = 0; i < txCount; i++) s.Transactions.Add(ReadTransaction(r));

            if (version >= 2)
            {
                s.PeriodsSinceReport = r.ReadInt32();
                s.QuarterNumber = r.ReadInt32();
                s.QuarterDefaults = r.ReadInt32();
                int reportCount = r.ReadInt32();
                for (int i = 0; i < reportCount; i++) s.Reports.Add(ReadLegacyReport(r, version));
            }

            if (version >= 3)
                s.TotalCitizenProceeds = r.ReadSingle();

            // v6 trailed a _defaultLockout int; it has no clean mapping to the new
            // lastDefaultPeriod, so consume and discard it. State is re-derived from
            // arrears below, and lastDefaultPeriod stays -1 (no reconstructed history).
            if (version >= 6)
                r.ReadInt32();

            s.LastDefaultPeriod = -1;
            s.PeriodCounter = 0; // absent in legacy; start the lifecycle clock fresh
            return s;
        }

        private static List<Bond> ReadLegacyBondList(BinaryReader r, byte version)
        {
            List<Bond> list = new List<Bond>();
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string id = r.ReadString(); string name = r.ReadString();
                float face = r.ReadSingle(); float coupon = r.ReadSingle();
                int totalP = r.ReadInt32(); int remainP = r.ReadInt32();
                float purchase = r.ReadSingle(); float couponsRcvd = r.ReadSingle();

                Bond b = new Bond(id, name, face, coupon, totalP);
                b.RemainingPeriods = remainP; b.PurchasePrice = purchase; b.CouponsReceived = couponsRcvd;

                if (version >= 5)
                {
                    b.PlacedFraction = r.ReadSingle();
                    b.OutstandingPrincipal = r.ReadSingle();
                }
                else
                {
                    // Migration (spec section 5): the old SoldFraction was take-up
                    // AND outstanding. Best estimate: placed = soldFrac, owed = the
                    // placed face.
                    float soldFrac = r.ReadSingle();
                    b.PlacedFraction = soldFrac;
                    b.OutstandingPrincipal = face * soldFrac;
                }

                bool legacyInDefault = false;
                if (version >= 6)
                {
                    b.Arrears = r.ReadSingle();
                    legacyInDefault = r.ReadBoolean();
                }

                // Derive the lifecycle state from what the legacy save knew.
                if (legacyInDefault) b.State = BondState.Defaulted;
                else if (b.Arrears > EPS) b.State = BondState.Delinquent;
                else b.State = BondState.Active;
                b.DefaultedAtPeriod = legacyInDefault ? 0 : -1;

                list.Add(b);
            }
            return list;
        }

        private static QuarterlyReport ReadLegacyReport(BinaryReader r, byte version)
        {
            QuarterlyReport rp = new QuarterlyReport();
            rp.Quarter = r.ReadInt32(); rp.Rating = (CreditRating)r.ReadInt32(); rp.CreditStatus = r.ReadString();
            rp.DSCR = r.ReadSingle(); rp.DebtBurden = r.ReadSingle(); rp.GrossIncome = r.ReadSingle(); rp.TotalExpenses = r.ReadSingle();
            rp.NOI = r.ReadSingle(); rp.DefaultProbability = r.ReadSingle(); rp.IssuedBonds = r.ReadInt32(); rp.MaxBonds = r.ReadInt32();
            rp.DebtFace = r.ReadSingle(); rp.DebtOwed = r.ReadSingle(); rp.AvgSubscription = r.ReadSingle(); rp.CouponsPaid = r.ReadSingle();
            rp.QuarterDefaults = r.ReadInt32(); rp.TotalDefaults = r.ReadInt32(); rp.BenchmarkRate = r.ReadSingle(); rp.RequiredYield = r.ReadSingle();
            rp.DemandScore = r.ReadSingle(); rp.SmoothedPressure = r.ReadSingle(); rp.AbsorptionCapacity = r.ReadSingle(); rp.Population = r.ReadInt32();
            rp.PortfolioBonds = r.ReadInt32(); rp.SwapCount = r.ReadInt32(); rp.HedgedNotional = r.ReadSingle(); rp.RealizedPL = r.ReadSingle();
            rp.SwapPL = r.ReadSingle(); rp.RevenueVolatility = r.ReadSingle(); rp.Outlook = r.ReadString();
            if (version >= 4)
            {
                rp.Happiness = r.ReadSingle(); rp.EmploymentRate = r.ReadSingle(); rp.PopulationGrowth = r.ReadSingle();
                rp.CitizenConfidence = r.ReadSingle(); rp.BondAppeal = r.ReadSingle(); rp.FinancialHealth = r.ReadSingle();
                rp.CitizenProceeds = r.ReadSingle();
            }
            return rp;
        }

        // ---- invariant validation (spec section 7), applied to the staging object ----

        private static bool ValidateState(BondMarketState s)
        {
            if (s == null) return false;
            if (!ValidateBondList(s.Issued, true)) return false;
            if (!ValidateBondList(s.Portfolio, false)) return false;
            if (!ValidateBondList(s.Market, false)) return false;
            return true;
        }

        private static bool ValidateBondList(List<Bond> bonds, bool issuer)
        {
            for (int i = 0; i < bonds.Count; i++)
            {
                Bond b = bonds[i];
                if (b.Id == null) return false;
                if (b.FaceValue < 0f) return false;
                if (b.RemainingPeriods < 0) return false;                               // I5
                if (!issuer) continue;
                if (b.PlacedFraction < -EPS || b.PlacedFraction > 1f + EPS) return false; // I1
                if (b.OutstandingPrincipal < -EPS) return false;                          // I2
                if (b.OutstandingPrincipal > b.FaceValue + 1f) return false;              // I3
                if (b.OutstandingPrincipal > b.FaceValue * b.PlacedFraction + 1f) return false; // I4
                if (b.State == BondState.Redeemed && b.OutstandingPrincipal > 1f) return false;  // I6
                if (b.Arrears < -EPS) return false;                                       // I8
                if (b.PeriodsInArrears < 0) return false;                                 // I9
            }
            return true;
        }
    }
}
