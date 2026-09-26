using System.Diagnostics;

namespace MyFirstMod
{
    // WO-44: the engine's own timing, recorded on every tick at the cost of two
    // timestamp reads. Ordinary ticks and period-boundary ticks (which run the
    // once-per-period metrics block) are kept apart, because they carry different
    // budgets: under 50 microseconds and under 1 ms.
    public sealed class EnginePerf
    {
        public const double TickBudgetMicros = 50.0;
        public const double PeriodBudgetMicros = 1000.0;

        private static readonly double MicrosPerTimestamp = 1000000.0 / Stopwatch.Frequency;

        public long Ticks;
        public double TickMicrosTotal;
        public double TickMicrosMax;
        public long Periods;
        public double PeriodMicrosTotal;
        public double PeriodMicrosMax;

        public double TickMicrosAverage { get { return Ticks > 0 ? TickMicrosTotal / Ticks : 0.0; } }
        public double PeriodMicrosAverage { get { return Periods > 0 ? PeriodMicrosTotal / Periods : 0.0; } }

        public static long Now() { return Stopwatch.GetTimestamp(); }

        public void Record(long start, long end, bool periodBoundary)
        {
            double micros = (end - start) * MicrosPerTimestamp;
            if (periodBoundary)
            {
                Periods++;
                PeriodMicrosTotal += micros;
                if (micros > PeriodMicrosMax) PeriodMicrosMax = micros;
            }
            else
            {
                Ticks++;
                TickMicrosTotal += micros;
                if (micros > TickMicrosMax) TickMicrosMax = micros;
            }
        }

        public void Reset()
        {
            Ticks = 0; TickMicrosTotal = 0; TickMicrosMax = 0;
            Periods = 0; PeriodMicrosTotal = 0; PeriodMicrosMax = 0;
        }

        public string Line()
        {
            return string.Format("[MyFirstMod] PERF tick avg {0:F1}us max {1:F1}us (budget {2:F0}us) | period avg {3:F0}us max {4:F0}us (budget {5:F0}us) | {6} ticks, {7} periods",
                TickMicrosAverage, TickMicrosMax, TickBudgetMicros, PeriodMicrosAverage, PeriodMicrosMax, PeriodBudgetMicros, Ticks, Periods);
        }
    }
}
