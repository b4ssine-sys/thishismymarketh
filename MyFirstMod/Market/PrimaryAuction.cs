using System;

namespace MyFirstMod
{
    // Phase 5 (P1-7): primary issuance as a uniform-price auction. The player offers
    // a yield; the bid-to-cover rises with the concession over fair value and the
    // city's demand score. Below a minimum cover the deal fails (B-3). Pure.
    public struct AuctionResult
    {
        public bool Filled;          // deal proceeded (cover >= minCover)
        public float Cover;          // bid-to-cover ratio
        public float FilledFraction; // fraction of the offered face actually placed [0,1]
        public float ClearingYield;  // uniform clearing yield (= offered yield, B-2)
    }

    public static class PrimaryAuction
    {
        public const float ConcessionScaleBp = 40f; // c0
        public const float CoverCap = 4.0f;
        public const float MinCover = 0.75f;

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        // B-1: cover(c) = clamp( exp(c/c0) * demandScore, 0, coverCap ).
        // c is the concession in bp (offered yield - fair yield).
        public static float BidToCover(float concessionBp, float demandScore)
        {
            float d = Clamp(demandScore, 0f, 1f);
            float cover = (float)Math.Exp(concessionBp / ConcessionScaleBp) * d;
            return Clamp(cover, 0f, CoverCap);
        }

        // Evaluate an offered yield against fair value. Uniform price (B-2): if the
        // book covers, everyone fills at the offered yield; a thin-but-viable book
        // (>= MinCover, < 1) fills partially; below MinCover the deal fails (B-3).
        public static AuctionResult Evaluate(float offeredYield, float fairYield, float demandScore)
        {
            float concessionBp = (offeredYield - fairYield) * 10000f;
            float cover = BidToCover(concessionBp, demandScore);

            AuctionResult r;
            r.Cover = cover;
            r.ClearingYield = offeredYield;
            if (cover < MinCover)
            {
                r.Filled = false;
                r.FilledFraction = 0f;
            }
            else
            {
                r.Filled = true;
                r.FilledFraction = cover >= 1f ? 1f : cover; // partial between MinCover and 1
            }
            return r;
        }

        // Convenience: the estimated cover shown to the player before committing
        // (pre-trade indication).
        public static float EstimateCover(float offeredYield, float fairYield, float demandScore)
        {
            return BidToCover((offeredYield - fairYield) * 10000f, demandScore);
        }
    }
}
