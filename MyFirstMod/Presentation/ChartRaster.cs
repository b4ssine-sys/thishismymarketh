using System;

namespace MyFirstMod
{
    // WO-43: draws small charts straight into a pixel buffer (packed 0xRRGGBBAA,
    // row 0 at the bottom, as Unity textures are laid out), so a chart is one
    // texture instead of a UI component per data point. Pure and allocation-free.
    public static class ChartRaster
    {
        public static uint Rgba(byte r, byte g, byte b, byte a)
        {
            return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
        }

        public static void Clear(uint[] pixels, uint color)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        }

        public static void Plot(uint[] pixels, int width, int height, int x, int y, uint color)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            pixels[y * width + x] = color;
        }

        // A horizontal gridline at a value.
        public static void Gridline(uint[] pixels, int width, int height, float value, float min, float max, uint color)
        {
            int y = Row(value, min, max, height);
            for (int x = 0; x < width; x += 2) Plot(pixels, width, height, x, y, color);
        }

        // A polyline through values[0..count), spread across the width.
        public static void Line(uint[] pixels, int width, int height, float[] values, int count,
            float min, float max, uint color)
        {
            if (count <= 0) return;
            if (count == 1)
            {
                int y1 = Row(values[0], min, max, height);
                for (int x = 0; x < width; x++) Plot(pixels, width, height, x, y1, color);
                return;
            }
            int prevX = 0, prevY = Row(values[0], min, max, height);
            for (int i = 1; i < count; i++)
            {
                int x = (int)Math.Round((double)i * (width - 1) / (count - 1));
                int y = Row(values[i], min, max, height);
                Segment(pixels, width, height, prevX, prevY, x, y, color);
                prevX = x;
                prevY = y;
            }
        }

        // A step chart (a rating holds its value until it changes).
        public static void Steps(uint[] pixels, int width, int height, float[] values, int count,
            float min, float max, uint color)
        {
            if (count <= 0) return;
            float columnWidth = (float)width / count;
            int prevY = Row(values[0], min, max, height);
            for (int i = 0; i < count; i++)
            {
                int y = Row(values[i], min, max, height);
                int x0 = (int)(i * columnWidth);
                int x1 = (int)((i + 1) * columnWidth) - 1;
                if (i > 0 && y != prevY) Segment(pixels, width, height, x0, prevY, x0, y, color);
                for (int x = x0; x <= x1; x++) Plot(pixels, width, height, x, y, color);
                prevY = y;
            }
        }

        public static int Row(float value, float min, float max, int height)
        {
            if (max <= min) return 0;
            float t = (value - min) / (max - min);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return (int)Math.Round(t * (height - 1));
        }

        // Bresenham.
        private static void Segment(uint[] pixels, int width, int height, int x0, int y0, int x1, int y1, uint color)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Plot(pixels, width, height, x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
