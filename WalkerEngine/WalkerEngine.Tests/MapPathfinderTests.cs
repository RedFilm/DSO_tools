using WalkerEngine;

namespace WalkerEngine.Tests;

public class MapPathfinderTests
{
    [Fact]
    public void ShortestPath_GoesAroundAWall()
    {
        var image = new ColorImage(60, 30);
        Fill(image, 0, 0, 60, 30, new Rgba32(90, 90, 90, 255));
        Fill(image, 30, 0, 2, 24, new Rgba32(0, 0, 0, 255));
        var scan = MapScanner.Scan(image);
        var start = new MapPoint(0.08, 0.1);
        var goal = new MapPoint(0.9, 0.1);

        bool found = MapPathfinder.TryFind(scan, image, start, goal, out List<MapPoint> path);

        Assert.True(found);
        Assert.Equal(goal, path[^1]);
        Assert.True(path.Count >= 3);
        Assert.Contains(path, point => point.Y > 0.75);
    }

    [Fact]
    public void OpenCorridor_StaysOnAStraightLine()
    {
        var image = new ColorImage(40, 20);
        Fill(image, 0, 0, 40, 20, new Rgba32(90, 90, 90, 255));
        var scan = MapScanner.Scan(image);

        bool found = MapPathfinder.TryFind(scan, image, new MapPoint(0.1, 0.5), new MapPoint(0.9, 0.5), out List<MapPoint> path);

        Assert.True(found);
        Assert.Equal(2, path.Count);
        Assert.All(path, point => Assert.InRange(point.Y, 0.4, 0.6));
    }

    [Fact]
    public void WallStroke_AndIcon_AreNotWalkedThrough()
    {
        var image = new ColorImage(80, 40);
        Fill(image, 0, 0, 80, 40, new Rgba32(50, 50, 50, 255));
        Fill(image, 40, 0, 2, 28, new Rgba32(153, 153, 153, 255));
        Fill(image, 20, 16, 10, 10, new Rgba32(40, 90, 190, 255));
        var scan = MapScanner.Scan(image);
        var start = scan.Bounds.ToNormalized(8, 8);
        var goal = scan.Bounds.ToNormalized(70, 8);

        bool found = MapPathfinder.TryFind(scan, image, start, goal, out List<MapPoint> path, clearance: 6);

        Assert.True(found);
        Assert.Contains(path, point => point.Y > 0.7);
        foreach (var (x, y) in SegmentPixels(scan, path))
        {
            Rgba32 color = image[x, y];
            Assert.True(color.R < 140 || color.G < 140 || color.B < 140);
            Assert.False(color.B > color.R + 40 && color.B > color.G + 20);
        }
    }

    [Fact]
    public void NarrowCorridor_CharacterTouchingBothWalls_DoesNotSealThePassage()
    {
        var image = new ColorImage(80, 24);
        Fill(image, 4, 8, 72, 1, new Rgba32(153, 153, 153, 255));
        Fill(image, 4, 14, 72, 1, new Rgba32(153, 153, 153, 255));
        Fill(image, 4, 9, 72, 5, new Rgba32(50, 50, 50, 255));
        Fill(image, 36, 9, 7, 5, new Rgba32(80, 220, 70, 255));
        var scan = MapScanner.Scan(image);
        var start = scan.Bounds.ToNormalized(10, 11);
        var goal = scan.Bounds.ToNormalized(68, 11);

        bool found = MapPathfinder.TryFind(scan, image, start, goal, out List<MapPoint> path, clearance: 6);

        Assert.True(scan.PlayerFound);
        Assert.True(found);
        Assert.Contains(SegmentPixels(scan, path), pixel => pixel.X is >= 36 and <= 42 && pixel.Y is >= 9 and <= 13);
    }

    [Fact]
    public void NarrowCorridor_LargeClearance_StaysOnTheFloor()
    {
        var image = new ColorImage(90, 36);
        Fill(image, 4, 11, 82, 1, new Rgba32(153, 153, 153, 255));
        Fill(image, 4, 24, 82, 1, new Rgba32(153, 153, 153, 255));
        Fill(image, 4, 12, 82, 12, new Rgba32(50, 50, 50, 255));
        var scan = MapScanner.Scan(image);
        var start = scan.Bounds.ToNormalized(8, 18);
        var goal = scan.Bounds.ToNormalized(80, 18);

        bool found = MapPathfinder.TryFind(scan, image, start, goal, out List<MapPoint> path, clearance: 15);

        Assert.True(found);
        Assert.True(path.Count >= 2);
        foreach (var (x, y) in SegmentPixels(scan, path))
        {
            Rgba32 color = image[x, y];
            Assert.True(color.R <= 110 && color.G <= 110 && color.B <= 110);
            Assert.InRange(y, 16, 19);
        }
    }

    [Fact]
    public void BlockedSpot_IsLeftOutOfThePath()
    {
        var image = new ColorImage(90, 40);
        Fill(image, 0, 0, 90, 40, new Rgba32(90, 90, 90, 255));
        var scan = MapScanner.Scan(image);
        var blocked = scan.Bounds.ToNormalized(45, 20);
        var start = scan.Bounds.ToNormalized(8, 20);
        var goal = scan.Bounds.ToNormalized(82, 20);

        bool found = MapPathfinder.TryFind(scan, image, start, goal, out List<MapPoint> path, clearance: 0, blocked: [blocked], blockRadius: 5);

        Assert.True(found);
        Assert.DoesNotContain(SegmentPixels(scan, path), pixel =>
        {
            int dx = pixel.X - 45;
            int dy = pixel.Y - 20;
            return dx * dx + dy * dy <= 25;
        });
    }

    private static IEnumerable<(int X, int Y)> SegmentPixels(MapScan scan, List<MapPoint> path)
    {
        for (int i = 1; i < path.Count; i++)
        {
            var (x0, y0) = scan.Bounds.ToPixel(path[i - 1]);
            var (x1, y1) = scan.Bounds.ToPixel(path[i]);
            foreach (var pixel in Line((int)Math.Round(x0), (int)Math.Round(y0), (int)Math.Round(x1), (int)Math.Round(y1)))
                yield return pixel;
        }
    }

    private static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int stepX = x0 < x1 ? 1 : -1;
        int stepY = y0 < y1 ? 1 : -1;
        int error = dx - dy;
        int x = x0;
        int y = y0;
        while (true)
        {
            yield return (x, y);
            if (x == x1 && y == y1)
                yield break;

            int doubled = error * 2;
            if (doubled > -dy)
            {
                error -= dy;
                x += stepX;
            }

            if (doubled < dx)
            {
                error += dx;
                y += stepY;
            }
        }
    }

    [Fact]
    public void ChestIcon_CanBeTheGoal_WhenTheStandDiskIsOpen()
    {
        var image = new ColorImage(80, 40);
        Fill(image, 0, 0, 80, 40, new Rgba32(90, 90, 90, 255));
        Fill(image, 40, 12, 14, 15, new Rgba32(49, 112, 210, 255));
        var scan = MapScanner.Scan(image);
        var start = scan.Bounds.ToNormalized(8, 20);
        var goal = scan.Bounds.ToNormalized(47, 19);

        bool beside = MapPathfinder.TryFind(scan, image, start, goal, out List<MapPoint> around);
        bool onto = MapPathfinder.TryFind(scan, image, start, goal, out List<MapPoint> path, standRadius: 8);

        Assert.True(beside);
        Assert.True(onto);
        Assert.NotEqual(goal, around[^1]);
        Assert.Equal(goal, path[^1]);
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
