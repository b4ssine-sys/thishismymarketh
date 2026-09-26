// Headless simulator only (BOND_MARKET_HEADLESS). The game compiles every .cs
// under Source\, so without this guard a full repo copy fails in-game.
#if BOND_MARKET_HEADLESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MyFirstMod.HeadlessSim
{
    // One month of a city's budget: operating income and expense (display units
    // per month) and population.
    public struct TraceMonth
    {
        public float Income;
        public float Expense;
        public int Population;
    }

    public enum SizeClass { Small, Medium, Large }

    // A city's budget over time. Either read from a recorded trace (CSV with
    // columns month,income,expense,population) or generated synthetically.
    public sealed class CityTrace
    {
        public SizeClass Size;
        public string Source;              // "synthetic" or the trace file name
        public float ExpenseRatio;         // spending discipline: < 1 well run, > 1 badly run
        public float StartCash;
        public readonly List<TraceMonth> Months = new List<TraceMonth>();

        public static readonly int[] BasePopulation = { 5000, 50000, 200000 };

        // Synthetic model (placeholder until Gate C records real budgets):
        //   income per resident per month ~ 3 display units, +/- 25% by city;
        //   expense = income x a per-city discipline ratio in [0.88, 1.06];
        //   population grows 0-3% a year; a business-cycle wobble of +/-4% on
        //   income; monthly noise of 3%; a 1-in-120 chance each month of a
        //   six-month shock (income -12%) that tests coverage and reserves.
        public static CityTrace Synthetic(SizeClass size, int seed, int months)
        {
            var r = new Random(seed);
            var t = new CityTrace();
            t.Size = size;
            t.Source = "synthetic";
            t.ExpenseRatio = 0.88f + (float)r.NextDouble() * 0.18f;
            float perCapita = 3f * (0.75f + (float)r.NextDouble() * 0.5f);
            float growth = (float)r.NextDouble() * 0.03f;
            double cyclePhase = r.NextDouble() * Math.PI * 2;
            int pop0 = (int)(BasePopulation[(int)size] * (0.8 + r.NextDouble() * 0.4));
            int shockLeft = 0;
            for (int m = 0; m < months; m++)
            {
                int pop = (int)(pop0 * Math.Pow(1 + growth, m / 12.0));
                double cycle = 1 + 0.04 * Math.Sin(cyclePhase + m * 2 * Math.PI / 96);
                if (shockLeft == 0 && r.NextDouble() < 1.0 / 120) shockLeft = 6;
                double shock = shockLeft > 0 ? 0.88 : 1.0;
                if (shockLeft > 0) shockLeft--;
                double noise = 1 + (r.NextDouble() - 0.5) * 0.06;
                float income = (float)(pop * perCapita * cycle * shock * noise);
                float expense = (float)(pop * perCapita * t.ExpenseRatio * (1 + (r.NextDouble() - 0.5) * 0.04));
                t.Months.Add(new TraceMonth { Income = income, Expense = expense, Population = pop });
            }
            t.StartCash = t.Months[0].Expense * (2f + (float)r.NextDouble() * 4f);
            return t;
        }

        public static CityTrace FromCsv(string path, SizeClass size)
        {
            var t = new CityTrace();
            t.Size = size;
            t.Source = Path.GetFileName(path);
            float incomeSum = 0f, expenseSum = 0f;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || char.IsLetter(line[0])) continue; // header
                string[] c = line.Split(',');
                var m = new TraceMonth
                {
                    Income = float.Parse(c[1], CultureInfo.InvariantCulture),
                    Expense = float.Parse(c[2], CultureInfo.InvariantCulture),
                    Population = int.Parse(c[3], CultureInfo.InvariantCulture)
                };
                incomeSum += m.Income;
                expenseSum += m.Expense;
                t.Months.Add(m);
            }
            t.ExpenseRatio = incomeSum > 0f ? expenseSum / incomeSum : 1f;
            t.StartCash = t.Months.Count > 0 ? t.Months[0].Expense * 3f : 0f;
            return t;
        }

        public static SizeClass Classify(int population)
        {
            if (population < 20000) return SizeClass.Small;
            if (population < 120000) return SizeClass.Medium;
            return SizeClass.Large;
        }
    }
}

#endif
