// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    // The engine's cash moves inside OnUpdateMoneyAmount must survive whatever the
    // game does with the return value. Each test models one game behaviour: the
    // treasury the game ends up with must equal handed + what the engine moved.
    public class CashSettlementTests
    {
        private const long Handed = 1_000_000;

        // Game assigns the return value; Fetch/Add hit the treasury immediately.
        [Fact]
        public void Assigned_Immediate_KeepsMovesExactlyOnce()
        {
            long treasury = Handed;
            long before = treasury;
            treasury += 25_000;   // coupon income added during the callback
            treasury -= 40_000;   // debt service fetched during the callback
            long ret = CashSettlement.ReturnValue(Handed, true, before, treasury);
            treasury = ret;       // the game assigns
            Assert.Equal(Handed + 25_000 - 40_000, treasury);
        }

        // Game ignores the return value: whatever we return, the treasury already
        // holds the moves once.
        [Fact]
        public void Ignored_Immediate_ReturnMatchesTreasury()
        {
            long treasury = Handed;
            long before = treasury;
            treasury -= 40_000;
            long ret = CashSettlement.ReturnValue(Handed, true, before, treasury);
            Assert.Equal(treasury, ret);
        }

        // Game applies Fetch/Add later (deferred): the live field does not move
        // during the callback, so returning it unchanged avoids double counting.
        [Fact]
        public void Deferred_Moves_ReturnHandedUnchanged()
        {
            long ret = CashSettlement.ReturnValue(Handed, true, Handed, Handed);
            Assert.Equal(Handed, ret);
        }

        // Another extension earlier in the chain changed the amount before us:
        // its change is preserved and ours is added on top.
        [Fact]
        public void EarlierExtensionAdjustment_IsPreserved()
        {
            long handedByChain = Handed + 5_000; // not yet written to the field
            long before = Handed;
            long after = Handed - 40_000;
            Assert.Equal(handedByChain - 40_000, CashSettlement.ReturnValue(handedByChain, true, before, after));
        }

        // Field unbound: behave exactly as before this fix.
        [Fact]
        public void Unbound_ReturnsHanded()
        {
            Assert.Equal(Handed, CashSettlement.ReturnValue(Handed, false, 0, 0));
        }
    }
}

#endif
