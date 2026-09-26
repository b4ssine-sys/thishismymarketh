namespace MyFirstMod
{
    // The one definition of an issue's fair and offered yield, used by the engine
    // when it runs an auction and by the issuance ticket when it previews one, so
    // the preview can never disagree with the result.
    public static class AuctionPricing
    {
        public const float MinOfferedYield = 0.001f;

        // The risk-free spot at the issue's tenor, or the benchmark if no curve
        // is available yet. Investors price a city issue off this plus the city's
        // own credit spread (IssueFairYield).
        public static float FairYield(YieldCurve curve, float benchmarkRate, int periods, int periodsPerYear)
        {
            float years = (float)periods / periodsPerYear;
            return curve.Lambda > 0.0001f ? curve.SpotRate(years) : benchmarkRate;
        }

        // WO-41: the same, read from the period's curve table.
        public static float FairYield(CurveTable table, float benchmarkRate, int periods)
        {
            return table != null ? table.Spot(periods) : benchmarkRate;
        }

        // Fair value of a new city issue: what investors demand for this tenor
        // from this city. Risk-free spot at the tenor, plus the city's credit
        // spread (rating spread, fiscal and over-hedge loading, default spike),
        // plus the pledged-revenue adjustment for a revenue bond.
        //
        // Both sides of the auction are measured on the same basis, so the
        // concession is exactly the player's spread. Before this, fair value was
        // the risk-free spot while the offer was the short-end required yield
        // after wealth and demand discounts, so a well-reserved AAA city offered
        // hundreds of bp under "fair" and its auctions failed at 0.00x cover.
        public static float IssueFairYield(CurveTable table, float benchmarkRate, int periods,
            float creditSpread, float revenueAdjustment)
        {
            float y = FairYield(table, benchmarkRate, periods) + creditSpread + revenueAdjustment;
            return y < MinOfferedYield ? MinOfferedYield : y;
        }

        // The yield the city offers: fair value plus the player's own spread
        // (WO-36 ticket). A zero spread is priced exactly at fair value.
        public static float OfferedYield(float issueFairYield, float playerSpread)
        {
            float y = issueFairYield + playerSpread;
            return y < MinOfferedYield ? MinOfferedYield : y;
        }

        // The player spread that makes the book exactly covered (cover = 1).
        // Only demand sets it, because the offer and fair value share one basis.
        // Negative when demand is above neutral: strong demand clears below fair
        // value. Returns float.NaN when no spread can (zero demand).
        public static float SpreadForFullCover(float demandScore)
        {
            if (demandScore <= 0f) return float.NaN;
            float d = demandScore > 1f ? 1f : demandScore;
            return PrimaryAuction.ConcessionScaleBp
                * (float)System.Math.Log(PrimaryAuction.NeutralDemand / d) / 10000f;
        }
    }
}
