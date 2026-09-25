namespace MyFirstMod
{
    // WO-40: every player action is an order placed on the engine's queue and
    // carried out by the simulation thread at the start of its next tick.
    public enum CommandKind
    {
        BuyBond,
        SellBond,
        SellAllBonds,
        IssueBond,
        IssueBondPercent,
        PayDebtPercent,
        RepayBond,
        BuyBulk1B,
        BuyBulk10x1M,
        BuyBulk10x10M,
        EnterSwap,
        TerminateSwap,
        TerminateAllSwaps,
        SellSwapTranche,
        SellAllSwapsTranche,
        AutoHedge,
        SetHazardMultiplier,
        SetRateVolatility,
        SetCitizenTrading,
        SetRevenueBonds,
        AckCreditNotice
    }

    public sealed class EngineCommand
    {
        public int Sequence;     // assigned by BondMarketEngine.Submit
        public CommandKind Kind;
        public string Id;        // bond / swap id
        public int Index;        // template index, periods
        public float Value;      // percent, fraction, notional, setting value
        public float Value2;     // fixed rate, offered-yield spread
        public bool Flag;        // pay-fixed, toggle value

        private static EngineCommand Make(CommandKind kind)
        {
            EngineCommand c = new EngineCommand();
            c.Kind = kind;
            return c;
        }

        public static EngineCommand BuyBond(string id) { EngineCommand c = Make(CommandKind.BuyBond); c.Id = id; return c; }
        public static EngineCommand SellBond(string id) { EngineCommand c = Make(CommandKind.SellBond); c.Id = id; return c; }
        public static EngineCommand SellAllBonds() { return Make(CommandKind.SellAllBonds); }
        public static EngineCommand RepayBond(string id) { EngineCommand c = Make(CommandKind.RepayBond); c.Id = id; return c; }
        public static EngineCommand PayDebtPercent(float percent) { EngineCommand c = Make(CommandKind.PayDebtPercent); c.Value = percent; return c; }
        public static EngineCommand IssueBondPercent(float percent) { EngineCommand c = Make(CommandKind.IssueBondPercent); c.Value = percent; return c; }

        // yieldSpread: offered yield minus the engine's required yield (WO-36
        // ticket slider). Zero offers at the required yield.
        public static EngineCommand IssueBond(int templateIndex, float yieldSpread)
        {
            EngineCommand c = Make(CommandKind.IssueBond);
            c.Index = templateIndex;
            c.Value2 = yieldSpread;
            return c;
        }

        public static EngineCommand BuyBulk1B() { return Make(CommandKind.BuyBulk1B); }
        public static EngineCommand BuyBulk10x1M() { return Make(CommandKind.BuyBulk10x1M); }
        public static EngineCommand BuyBulk10x10M() { return Make(CommandKind.BuyBulk10x10M); }

        public static EngineCommand EnterSwap(float notional, float fixedRate, int periods, bool payFixed)
        {
            EngineCommand c = Make(CommandKind.EnterSwap);
            c.Value = notional;
            c.Value2 = fixedRate;
            c.Index = periods;
            c.Flag = payFixed;
            return c;
        }

        public static EngineCommand TerminateSwap(string id) { EngineCommand c = Make(CommandKind.TerminateSwap); c.Id = id; return c; }
        public static EngineCommand TerminateAllSwaps() { return Make(CommandKind.TerminateAllSwaps); }
        public static EngineCommand SellSwapTranche(string id, float fraction) { EngineCommand c = Make(CommandKind.SellSwapTranche); c.Id = id; c.Value = fraction; return c; }
        public static EngineCommand SellAllSwapsTranche(float fraction) { EngineCommand c = Make(CommandKind.SellAllSwapsTranche); c.Value = fraction; return c; }
        public static EngineCommand AutoHedge() { return Make(CommandKind.AutoHedge); }

        public static EngineCommand SetHazardMultiplier(float value) { EngineCommand c = Make(CommandKind.SetHazardMultiplier); c.Value = value; return c; }
        public static EngineCommand SetRateVolatility(float value) { EngineCommand c = Make(CommandKind.SetRateVolatility); c.Value = value; return c; }
        public static EngineCommand SetCitizenTrading(bool on) { EngineCommand c = Make(CommandKind.SetCitizenTrading); c.Flag = on; return c; }
        public static EngineCommand SetRevenueBonds(bool on) { EngineCommand c = Make(CommandKind.SetRevenueBonds); c.Flag = on; return c; }
        public static EngineCommand AckCreditNotice() { return Make(CommandKind.AckCreditNotice); }
    }

    // The outcome of one command, published in the snapshot so the UI can report
    // it. Count is how many instruments were affected; Amount is the cash moved
    // (display units, signed from the city's point of view).
    public sealed class CommandResult
    {
        public int Sequence;
        public CommandKind Kind;
        public bool Success;
        public int Count;
        public float Amount;
        public float Detail;   // e.g. auction cover or filled fraction
        public string Message;
    }
}
