using WalkerEngine;

namespace WalkerEngine.Tests;

public class DoorFinderTests
{
    [Fact]
    public void CyanDoor_IsFound_ChestAndRedAreIgnored()
    {
        var image = new ColorImage(80, 80);
        Fill(image, 0, 0, 80, 80, new Rgba32(44, 44, 44, 255));
        Fill(image, 10, 10, 6, 12, new Rgba32(40, 90, 170, 255));
        Fill(image, 40, 20, 14, 11, new Rgba32(40, 90, 170, 255));
        Fill(image, 60, 60, 6, 6, new Rgba32(180, 40, 30, 255));

        List<DoorMark> marks = DoorFinder.FindAll(image, new MapBounds(0, 0, 79, 79));

        Assert.Single(marks);
        Assert.Equal(6 * 12, marks[0].Pixels);
        Assert.True(marks[0].Point.X < 0.3);
    }

    [Fact]
    public void EnterButton_IsFound_BelowTheMap()
    {
        var image = new ColorImage(200, 160);
        Fill(image, 0, 0, 200, 160, new Rgba32(12, 12, 12, 255));
        Fill(image, 8, 8, 5, 5, new Rgba32(68, 154, 225, 255));
        Fill(image, 90, 120, 70, 16, new Rgba32(8, 70, 120, 255));

        bool found = EntranceFinder.TryFindButton(image, new MapBounds(0, 0, 70, 70), out int x, out int y);

        Assert.True(found);
        Assert.InRange(x, 90, 160);
        Assert.InRange(y, 120, 136);
    }

    [Fact]
    public void RetryButton_IsFound_CenteredAtBottom()
    {
        var image = new ColorImage(400, 300);
        Fill(image, 0, 0, 400, 300, new Rgba32(12, 12, 12, 255));
        // Широкая синяя «Новая попытка» по центру низа; высота >36 — Enter-детектор её не берёт.
        Fill(image, 120, 245, 160, 40, new Rgba32(40, 140, 185, 255));
        Fill(image, 320, 240, 60, 14, new Rgba32(8, 70, 120, 255));

        Assert.True(EntranceFinder.TryFindRetryButton(image, out int x, out int y));
        Assert.InRange(x, 180, 220);
        Assert.InRange(y, 255, 280);
        Assert.True(EntranceFinder.TryFindButton(image, default, out int enterX, out int enterY));
        Assert.InRange(enterX, 320, 380);
        Assert.InRange(enterY, 240, 255);
    }

    [Fact]
    public void RetryButton_IsFound_OnLiveConnectionFailShot()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "door-retry.png");
        if (!File.Exists(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "door-retry.png");
        Assert.True(File.Exists(path), $"Нет эталона {path}");

        var image = ColorImage.Load(path);
        Assert.True(EntranceFinder.TryFindRetryButton(image, out int x, out int y));
        // Замер на 1024×723: центр ≈ (514,689).
        Assert.InRange(x, 480, 540);
        Assert.InRange(y, 670, 705);
    }

    [Fact]
    public void LoadingBar_IsTheGreenStripAtTheBottom()
    {
        var image = new ColorImage(200, 80);
        Fill(image, 0, 0, 200, 80, new Rgba32(8, 8, 8, 255));
        Fill(image, 40, 74, 90, 3, new Rgba32(40, 180, 50, 255));

        Assert.True(EntranceFinder.HasLoadingBar(image));
    }

    [Fact]
    public void LoadingBar_DetectsGrayTrackBeforeGreenFill()
    {
        var image = new ColorImage(400, 200);
        Fill(image, 0, 0, 400, 200, new Rgba32(8, 8, 8, 255));
        // Пустая полоса: серый трек на всю ширину бара.
        Fill(image, 60, 184, 280, 5, new Rgba32(92, 92, 96, 255));

        Assert.True(EntranceFinder.HasLoadingBar(image));
    }

    [Fact]
    public void LoadingBar_DetectsDimGreenProgress()
    {
        var image = new ColorImage(400, 200);
        Fill(image, 0, 0, 400, 200, new Rgba32(8, 8, 8, 255));
        Fill(image, 60, 184, 160, 5, new Rgba32(57, 113, 41, 255));
        Fill(image, 220, 184, 120, 5, new Rgba32(92, 92, 96, 255));

        Assert.True(EntranceFinder.HasLoadingBar(image));
    }

    [Fact]
    public void LoadingBar_IgnoresPlainDarkBottom()
    {
        var image = new ColorImage(400, 200);
        Fill(image, 0, 0, 400, 200, new Rgba32(8, 8, 8, 255));

        Assert.False(EntranceFinder.HasLoadingBar(image));
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
