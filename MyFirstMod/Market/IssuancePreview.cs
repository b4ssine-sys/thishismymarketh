namespace MyFirstMod
{
    public struct IssuanceInputs
    {
        public float Face;
        public int Periods;
        public float OfferedYield;
        public float FairYield;
        public float DemandScore;
        public float RemainingCapacity;
        public float CashBalance;
        public CreditMetrics Metrics;   // the city's metrics before the deal
        public bool HasArrears;
        public int PeriodsPerYear;
    }

    public struct IssuancePreviewResult
    {
        public float Cover;
        public bool Fills;
        public float FilledFraction;
        public float PlacedFace;
        public float Fee;
        public float Proceeds;          // placed face less the underwriting fee
        public float AnnualCoupon;      // on the placed face, at the offered yield
        public bool OverCapacity;
        public CreditRating RatingBefore;
        public CreditRating RatingAfter;
        public CreditMetrics After;
    }

    // WO-36: everything the issuance ticket shows before the player commits:
    // expected bid-to-cover, fill, proceeds after the 75bp fee, the annual coupon
    // cost, and the city's DSCR, burden, reserves and rating after the deal. Pure,
    // and built from the same auction, fee and credit-model functions the engine
    // runs, so what the ticket shows is what the deal does.
    public static class IssuancePreview
    {
        public static IssuancePreviewResult Run(IssuanceInputs x)
        {
            var r = new IssuancePreviewResult();
            r.RatingBefore = RatingEngine.EvaluateRating(x.Metrics, x.HasArrears);
            r.OverCapacity = x.Face > x.RemainingCapacity;

            AuctionResult ar = PrimaryAuction.Evaluate(x.OfferedYield, x.FairYield, x.DemandScore);
            r.Cover = ar.Cover;
            r.Fills = ar.Filled && !r.OverCapacity;
            r.FilledFraction = r.Fills ? ar.FilledFraction : 0f;
            r.PlacedFace = x.Face * r.FilledFraction;
            r.Fee = Friction.UnderwritingFee(r.PlacedFace);
            r.Proceeds = r.PlacedFace - r.Fee;
            if (r.Proceeds < 0f) r.Proceeds = 0f;
            r.AnnualCoupon = r.PlacedFace * x.OfferedYield;

            r.After = CreditModel.FromAnnual(
                x.Metrics.AnnualOperatingRevenue,
                x.Metrics.AnnualOperatingExpense,
                x.Metrics.AnnualDebtService + r.AnnualCoupon,
                x.CashBalance + r.Proceeds,
                x.PeriodsPerYear);
            r.RatingAfter = RatingEngine.EvaluateRating(r.After, x.HasArrears);
            return r;
        }
    }
}
