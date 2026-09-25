namespace MyFirstMod
{
    // The one definition of an issue's fair and offered yield, used by the engine
    // when it runs an auction and by the issuance ticket when it previews one, so
    // the preview can never disagree with the result.
    public static class AuctionPricing
    {
        public const float MinOfferedYield = 0.001f;

        // Fair value investors measure a new issue against: the curve's spot rate
        // at the issue's tenor, or the benchmark if no curve is available yet.
        public static float FairYield(YieldCurve curve, float benchmarkRate, int periods, int periodsPerYear)
        {
            float years = (float)periods / periodsPerYear;
            return curve.Lambda > 0.0001f ? curve.SpotRate(years) : benchmarkRate;
        }

        // The yield the city offers: its required yield, the pledged-revenue
        // adjustment, and the player's own spread (WO-36 ticket).
        public static float OfferedYield(float requiredYield, float revenueAdjustment, float playerSpread)
        {
            float y = requiredYield + revenueAdjustment + playerSpread;
            return y < MinOfferedYield ? MinOfferedYield : y;
        }

        // The player spread that makes the book exactly covered (cover = 1).
        // Returns float.NaN when no spread can (zero demand).
        public static float SpreadForFullCover(float requiredYield, float revenueAdjustment,
            float fairYield, float demandScore)
        {
            if (demandScore <= 0f) return float.NaN;
            float d = demandScore > 1f ? 1f : demandScore;
            float concessionBp = PrimaryAuction.ConcessionScaleBp * (float)System.Math.Log(1.0 / d);
            float offered = fairYield + concessionBp / 10000f;
            return offered - requiredYield - revenueAdjustment;
        }
    }
}
