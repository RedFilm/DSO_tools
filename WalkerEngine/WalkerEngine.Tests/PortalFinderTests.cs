using WalkerEngine;

namespace WalkerEngine.Tests;

public class PortalFinderTests
{
    [Fact]
    public void TealPortal_IsFound_MapIconAndGrayAreIgnored()
    {
        var image = new ColorImage(400, 300);
        Fill(image, 120, 100, 40, 30, new Rgba32(20, 115, 118, 255));
        Fill(image, 20, 80, 30, 30, new Rgba32(20, 115, 118, 255));
        Fill(image, 250, 100, 24, 24, new Rgba32(168, 168, 168, 255));

        List<PortalMark> marks = PortalFinder.FindAll(image, new MapBounds(0, 70, 80, 160));

        Assert.Single(marks);
        Assert.InRange(marks[0].X, 135, 145);
        Assert.InRange(marks[0].Y, 110, 120);
        Assert.True(marks[0].Pixels >= PortalFinder.MinPixels);
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
