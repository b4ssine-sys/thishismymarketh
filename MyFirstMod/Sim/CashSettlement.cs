namespace MyFirstMod
{
    // What OnUpdateMoneyAmount must return so that the cash the engine moved
    // during the callback survives, whether the game assigns the return value to
    // its treasury or ignores it (see TreasuryProbe). Pure so it is pinned by tests.
    public static class CashSettlement
    {
        // handed: the amount the game passed into the callback.
        // liveBefore/liveAfter: the treasury field read before and after the
        // engine's moves; liveOk is false when the field could not be read.
        public static long ReturnValue(long handed, bool liveOk, long liveBefore, long liveAfter)
        {
            if (!liveOk) return handed; // unbound: unchanged behaviour
            return handed + (liveAfter - liveBefore);
        }
    }
}
