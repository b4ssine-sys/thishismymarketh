namespace MyFirstMod
{
    // The city's issuance menu. Pure and static so the UI can preview a deal
    // (WO-36) from the snapshot without asking the engine.
    public static class IssueTemplates
    {
        private static readonly string[] Names = new string[]
        {
            "Emergency Note", "Municipal Note", "Water Revenue Bond",
            "Electric Revenue Bond", "Transit Revenue Bond",
            "Infrastructure Bond", "Capital Bond"
        };
        private static readonly float[] Faces = new float[]
        {
            25000f, 75000f, 150000f, 200000f, 300000f, 400000f, 750000f
        };
        private static readonly int[] Periods = new int[]
        {
            24, 36, 48, 60, 72, 84, 120
        };
        private static readonly RevenueSource[] Revenue = new RevenueSource[]
        {
            RevenueSource.None, RevenueSource.None, RevenueSource.Water,
            RevenueSource.Electricity, RevenueSource.PublicTransport,
            RevenueSource.None, RevenueSource.None
        };

        public const int EmergencyNote = 0;
        public const int PercentIssuePeriods = 60;

        public static int Count { get { return Names.Length; } }

        public static bool IsValid(int index) { return index >= 0 && index < Names.Length; }
        public static string Name(int index) { return Names[index]; }
        public static float Face(int index) { return Faces[index]; }
        public static int TermPeriods(int index) { return Periods[index]; }
        public static RevenueSource Source(int index) { return Revenue[index]; }

        // Revenue templates are quarantined behind the setting until Gate B.
        public static bool IsAvailable(int index, bool revenueBondsEnabled)
        {
            if (!IsValid(index)) return false;
            return Revenue[index] == RevenueSource.None || revenueBondsEnabled;
        }

        public static int AvailableCount(bool revenueBondsEnabled)
        {
            int n = 0;
            for (int i = 0; i < Names.Length; i++)
                if (IsAvailable(i, revenueBondsEnabled)) n++;
            return n;
        }
    }
}
