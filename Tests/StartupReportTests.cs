// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    // WO-34: the self-check line carries everything a "UI missing" report needs.
    public class StartupReportTests
    {
        [Fact]
        public void Line_CarriesEveryDiagnosticField()
        {
            string line = StartupReport.Format("1.0.0", 12, true,
                "(Service,out,out)", "1.21.1-f9", "LoadGame", true, "live-field");

            Assert.StartsWith(StartupReport.Prefix, line);
            Assert.Contains("mod=1.0.0", line);
            Assert.Contains("save=v12", line);
            Assert.Contains("ledger=BOUND", line);
            Assert.Contains("game=1.21.1-f9", line);
            Assert.Contains("mode=LoadGame", line);
            Assert.Contains("ui=OK", line);
            Assert.Contains("cash=live-field", line);
            Assert.DoesNotContain("\n", line);
        }

        [Fact]
        public void Line_FlagsFallbackAndUiFailure()
        {
            string line = StartupReport.Format("1.0.0", 12, false, null, null, "NewGame", false, "unbound");

            Assert.Contains("ledger=FALLBACK (unbound)", line);
            Assert.Contains("game=unknown", line);
            Assert.Contains("ui=FAILED", line);
            Assert.Contains("cash=unbound", line);
        }
    }
}

#endif
