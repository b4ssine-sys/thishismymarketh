namespace MyFirstMod
{
    // WO-34: the one-line startup self-check. Pure so the format is pinned by a
    // test; the game-facing values are gathered in SelfCheck.
    public static class StartupReport
    {
        public const string Prefix = "[MyFirstMod] SELF-CHECK";

        public static string Format(string modVersion, int saveFormat, bool ledgerBound,
            string ledgerShape, string gameBuild, string loadMode, bool uiCreated)
        {
            return string.Format(
                "{0} mod={1} save=v{2} ledger={3} ({4}) game={5} mode={6} ui={7}",
                Prefix,
                string.IsNullOrEmpty(modVersion) ? "?" : modVersion,
                saveFormat,
                ledgerBound ? "BOUND" : "FALLBACK",
                string.IsNullOrEmpty(ledgerShape) ? "unbound" : ledgerShape,
                string.IsNullOrEmpty(gameBuild) ? "unknown" : gameBuild,
                string.IsNullOrEmpty(loadMode) ? "?" : loadMode,
                uiCreated ? "OK" : "FAILED");
        }
    }
}
