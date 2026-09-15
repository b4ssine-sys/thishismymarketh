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
                case "settings.hazard": return "Default Hazard Multiplier";
                case "settings.hazard.desc": return "Controls issuer default frequency";
                case "settings.hazard.historical": return "Historical (x1)";
                case "settings.hazard.standard": return "Standard (x25)";
                case "settings.hazard.volatile": return "Volatile (x60)";
                case "settings.ratevol": return "Rate Volatility";
                case "settings.ratevol.desc": return "Controls interest rate movement intensity";
                case "settings.ratevol.calm": return "Calm (x0.5)";
                case "settings.ratevol.normal": return "Normal (x1.0)";
                case "settings.ratevol.turbulent": return "Turbulent (x2.0)";
                case "settings.trading": return "Citizen Bond Trading";
                case "settings.trading.desc": return "Enables/disables citizen market activity";
                case "settings.trading.enabled": return "Enabled";
                case "settings.trading.disabled": return "Disabled";
                case "settings.shortcut": return "Keyboard shortcut: Shift+B to toggle panel visibility";
                case "settings.footer": return "Settings are saved with your city and persist across sessions.";
                case "state.active": return "ACTIVE";
                case "state.delinquent": return "DELINQUENT";
                case "state.defaulted": return "DEFAULTED";
                case "state.redeemed": return "REDEEMED";
                case "state.pending": return "PENDING";
                case "label.buy": return "Buy";
                case "label.sell": return "Sell";
                case "label.repay": return "Repay";
                case "label.issue": return "Issue";
                case "label.exit": return "Exit";
                case "label.cycle": return "Cycle";
                case "label.toggle": return "Toggle";
                default: return key;
            }
        }
    }
}
