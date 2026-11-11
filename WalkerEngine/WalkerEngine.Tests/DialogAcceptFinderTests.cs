using WalkerEngine;

namespace WalkerEngine.Tests;

public class DialogAcceptFinderTests
{
    [Fact]
    public void GreenCheck_IsFound_OutsideExcludedMap()
    {
        var image = new ColorImage(400, 300);
        Fill(image, 10, 50, 12, 12, new Rgba32(50, 170, 40, 255));
        Fill(image, 220, 120, 14, 12, new Rgba32(50, 170, 40, 255));
        var excluded = new MapBounds(0, 40, 120, 200);

        bool found = DialogAcceptFinder.TryFind(image, excluded, out int x, out int y);

        Assert.True(found);
        Assert.InRange(x, 220, 240);
        Assert.InRange(y, 120, 140);
    }

    [Fact]
    public void NoGreen_ReturnsFalse()
    {
        var image = new ColorImage(200, 200);
        Fill(image, 80, 80, 20, 10, new Rgba32(40, 90, 170, 255));

        Assert.False(DialogAcceptFinder.TryFind(image, MapBounds.Empty, out _, out _));
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
