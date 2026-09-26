using ColossalFramework.UI;
using UnityEngine;

namespace MyFirstMod
{
    // WO-43: one chart, one texture, one sprite. The owner draws into Pixels with
    // ChartRaster and calls Upload; the texture is rewritten in place, never
    // recreated, and only when the data changed (once per period).
    public sealed class ChartSprite
    {
        // Texture uploads since start (diagnostic: once per period per chart).
        public static int Uploads;

        public readonly int Width;
        public readonly int Height;
        public readonly uint[] Pixels;
        private readonly Color32[] _colors;
        private readonly Texture2D _texture;
        public readonly UITextureSprite Sprite;

        public ChartSprite(UIComponent parent, float x, float y, int width, int height, string tooltip)
        {
            Width = width;
            Height = height;
            Pixels = new uint[width * height];
            _colors = new Color32[width * height];
            _texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            _texture.filterMode = FilterMode.Point;
            _texture.wrapMode = TextureWrapMode.Clamp;
            Sprite = parent.AddUIComponent<UITextureSprite>();
            Sprite.size = new Vector2(width, height);
            Sprite.relativePosition = new Vector3(x, y);
            Sprite.texture = _texture;
            if (tooltip != null) Sprite.tooltip = tooltip;
        }

        public void Upload()
        {
            for (int i = 0; i < Pixels.Length; i++)
            {
                uint p = Pixels[i];
                _colors[i] = new Color32((byte)(p >> 24), (byte)(p >> 16), (byte)(p >> 8), (byte)p);
            }
            _texture.SetPixels32(_colors);
            _texture.Apply();
            Uploads++;
        }

        public static uint Pack(Color32 c) { return ChartRaster.Rgba(c.r, c.g, c.b, c.a); }
    }
}
