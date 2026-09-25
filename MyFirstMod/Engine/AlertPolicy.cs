namespace MyFirstMod
{
    // Ordered by priority: when several arise in one month, the first wins.
    public enum AlertKind
    {
        CouponShortfall,
        ShortfallAhead,
        Downgrade,
        FailedAuction,
        BigRateMove
    }

    public sealed class Alert
    {
        public int Sequence;
        public AlertKind Kind;
        public int Period;
        public string Text;
    }

    // WO-38: decides which alert, if any, the player hears about this month.
    // At most one alert per in-game month (the highest-priority candidate), and a
    // condition that persists (a projected shortfall, say) alerts once when it
    // arises, not every month it lasts. Pure; the engine feeds it candidates and
    // closes each period. A persistent condition that loses its month to a
    // higher-priority alert is offered again the next month.
    public sealed class AlertPolicy
    {
        private const int KindCount = 5;

        private readonly bool[] _candidate = new bool[KindCount];
        private readonly string[] _candidateText = new string[KindCount];
        private readonly bool[] _active = new bool[KindCount];  // persistent conditions already delivered
        private readonly bool[] _fromCondition = new bool[KindCount];
        private int _sequence;

        public void Reset()
        {
            for (int i = 0; i < KindCount; i++)
            {
                _candidate[i] = false;
                _candidateText[i] = null;
                _active[i] = false;
                _fromCondition[i] = false;
            }
        }

        // A one-off event (a downgrade, a failed auction, a missed coupon, a big
        // rate move) happened this month.
        public void Raise(AlertKind kind, string text)
        {
            int k = (int)kind;
            if (_candidate[k]) return; // keep the first text of the month
            _candidate[k] = true;
            _candidateText[k] = text;
        }

        // A persistent condition's state this month; it alerts only on the month
        // it becomes true.
        public void Condition(AlertKind kind, bool present, string text)
        {
            int k = (int)kind;
            if (!present) { _active[k] = false; return; }
            if (_active[k]) return;
            Raise(kind, text);
            _fromCondition[k] = true;
        }

        // Close the month: returns the one alert to deliver, or null.
        public Alert EndPeriod(int period)
        {
            Alert chosen = null;
            for (int k = 0; k < KindCount; k++)
            {
                if (!_candidate[k]) continue;
                if (chosen == null)
                {
                    chosen = new Alert();
                    chosen.Sequence = ++_sequence;
                    chosen.Kind = (AlertKind)k;
                    chosen.Period = period;
                    chosen.Text = _candidateText[k];
                    if (_fromCondition[k]) _active[k] = true; // delivered: quiet while it lasts
                }
                _candidate[k] = false;
                _candidateText[k] = null;
                _fromCondition[k] = false;
            }
            return chosen;
        }
    }
}
