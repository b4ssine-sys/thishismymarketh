namespace MyFirstMod
{
    // WO-41: the yield curve evaluated once per period into monthly tables, so
    // every bond valuation, swap annuity and auction fair-yield lookup between
    // rebuilds is an array read instead of repeated Math.Exp calls. Immutable once
    // built (a snapshot may hold it), and computed in exactly the order the direct
    // evaluation uses, so results are identical, not merely close.
    public sealed class CurveTable
    {
        public const int MaxMonths = 120;
        private const int MonthsPerYear = 12;

        public readonly YieldCurve Curve;
        private readonly float[] _spot = new float[MaxMonths + 1];
        private readonly float[] _df = new float[MaxMonths + 1];
        private readonly float[] _annuitySum = new float[MaxMonths + 1]; // sum of DF(1..m)

        private CurveTable(YieldCurve curve)
        {
            Curve = curve;
            float sum = 0f;
            for (int m = 0; m <= MaxMonths; m++)
            {
                float years = (float)m / MonthsPerYear;
                _spot[m] = curve.SpotRate(years);
                _df[m] = curve.DiscountFactor(years);
                if (m > 0) sum += _df[m];
                _annuitySum[m] = sum;
            }
        }

        public static CurveTable Build(YieldCurve curve) { return new CurveTable(curve); }

        public float Spot(int months)
        {
            if (months <= 0) return _spot[0];
            return months <= MaxMonths ? _spot[months] : Curve.SpotRate((float)months / MonthsPerYear);
        }

        public float DiscountFactor(int months)
        {
            if (months <= 0) return 1f;
            return months <= MaxMonths ? _df[months] : Curve.DiscountFactor((float)months / MonthsPerYear);
        }

        // Sum of DF(k) for k = 1..months, divided by 12: the annuity of a monthly swap.
        public float Annuity(int months)
        {
            if (months <= 0) return 0f;
            if (months <= MaxMonths) return _annuitySum[months] / MonthsPerYear;
            float a = _annuitySum[MaxMonths];
            for (int k = MaxMonths + 1; k <= months; k++) a += DiscountFactor(k);
            return a / MonthsPerYear;
        }
    }
}
