// Headless simulator only (BOND_MARKET_HEADLESS). The game compiles every .cs
// under Source\, so without this guard a full repo copy fails in-game.
#if BOND_MARKET_HEADLESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MyFirstMod.EngineTests;

namespace MyFirstMod.HeadlessSim
{
    // WO-45 first cut. Runs seeded synthetic cities (or recorded traces) through
    // the real engine under each scripted strategy and writes per-run rows plus
    // a summary. Statistics beyond the summary table are still to do; see
    // HANDOFF.md.
    public static class Program
    {
        private sealed class RunResult
        {
            public int City;
            public SizeClass Size;
            public Strategy Strategy;
            public CreditRating FinalRating;
            public CreditRating WorstRating;
            public bool Defaulted;
            public float MinReserves;
            public int Issues;
        }

        public static int Main(string[] args)
        {
            int cities = IntArg(args, "-cities", 30);
            int years = IntArg(args, "-years", 20);
            string outDir = StrArg(args, "-out", "calibration");
            string traceDir = StrArg(args, "-traces", null);
            Directory.CreateDirectory(outDir);

            var traces = new List<CityTrace>();
            if (traceDir != null)
            {
                foreach (string f in Directory.GetFiles(traceDir, "*.csv"))
                    traces.Add(CityTrace.FromCsv(f, SizeClass.Medium));
            }
            else
            {
                for (int c = 0; c < cities; c++)
                    traces.Add(CityTrace.Synthetic((SizeClass)(c % 3), 1000 + c, years * 12));
            }

            var results = new List<RunResult>();
            for (int c = 0; c < traces.Count; c++)
            {
                foreach (Strategy s in Enum.GetValues(typeof(Strategy)))
                {
                    try { results.Add(Run(c, traces[c], s)); }
                    catch (Exception e) { Console.Error.WriteLine("city " + c + " " + s + ": " + e.Message); }
                }
            }

            WriteRows(Path.Combine(outDir, "runs.csv"), results);
            string summary = Summary(results, traces.Count, years);
            File.WriteAllText(Path.Combine(outDir, "summary.md"), summary);
            Console.Write(summary);
            return 0;
        }

        private static RunResult Run(int index, CityTrace trace, Strategy strategy)
        {
            TraceMonth first = trace.Months[0];
            var h = new GameHarness(startCashDisplay: (long)trace.StartCash, population: first.Population,
                incomePerMonthDisplay: (long)first.Income, expensePerMonthDisplay: (long)first.Expense,
                seed: 7000 + index);
            h.Boot();

            var r = new RunResult { City = index, Size = trace.Size, Strategy = strategy, MinReserves = float.MaxValue };
            for (int m = 0; m < trace.Months.Count; m++)
            {
                TraceMonth tm = trace.Months[m];
                h.Districts.m_districts.m_buffer[0].m_populationData.m_finalCount = (uint)tm.Population;
                h.IncomePerTick = (long)tm.Income * 100 / h.TicksPerMonth;
                h.ExpensePerTick = (long)tm.Expense * 100 / h.TicksPerMonth;

                EngineSnapshot s = h.Snap;
                int issuedBefore = s.Issued.Length;
                Player.Act(strategy, h.Engine, s);
                h.Months(1);

                s = h.Snap;
                if (s.Issued.Length > issuedBefore) r.Issues++;
                if (s.Rating > r.WorstRating) r.WorstRating = s.Rating;
                if (s.MonthsOfReserves < r.MinReserves) r.MinReserves = s.MonthsOfReserves;
                for (int i = 0; i < s.Issued.Length; i++)
                    if (s.Issued[i].InDefault) r.Defaulted = true;
                if (s.Rating == CreditRating.D) r.Defaulted = true;
            }
            r.FinalRating = h.Snap.Rating;
            return r;
        }

        private static void WriteRows(string path, List<RunResult> results)
        {
            var sb = new StringBuilder("city,size,strategy,final_rating,worst_rating,defaulted,min_reserves_months,issues\n");
            foreach (RunResult r in results)
                sb.Append(r.City).Append(',').Append(r.Size).Append(',').Append(r.Strategy).Append(',')
                  .Append(r.FinalRating).Append(',').Append(r.WorstRating).Append(',')
                  .Append(r.Defaulted ? 1 : 0).Append(',')
                  .Append(r.MinReserves.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.Issues).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }

        private static string Summary(List<RunResult> results, int cities, int years)
        {
            var sb = new StringBuilder();
            sb.Append("# Headless calibration run\n\n");
            sb.Append(cities).Append(" cities x ").Append(years).Append(" years x 3 strategies (synthetic unless -traces).\n\n");
            sb.Append("| Strategy | Runs | Default rate | Median final rating | Mean issues |\n|---|---|---|---|---|\n");
            foreach (Strategy s in Enum.GetValues(typeof(Strategy)))
            {
                var ratings = new List<int>();
                int n = 0, defaults = 0, issues = 0;
                foreach (RunResult r in results)
                {
                    if (r.Strategy != s) continue;
                    n++; issues += r.Issues;
                    if (r.Defaulted) defaults++;
                    ratings.Add((int)r.FinalRating);
                }
                if (n == 0) continue;
                ratings.Sort();
                sb.Append("| ").Append(s).Append(" | ").Append(n).Append(" | ")
                  .Append((100.0 * defaults / n).ToString("0.0", CultureInfo.InvariantCulture)).Append("% | ")
                  .Append((CreditRating)ratings[n / 2]).Append(" | ")
                  .Append(((double)issues / n).ToString("0.0", CultureInfo.InvariantCulture)).Append(" |\n");
            }
            return sb.ToString();
        }

        private static int IntArg(string[] args, string name, int fallback)
        {
            string v = StrArg(args, name, null);
            return v == null ? fallback : int.Parse(v, CultureInfo.InvariantCulture);
        }

        private static string StrArg(string[] args, string name, string fallback)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }
    }
}

#endif
