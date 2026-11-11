using WalkerEngine;

namespace WalkerEngine.Tests;

public class MountCastFinderTests
{
    [Fact]
    public void CyanStrip_InCenter_IsFound()
    {
        var image = new ColorImage(400, 300);
        // Центр области поиска ~ 48% x 53% → около (192, 160).
        Fill(image, 170, 156, 50, 8, new Rgba32(90, 170, 230, 255));

        bool found = MountCastFinder.TryFind(image, MapBounds.Empty, out int x, out int y, out int width, out int height);

        Assert.True(found);
        Assert.InRange(x, 170, 220);
        Assert.InRange(y, 156, 164);
        Assert.InRange(width, 40, 60);
        Assert.InRange(height, 6, 10);
    }

    [Fact]
    public void VerticalBlob_IsIgnored()
    {
        var image = new ColorImage(400, 300);
        Fill(image, 190, 140, 10, 40, new Rgba32(90, 170, 230, 255));

        Assert.False(MountCastFinder.IsVisible(image, MapBounds.Empty));
    }

    [Fact]
    public void NoCyan_ReturnsFalse()
    {
        var image = new ColorImage(400, 300);
        Fill(image, 170, 156, 50, 8, new Rgba32(40, 170, 50, 255));

        Assert.False(MountCastFinder.IsVisible(image, MapBounds.Empty));
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
