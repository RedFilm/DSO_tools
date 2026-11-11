using WalkerEngine;

namespace WalkerEngine.Tests;

public class ChestFinderTests
{
    [Fact]
    public void BlueChest_IsFound_SmallSpotAndRedAreIgnored()
    {
        var image = new ColorImage(80, 80);
        Fill(image, 0, 0, 80, 80, new Rgba32(44, 44, 44, 255));
        Fill(image, 8, 8, 6, 6, new Rgba32(49, 112, 210, 255));
        Fill(image, 40, 28, 14, 14, new Rgba32(49, 112, 210, 255));
        Fill(image, 44, 31, 4, 3, new Rgba32(68, 150, 235, 255));
        Fill(image, 60, 60, 16, 16, new Rgba32(180, 40, 30, 255));

        List<ChestMark> marks = ChestFinder.FindAll(image, new MapBounds(0, 0, 79, 79));

        Assert.Single(marks);
        Assert.InRange(marks[0].Pixels, 45, 14 * 14);
        Assert.InRange(marks[0].Point.X, 0.45, 0.65);
        Assert.InRange(marks[0].Point.Y, 0.30, 0.50);
    }

    [Fact]
    public void RoundMarker_IsNotAChest()
    {
        var image = new ColorImage(80, 40);
        Fill(image, 4, 8, 11, 11, new Rgba32(70, 140, 220, 255));
        Fill(image, 40, 6, 14, 15, new Rgba32(70, 140, 220, 255));

        List<ChestMark> marks = ChestFinder.FindAll(image, new MapBounds(0, 0, 79, 39));

        Assert.Single(marks);
        Assert.Equal(14 * 15, marks[0].Pixels);
        Assert.True(marks[0].Point.X > 0.5);
    }

    [Fact]
    public void BlueArch_IsNotAChest()
    {
        var image = new ColorImage(40, 40);
        Fill(image, 8, 8, 16, 3, new Rgba32(70, 140, 220, 255));
        Fill(image, 8, 11, 4, 14, new Rgba32(70, 140, 220, 255));
        Fill(image, 20, 11, 4, 14, new Rgba32(70, 140, 220, 255));

        List<ChestMark> marks = ChestFinder.FindAll(image, new MapBounds(0, 0, 39, 39));

        Assert.Empty(marks);
    }

    [Fact]
    public void CoveredChest_PixelsRemainNearTheLastPoint()
    {
        var image = new ColorImage(80, 80);
        var bounds = new MapBounds(0, 0, 79, 79);
        Fill(image, 40, 28, 14, 14, new Rgba32(49, 112, 210, 255));
        Fill(image, 42, 30, 10, 10, new Rgba32(40, 180, 40, 255));
        MapPoint point = bounds.ToNormalized(46, 34);

        Assert.Empty(ChestFinder.FindAll(image, bounds));
        Assert.InRange(ChestFinder.CountNear(image, bounds, point, 16), 90, 100);

        Fill(image, 40, 28, 14, 14, new Rgba32(44, 44, 44, 255));
        Assert.Equal(0, ChestFinder.CountNear(image, bounds, point, 16));
    }

    [Fact]
    public void HoverRing_IsLit_GreenFogIsNot()
    {
        var lit = new ColorImage(200, 200);
        Fill(lit, 90, 90, 20, 20, new Rgba32(168, 168, 168, 255));
        Assert.True(ChestHover.IsLit(lit, 100, 100));

        var fog = new ColorImage(200, 200);
        Fill(fog, 90, 90, 20, 20, new Rgba32(40, 180, 40, 255));
        Assert.False(ChestHover.IsLit(fog, 100, 100));

        var sparse = new ColorImage(200, 200);
        Fill(sparse, 98, 98, 4, 4, new Rgba32(168, 168, 168, 255));
        Assert.False(ChestHover.IsLit(sparse, 100, 100));
    }

    [Fact]
    public void HoverRing_CenterIsInsideTheOutline()
    {
        var ring = new ColorImage(200, 200);
        Fill(ring, 70, 70, 60, 60, new Rgba32(168, 168, 168, 255));
        Fill(ring, 72, 72, 56, 56, new Rgba32(0, 0, 0, 255));

        Assert.True(ChestHover.TryCenter(ring, 70, 100, MapBounds.Empty, MapBounds.Empty, out int x, out int y));
        Assert.InRange(x, 95, 105);
        Assert.InRange(y, 95, 105);
    }

    [Fact]
    public void SkillBar_IsNotADoorHighlight()
    {
        var image = new ColorImage(200, 220);
        Fill(image, 10, 170, 180, 40, new Rgba32(168, 168, 168, 255));

        Assert.False(ChestHover.TryCenter(image, 100, 150, new MapBounds(0, 0, 199, 155), MapBounds.Empty, out _, out _));
    }

    private static void Fill(ColorImage image, int x, int y, int width, int height, Rgba32 color)
    {
        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width; px++)
                image[px, py] = color;
        }
    }
}
