// Test-only: compiled solely by Tests/BondMarket.Tests.csproj, which defines
// BOND_MARKET_TESTS. The game compiles every .cs under Source\, so without
// this guard a full repo copy fails in-game (C# 7 syntax, xUnit refs).
#if BOND_MARKET_TESTS
using Xunit;

namespace MyFirstMod.Tests
{
    // WO-43: charts drawn straight into a pixel buffer.
    public class ChartRasterTests
    {
        private const uint Bg = 0x000000FFu;
        private const uint Ink = 0xFFFFFFFFu;

        [Fact]
        public void Row_MapsValueRangeOntoHeight()
        {
            Assert.Equal(0, ChartRaster.Row(0f, 0f, 1f, 10));
            Assert.Equal(9, ChartRaster.Row(1f, 0f, 1f, 10));
            Assert.Equal(9, ChartRaster.Row(5f, 0f, 1f, 10));   // clamped
            Assert.Equal(0, ChartRaster.Row(-5f, 0f, 1f, 10));
        }

        [Fact]
        public void Line_ConnectsFirstAndLastPoints()
        {
            const int w = 20, h = 10;
            var px = new uint[w * h];
            ChartRaster.Clear(px, Bg);
            ChartRaster.Line(px, w, h, new[] { 0f, 1f }, 2, 0f, 1f, Ink);
            Assert.Equal(Ink, px[0]);                       // bottom-left
            Assert.Equal(Ink, px[(h - 1) * w + (w - 1)]);   // top-right
            int inked = 0;
            foreach (uint p in px) if (p == Ink) inked++;
            Assert.InRange(inked, w, w + h);                // a thin continuous line
        }

        [Fact]
        public void Steps_HoldLevelsAndJoinChanges()
        {
            const int w = 8, h = 8;
            var px = new uint[w * h];
            ChartRaster.Clear(px, Bg);
            ChartRaster.Steps(px, w, h, new[] { 7f, 7f, 3f, 3f }, 4, 0f, 7f, Ink);
            Assert.Equal(Ink, px[7 * w + 0]);   // first level at the top
            Assert.Equal(Ink, px[3 * w + 7]);   // last level lower down
            Assert.Equal(Ink, px[5 * w + 4]);   // the vertical join at the change
        }

        [Fact]
        public void Gridline_IsDotted()
        {
            const int w = 10, h = 5;
            var px = new uint[w * h];
            ChartRaster.Clear(px, Bg);
            ChartRaster.Gridline(px, w, h, 0.5f, 0f, 1f, Ink);
            Assert.Equal(Ink, px[2 * w + 0]);
            Assert.Equal(Bg, px[2 * w + 1]);
        }

        [Fact]
        public void Rgba_PacksChannels()
        {
            Assert.Equal(0x11223344u, ChartRaster.Rgba(0x11, 0x22, 0x33, 0x44));
        }
    }
}

#endif
