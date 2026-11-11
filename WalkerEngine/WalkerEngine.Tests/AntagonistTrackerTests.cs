using WalkerEngine;

namespace WalkerEngine.Tests;

public class AntagonistTrackerTests
{
    [Fact]
    public void NeedsThreeFrames_BeforeConfirm()
    {
        var tracker = new AntagonistTracker();
        MapScan scan = ScanWithPlayer(40, 40);
        var mark = new AntagonistMark(Pixel(scan, 48, 42), 50);

        Assert.Empty(tracker.Update(scan, [mark]));
        Assert.Empty(tracker.Update(scan, [mark]));
        Assert.Single(tracker.Update(scan, [mark]));
    }

    [Fact]
    public void FarMark_IsAccepted_WhilePlayerIdle()
    {
        var tracker = new AntagonistTracker();
        MapScan scan = ScanWithPlayer(20, 20);
        var far = new AntagonistMark(Pixel(scan, 180, 180), 80);

        Assert.Empty(tracker.Update(scan, [far]));
        Assert.Empty(tracker.Update(scan, [far]));
        Assert.Single(tracker.Update(scan, [far]));
    }

    [Fact]
    public void FarMark_StaysConfirmed_WhenPlayerStartsMoving()
    {
        var tracker = new AntagonistTracker();
        var mark = new AntagonistMark(new MapPoint(0.8, 0.05), 119);

        Assert.Empty(tracker.Update(ScanWithPlayer(124, 254), [mark]));
        Assert.Empty(tracker.Update(ScanWithPlayer(124, 254), [mark]));
        Assert.Single(tracker.Update(ScanWithPlayer(124, 254), [mark]));
        // Старт движения к цели — раньше «залипший bleed» сбрасывал confirmed.
        Assert.Single(tracker.Update(ScanWithPlayer(129, 257), [mark]));
        Assert.Single(tracker.Update(ScanWithPlayer(135, 250), [mark]));
    }

    [Fact]
    public void DroppedObservation_ClearsTrack()
    {
        var tracker = new AntagonistTracker();
        MapScan scan = ScanWithPlayer(40, 40);
        var mark = new AntagonistMark(Pixel(scan, 48, 42), 50);

        Assert.Empty(tracker.Update(scan, [mark]));
        Assert.Empty(tracker.Update(scan, [mark]));
        Assert.Single(tracker.Update(scan, [mark]));
        Assert.Empty(tracker.Update(scan, []));
        Assert.Empty(tracker.Update(scan, [mark]));
        Assert.Empty(tracker.Update(scan, [mark]));
        Assert.Single(tracker.Update(scan, [mark]));
    }

    private static MapScan ScanWithPlayer(double playerX, double playerY)
    {
        var image = new ColorImage(220, 220);
        for (int y = 0; y < 220; y++)
        {
            for (int x = 0; x < 220; x++)
                image[x, y] = new Rgba32(70, 70, 70, 255);
        }

        int px = (int)Math.Round(playerX);
        int py = (int)Math.Round(playerY);
        for (int y = py - 3; y <= py + 3; y++)
        {
            for (int x = px - 3; x <= px + 3; x++)
            {
                if (x >= 0 && y >= 0 && x < 220 && y < 220)
                    image[x, y] = new Rgba32(40, 220, 60, 255);
            }
        }

        return MapScanner.Scan(image);
    }

    private static MapPoint Pixel(MapScan scan, double x, double y)
        => scan.Bounds.ToNormalized(x, y);
}
