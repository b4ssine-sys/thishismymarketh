using UnityEngine;

namespace MyFirstMod
{
    // WO-39: colour-blind-safe palette (Okabe-Ito) and the shared sprite names.
    // Ratings always print their letters too, so colour is never the only cue.
    public static class Theme
    {
        public static readonly Color32 Text = new Color32(230, 230, 230, 255);
        public static readonly Color32 Muted = new Color32(160, 165, 170, 255);
        public static readonly Color32 Good = new Color32(0, 158, 115, 255);      // bluish green
        public static readonly Color32 Warn = new Color32(230, 159, 0, 255);      // orange (amber)
        public static readonly Color32 Bad = new Color32(213, 94, 0, 255);        // vermillion
        public static readonly Color32 Accent = new Color32(86, 180, 233, 255);   // sky blue
        public static readonly Color32 BarTrack = new Color32(60, 64, 70, 255);
        public static readonly Color32 BarFill = new Color32(86, 180, 233, 255);
        public static readonly Color32 MarkerUp = new Color32(0, 158, 115, 255);
        public static readonly Color32 MarkerDown = new Color32(230, 159, 0, 255);

        public const string PanelSprite = "MenuPanel2";
        public const string CardSprite = "GenericPanel";
        public const string FillSprite = "GenericPanelWhite";
        public const string ButtonSprite = "ButtonMenu";
        public const string ButtonHovered = "ButtonMenuHovered";
        public const string ButtonPressed = "ButtonMenuPressed";
        public const string ButtonFocused = "ButtonMenuFocused";
        public const string ButtonDisabled = "ButtonMenuDisabled";

        public static Color32 Rating(CreditRating r)
        {
            switch (r)
            {
                case CreditRating.AAA:
                case CreditRating.AA:  return new Color32(0, 114, 178, 255);   // blue
                case CreditRating.A:   return new Color32(86, 180, 233, 255);  // sky blue
                case CreditRating.BBB: return new Color32(0, 158, 115, 255);   // bluish green
                case CreditRating.BB:  return new Color32(240, 228, 66, 255);  // yellow
                case CreditRating.B:   return new Color32(230, 159, 0, 255);   // orange
                case CreditRating.CCC: return new Color32(213, 94, 0, 255);    // vermillion
                default:               return new Color32(204, 121, 167, 255); // reddish purple (D)
            }
        }

        public static Color32 Cover(float cover)
        {
            if (cover >= 1f) return Good;
            if (cover >= PrimaryAuction.MinCover) return Warn;
            return Bad;
        }
    }
}
