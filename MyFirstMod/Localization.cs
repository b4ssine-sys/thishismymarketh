namespace MyFirstMod
{
    public static class Loc
    {
        public static string Get(string key)
        {
            return Get(key, "en");
        }

        public static string Get(string key, string locale)
        {
            switch (key)
            {
                case "panel.title": return "Municipal Bond Market";
                case "tab.market": return "Market";
                case "tab.portfolio": return "Portfolio";
                case "tab.debt": return "Debt";
                case "tab.hedging": return "Hedging";
                case "tab.positions": return "Positions";
                case "tab.activity": return "Activity";
                case "tab.report": return "Report";
                case "tab.settings": return "Settings";

                case "label.buy": return "Buy";
                case "label.sell": return "Sell";
                case "label.repay": return "Repay";
                case "label.issue": return "Issue";
                case "label.exit": return "Exit";
                case "label.cycle": return "Cycle";
                case "label.toggle": return "Toggle";
                case "label.view": return "View";
                case "label.sellall": return "Sell All";
                case "label.buy1m": return "10x 1M 5yr";
                case "label.buy10m": return "10x 10M 5yr";
                case "label.buy1b": return "Buy 1B 5yr";
                case "label.pay50": return "Pay 50%";
                case "label.pay25": return "Pay 25%";
                case "label.issue50": return "Issue 50%";
                case "label.issue25": return "Issue 25%";
                case "label.autohedge": return "Auto-Hedge";
                case "label.sell25": return "Sell 25%";
                case "label.sell50": return "Sell 50%";
                case "label.exitall": return "Exit All";
                case "label.latest": return "Latest";
                case "label.history": return "History";
                case "label.payfixed": return "Pay Fixed";
                case "label.rcvfixed": return "Rcv Fixed";
                case "label.close": return "X";

                case "state.active": return "ACTIVE";
                case "state.delinquent": return "DELINQUENT";
                case "state.defaulted": return "DEFAULTED";
                case "state.redeemed": return "REDEEMED";
                case "state.pending": return "PENDING";
                case "state.low": return "LOW";
                case "state.owe": return "OWE";

                case "status.notready": return "Bond market engine not ready...";
                case "status.loading": return "Loading financial data...";

                case "hint.scroll": return "scroll to see more";
                case "hint.nopositions": return "No open positions";
                case "hint.noactivity": return "No citizen trading activity yet - issue bonds first";
                case "hint.noreports": return "No quarterly reports yet. First report generates after 3 periods.";
                case "hint.awaitreport": return "Awaiting first quarterly report...";
                case "hint.ratingd": return "RATING D - BOND MARKET ACCESS DENIED";
                case "hint.nodemand": return "NO DEMAND - CITIZENS UNWILLING TO BUY BONDS";
                case "hint.maxslots": return "MAX SLOTS - REPAY EXISTING DEBT FIRST";
                case "hint.saturated": return "MARKET SATURATED - CAPACITY FULL, WAIT FOR CITY GROWTH";
                case "hint.cannotissue": return "CANNOT ISSUE - CHECK RATING AND DEMAND";
                case "hint.weak": return "WEAK";

                case "pressure.buy": return "BUY";
                case "pressure.sell": return "SELL";
                case "pressure.even": return "EVEN";

                case "revenue.go": return "GO";
                case "revenue.water": return "Water";
                case "revenue.electric": return "Electric";
                case "revenue.transit": return "Transit";

                case "settings.hazard": return "Default Hazard Multiplier";
                case "settings.hazard.desc": return "controls issuer default frequency";
                case "settings.hazard.historical": return "Historical (x1)";
                case "settings.hazard.standard": return "Standard (x25)";
                case "settings.hazard.volatile": return "Volatile (x60)";
                case "settings.ratevol": return "Rate Volatility";
                case "settings.ratevol.desc": return "controls interest rate movement intensity";
                case "settings.ratevol.calm": return "Calm (x0.5)";
                case "settings.ratevol.normal": return "Normal (x1.0)";
                case "settings.ratevol.turbulent": return "Turbulent (x2.0)";
                case "settings.trading": return "Citizen Bond Trading";
                case "settings.trading.desc": return "enables/disables citizen market activity";
                case "settings.trading.enabled": return "Enabled";
                case "settings.trading.disabled": return "Disabled";
                case "settings.shortcut": return "Keyboard shortcut: Shift+B to toggle panel visibility";
                case "settings.footer": return "Settings are saved with your city and persist across sessions.";

                default: return key;
            }
        }
    }
}
