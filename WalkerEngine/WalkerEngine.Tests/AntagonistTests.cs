using WalkerEngine;

namespace WalkerEngine.Tests;

public class AntagonistTests
{
    [Fact]
    public void RedRegion_LargestIsChosen_SmallClusterIsIgnored()
    {
        var image = new ColorImage(100, 100);
        Fill(image, 8, 8, 1, 1, new Rgba32(160, 40, 0, 255));
        Fill(image, 12, 12, 4, 4, new Rgba32(160, 40, 0, 255));
        Fill(image, 40, 30, 18, 18, new Rgba32(160, 40, 0, 255));

        bool found = AntagonistFinder.TryFind(image, new MapBounds(0, 0, 99, 99), out MapPoint point);
        List<AntagonistMark> marks = AntagonistFinder.FindAll(image, new MapBounds(0, 0, 99, 99));

        Assert.True(found);
        Assert.Single(marks);
        Assert.True(marks[0].Pixels >= 25);
        Assert.InRange(point.X, 0.45, 0.65);
        Assert.InRange(point.Y, 0.35, 0.55);
    }

    [Fact]
    public void SolidRed_IsFound_WithoutEyeHoles()
    {
        var image = new ColorImage(80, 80);
        Fill(image, 4, 4, 14, 14, new Rgba32(160, 40, 0, 255));
        Fill(image, 40, 30, 14, 14, new Rgba32(127, 33, 0, 255));
        Fill(image, 43, 34, 3, 3, new Rgba32(20, 20, 20, 255));
        Fill(image, 49, 34, 3, 3, new Rgba32(20, 20, 20, 255));
        Fill(image, 46, 38, 2, 3, new Rgba32(20, 20, 20, 255));

        List<AntagonistMark> marks = AntagonistFinder.FindAll(image, new MapBounds(0, 0, 79, 79));

        Assert.Equal(2, marks.Count);
        Assert.True(marks[0].Pixels >= marks[1].Pixels);
    }

    [Fact]
    public void VoidBleedThrough_IsIgnored_WhenOffMapMask()
    {
        // Серый пол + череп на полу + красный bleed в чёрной дыре внутри bbox карты.
        var image = new ColorImage(120, 120);
        Fill(image, 0, 0, 120, 120, new Rgba32(0, 0, 0, 255));
        Fill(image, 10, 10, 90, 90, new Rgba32(70, 70, 70, 255));
        Fill(image, 70, 70, 25, 25, new Rgba32(0, 0, 0, 255)); // дыра / пустота
        Fill(image, 40, 40, 10, 10, new Rgba32(160, 40, 15, 255));
        Fill(image, 75, 75, 12, 8, new Rgba32(120, 5, 5, 255)); // артефакт в дыре

        MapScan scan = MapScanner.Scan(image);
        Assert.True(scan.HasMap);
        List<AntagonistMark> withoutMask = AntagonistFinder.FindAll(image, scan.Bounds);
        List<AntagonistMark> withMask = AntagonistFinder.FindAll(image, scan);

        Assert.True(withoutMask.Count >= 2);
        Assert.Single(withMask);
        Assert.True(scan.IsWalkable(withMask[0].Point));
    }

    [Fact]
    public void ElongatedMuddyBleed_IsIgnored()
    {
        var image = new ColorImage(80, 80);
        Fill(image, 0, 0, 80, 80, new Rgba32(60, 60, 60, 255));
        // Вытянутое «грязное» пятно как bleed-through мира.
        Fill(image, 20, 30, 8, 22, new Rgba32(114, 46, 44, 255));

        List<AntagonistMark> marks = AntagonistFinder.FindAll(image, new MapBounds(0, 0, 79, 79));

        Assert.Empty(marks);
    }

    [Fact]
    public void Chase_FromTheEnd_RunsBackAlongTheRouteToTheIcon()
    {
        MapBounds bounds = new(0, 0, 99, 99);
        MapPoint[] route = [new(0, 0), new(1, 0), new(1, 1)];
        var target = new MapPoint(0.05, 0);

        var path = RouteChase.Toward(route, bounds, new MapPoint(1, 1), target);

        Assert.Equal(target, path[^1]);
        Assert.Contains(path, point => point.X > 0.4 && point.Y < 0.2);
        Assert.True(path.Count >= 3);
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
