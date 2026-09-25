using System.Text;
using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // WO-42: a label that reformats only when its value changes. The comparison
    // is on the raw value, so an unchanged figure costs a compare and nothing else;
    // formatting goes through one shared StringBuilder.
    public sealed class BoundLabel
    {
        private static readonly StringBuilder Sb = new StringBuilder(128);

        // Label text writes since start (WO-42 diagnostic: idle means zero).
        public static int Writes;

        public readonly UILabel Label;
        private float _lastValue = float.NaN;
        private string _lastText;
        private int _lastStyle = -1;

        public BoundLabel(UILabel label) { Label = label; }

        public void Text(string text)
        {
            if (ReferenceEquals(text, _lastText) || text == _lastText) return;
            _lastText = text;
            _lastValue = float.NaN;
            Label.text = text ?? "";
            Writes++;
        }

        // prefix + value (decimals, thousands separators) + suffix.
        public void Number(string prefix, float value, int decimals, string suffix)
        {
            if (value == _lastValue && _lastText == null) return;
            _lastValue = value;
            _lastText = null;
            Sb.Length = 0;
            if (prefix != null) Sb.Append(prefix);
            Sb.Append(value.ToString(decimals == 0 ? "N0" : "N" + decimals));
            if (suffix != null) Sb.Append(suffix);
            Label.text = Sb.ToString();
            Writes++;
        }

        public void Percent(string prefix, float fraction, int decimals)
        {
            Number(prefix, fraction * 100f, decimals, "%");
        }

        public void Color(Color32 c, int styleKey)
        {
            if (styleKey == _lastStyle) return;
            _lastStyle = styleKey;
            Label.textColor = c;
        }

        public void Visible(bool visible)
        {
            if (Label.isVisible != visible) Label.isVisible = visible;
        }
    }

    // A horizontal bar with a fill and up to two threshold markers (WO-35 factor
    // bars, WO-37 ladder, the Risk gauge).
    public sealed class Bar
    {
        public readonly UIPanel Track;
        private readonly UIPanel _fill;
        private readonly UIPanel _markerA;
        private readonly UIPanel _markerB;
        private readonly float _width;
        private float _lastFill = -1f, _lastA = -2f, _lastB = -2f;
        private Color32 _lastColor;
        private bool _colorSet;

        public Bar(UIComponent parent, float x, float y, float width, float height, string tooltip)
        {
            _width = width;
            Track = Widgets.Box(parent, x, y, width, height, Theme.BarTrack, tooltip);
            _fill = Widgets.Box(Track, 0f, 0f, 0f, height, Theme.BarFill, tooltip);
            _markerA = Widgets.Box(Track, 0f, -2f, 2f, height + 4f, Theme.MarkerUp, null);
            _markerB = Widgets.Box(Track, 0f, -2f, 2f, height + 4f, Theme.MarkerDown, null);
            _markerA.isVisible = false;
            _markerB.isVisible = false;
        }

        // fraction in [0,1]; markers are fractions or negative to hide.
        public void Set(float fraction, Color32 fillColor, float markerUp, float markerDown)
        {
            fraction = Mathf01(fraction);
            if (fraction != _lastFill)
            {
                _lastFill = fraction;
                _fill.width = _width * fraction;
            }
            if (!_colorSet || !Same(fillColor, _lastColor))
            {
                _colorSet = true;
                _lastColor = fillColor;
                _fill.color = fillColor;
            }
            Place(_markerA, markerUp, ref _lastA);
            Place(_markerB, markerDown, ref _lastB);
        }

        private void Place(UIPanel marker, float at, ref float last)
        {
            if (at == last) return;
            last = at;
            marker.isVisible = at >= 0f;
            if (at >= 0f) marker.relativePosition = new Vector3(_width * Mathf01(at) - 1f, -2f);
        }

        private static float Mathf01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        public static bool Same(Color32 a, Color32 b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }
    }

    public static class Widgets
    {
        public static float Scale { get { return UiPrefs.TextScale; } }

        public static UIPanel Box(UIComponent parent, float x, float y, float w, float h, Color32 color, string tooltip)
        {
            UIPanel p = parent.AddUIComponent<UIPanel>();
            p.backgroundSprite = Theme.FillSprite;
            p.color = color;
            p.size = new Vector2(w, h);
            p.relativePosition = new Vector3(x, y);
            if (tooltip != null) p.tooltip = tooltip;
            return p;
        }

        public static UIPanel Card(UIComponent parent, float x, float y, float w, float h)
        {
            UIPanel p = parent.AddUIComponent<UIPanel>();
            p.backgroundSprite = Theme.CardSprite;
            p.size = new Vector2(w, h);
            p.relativePosition = new Vector3(x, y);
            return p;
        }

        public static UILabel Label(UIComponent parent, float x, float y, float w, float h,
            float textScale, string text, string tooltip)
        {
            UILabel l = parent.AddUIComponent<UILabel>();
            l.autoSize = false;
            l.wordWrap = true;
            l.size = new Vector2(w, h);
            l.relativePosition = new Vector3(x, y);
            l.textScale = textScale * Scale;
            l.textColor = Theme.Text;
            l.verticalAlignment = UIVerticalAlignment.Middle;
            l.text = text ?? "";
            if (tooltip != null) l.tooltip = tooltip;
            return l;
        }

        public static BoundLabel Figure(UIComponent parent, float x, float y, float w, float h,
            float textScale, string tooltip)
        {
            return new BoundLabel(Label(parent, x, y, w, h, textScale, "", tooltip));
        }

        public static UIButton Button(UIComponent parent, string text, float x, float y, float w, float h,
            string tooltip, MouseEventHandler onClick)
        {
            UIButton b = parent.AddUIComponent<UIButton>();
            b.size = new Vector2(w, h);
            b.relativePosition = new Vector3(x, y);
            b.text = text;
            b.textScale = 0.8f * Scale;
            b.normalBgSprite = Theme.ButtonSprite;
            b.hoveredBgSprite = Theme.ButtonHovered;
            b.pressedBgSprite = Theme.ButtonPressed;
            b.focusedBgSprite = Theme.ButtonFocused;
            b.disabledBgSprite = Theme.ButtonDisabled;
            if (tooltip != null) b.tooltip = tooltip;
            if (onClick != null) b.eventClick += onClick;
            return b;
        }

        public static void Enable(UIButton b, bool enabled, string tooltipWhenDisabled, string tooltipWhenEnabled)
        {
            if (b.isEnabled != enabled) b.isEnabled = enabled;
            string tip = enabled ? tooltipWhenEnabled : tooltipWhenDisabled;
            if (tip != null && b.tooltip != tip) b.tooltip = tip;
        }
    }
}
