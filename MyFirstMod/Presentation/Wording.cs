using System;

namespace MyFirstMod
{
    // Plain-language labels for figures on the new screens. Pure.
    public static class Wording
    {
        // Cash runway from the treasury and the monthly operating flow.
        public static string Runway(float cash, float netPerMonth)
        {
            if (netPerMonth >= 0f)
                return string.Format("Growing about {0:N0} a month", netPerMonth);
            if (cash <= 0f)
                return string.Format("Empty, losing {0:N0} a month", -netPerMonth);
            float months = cash / -netPerMonth;
            return string.Format("About {0:F0} month(s) at -{1:N0} a month", months, -netPerMonth);
        }

        // Where the rate cycle is: the long-run mean follows base + amp*sin(phase),
        // so cos(phase) gives its direction.
        public static string RateCycle(float phase)
        {
            double c = Math.Cos(phase);
            double sn = Math.Sin(phase);
            if (c > 0.38) return "Rates trending up";
            if (c < -0.38) return "Rates trending down";
            return sn > 0 ? "Rates near a peak" : "Rates near a trough";
        }

        // Curve shape from the long-minus-short spot spread.
        public static string CurveShape(float shortSpot, float longSpot)
        {
            float slope = longSpot - shortSpot;
            if (slope > 0.010f) return "Steep curve";
            if (slope > 0.0025f) return "Normal curve";
            if (slope > -0.001f) return "Flat curve";
            return "Inverted curve";
        }

        // Hedge ratio in the rate-exposure gauge.
        public static string HedgeStatus(float debtFace, float hedged)
        {
            if (debtFace <= 0f)
                return hedged > 0f ? "Swaps with no debt to hedge" : "No debt to hedge";
            float ratio = hedged / debtFace;
            if (ratio > 1.0001f) return string.Format("Over-hedged: {0:F0}% of debt", ratio * 100f);
            return string.Format("{0:F0}% of {1:N0} debt hedged", ratio * 100f, debtFace);
        }

        public static string SignedMoney(float v)
        {
            return v >= 0f ? "+" + v.ToString("N0") : v.ToString("N0");
        }
    }

    // The activity strip along the bottom of the window: the latest alert, the
    // latest citizen-market activity and the latest order outcome. Pure.
    public static class FeedModel
    {
        public static string[] Lines(EngineSnapshot s)
        {
            string alert = null, market = null, order = null;
            if (s.Alerts.Length > 0)
            {
                Alert a = s.Alerts[s.Alerts.Length - 1];
                alert = string.Format("Month {0}: {1}", a.Period, a.Text);
            }
            if (s.Transactions.Length > 0)
            {
                CimTransaction t = s.Transactions[s.Transactions.Length - 1];
                market = string.Format("Citizens bought {0:N0}, sold {1:N0}. {2}", t.BuyVolume, t.SellVolume, t.Detail);
            }
            if (s.RecentResults.Length > 0)
            {
                CommandResult r = s.RecentResults[s.RecentResults.Length - 1];
                if (!string.IsNullOrEmpty(r.Message))
                    order = (r.Success ? "Done: " : "Not done: ") + r.Message;
            }

            int n = (alert != null ? 1 : 0) + (market != null ? 1 : 0) + (order != null ? 1 : 0);
            string[] lines = new string[n];
            int i = 0;
            if (alert != null) lines[i++] = alert;
            if (order != null) lines[i++] = order;
            if (market != null) lines[i++] = market;
            return lines;
        }
    }
}
