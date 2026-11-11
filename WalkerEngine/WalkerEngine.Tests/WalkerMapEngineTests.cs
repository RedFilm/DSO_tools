namespace WalkerEngine.Tests;

public class WalkerMapEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PngRoundtrip_PreservesPixels()
    {
        var image = new ColorImage(3, 2);
        image[0, 0] = Rgba32.Opaque(10, 20, 30);
        image[1, 0] = Rgba32.Opaque(255, 0, 0);
        image[2, 0] = new Rgba32(0, 255, 0, 128);
        image[0, 1] = Rgba32.Opaque(0, 0, 0);
        image[1, 1] = Rgba32.Opaque(40, 40, 40);
        image[2, 1] = Rgba32.Opaque(96, 220, 80);

        string path = Path.Combine(Path.GetTempPath(), "walker-roundtrip.png");
        image.Save(path);
        var loaded = ColorImage.Load(path);

        Assert.Equal(3, loaded.Width);
        Assert.Equal(2, loaded.Height);
        Assert.Equal(image[0, 0], loaded[0, 0]);
        Assert.Equal(image[2, 0], loaded[2, 0]);
        Assert.Equal(image[2, 1], loaded[2, 1]);
    }

    [Fact]
    public void Timeline_InterpolatesAndHolds()
    {
        var engine = new WalkerMapEngine();
        engine.Report(new MapPoint(0, 0), T0);
        engine.Report(new MapPoint(1, 0.5), T0.AddSeconds(2));

        var middle = engine.PositionAt(T0.AddSeconds(1));
        Near(0.5, middle.X);
        Near(0.25, middle.Y);

        var before = engine.PositionAt(T0.AddMinutes(-5));
        Near(0, before.X);

        var after = engine.PositionAt(T0.AddSeconds(10));
        Near(1, after.X);
        Near(0.5, after.Y);

        var predicted = engine.Predict(T0.AddSeconds(4));
        Near(2, predicted.X);
        Near(1, predicted.Y);
        Near(0.5, engine.LatestVelocity.X);
        Near(0.25, engine.LatestVelocity.Y);
    }

    [Fact]
    public void ForgetBefore_DropsOldSamples()
    {
        var engine = new WalkerMapEngine();
        engine.Report(new MapPoint(0, 0), T0);
        engine.Report(new MapPoint(1, 0), T0.AddSeconds(2));
        engine.Report(new MapPoint(2, 0), T0.AddSeconds(4));

        engine.ForgetBefore(T0.AddSeconds(2));

        Assert.Equal(2, engine.SampleCount);
        Near(1, engine.PositionAt(T0).X);
        Near(1.5, engine.PositionAt(T0.AddSeconds(3)).X);
    }

    [Fact]
    public void SyntheticMap_ReadsPlayerAndRejectsVoid()
    {
        var image = BuildMap();
        var engine = new WalkerMapEngine();
        var seen = engine.Observe(image, T0);

        Assert.True(seen.HasMap);
        Assert.True(seen.PlayerFound);
        Assert.Equal(40, seen.Bounds.Left);
        Assert.Equal(30, seen.Bounds.Top);
        Assert.Equal(340, seen.Bounds.Right);
        Assert.Equal(250, seen.Bounds.Bottom);
        Near(302, seen.PixelX);
        Near(102, seen.PixelY);
        Near((302.5 - 40) / 301, seen.Position.X);
        Near((102.5 - 30) / 221, seen.Position.Y);
        Assert.Equal(25, seen.PlayerPixels);

        Assert.True(engine.IsWalkable(seen.Position));
        Assert.True(engine.IsWalkable(seen.Bounds.ToNormalized(50, 50)));
        Assert.False(engine.IsWalkable(seen.Bounds.ToNormalized(110, 90)));
        Assert.False(engine.IsWalkable(new MapPoint(-0.2, 0.5)));
    }

    [Fact]
    public void ScaledMap_KeepsTheSameNormalizedPosition()
    {
        var image = BuildMap();
        var engine = new WalkerMapEngine();
        var original = engine.Observe(image, T0);

        var scaled = ScaleNearest(image, 2);
        var again = engine.Observe(scaled, T0.AddSeconds(1));

        Near(original.Position.X, again.Position.X, 1e-9);
        Near(original.Position.Y, again.Position.Y, 1e-9);
        Near(original.PixelX * 2 + 0.5, again.PixelX, 1e-6);
        Near(original.PixelY * 2 + 0.5, again.PixelY, 1e-6);
    }

    [Fact]
    public void UpdateMap_ChangesScaleWithoutMovingThePlayer()
    {
        var image = BuildMap();
        var engine = new WalkerMapEngine();
        var seen = engine.Observe(image, T0);

        engine.UpdateMap(ScaleNearest(image, 2));

        var still = engine.PositionAt(T0.AddSeconds(5));
        Near(seen.Position.X, still.X);
        Near(seen.Position.Y, still.Y);
        Assert.True(engine.IsWalkable(still));
        Assert.Equal(1, engine.SampleCount);

        var overlay = engine.DrawOverlay();
        var (pixelX, pixelY) = engine.MapBounds.ToPixel(still);
        int x = (int)Math.Floor(pixelX + 0.5);
        int y = (int)Math.Floor(pixelY + 0.5);
        Assert.Equal(Rgba32.Opaque(255, 255, 0), overlay[x, y]);
    }

    [Fact]
    public void TryMove_SlidesAlongAnOpenAxisAndStopsAtVoid()
    {
        var engine = new WalkerMapEngine();
        var seen = engine.Observe(BuildMap(), T0);

        bool moved = engine.TryMove(new MapVector(-0.02, 0), T0.AddSeconds(1), out var stepped);
        Assert.True(moved);
        Near(seen.Position.X - 0.02, stepped.X);
        Near(seen.Position.Y, stepped.Y);
        Assert.True(engine.IsWalkable(stepped));

        bool blocked = engine.TryMove(new MapVector(-2, -2), T0.AddSeconds(2), out var stopped);
        Assert.False(blocked);
        Near(stepped.X, stopped.X);
        Near(stepped.Y, stopped.Y);

        bool slid = engine.TryMove(new MapVector(0.01, -2), T0.AddSeconds(3), out var alongX);
        Assert.True(slid);
        Near(stepped.X + 0.01, alongX.X);
        Near(stepped.Y, alongX.Y);
    }

    [Fact]
    public void RealMap_FindsTheGreenMarkerAndSurvivesScaling()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "map.png");
        var image = ColorImage.Load(path);
        Assert.Equal(new Rgba32(120, 229, 99, 255), image[645, 360]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), image[0, 0]);

        var engine = new WalkerMapEngine();
        var seen = engine.Observe(image, T0);

        Assert.True(seen.PlayerFound);
        Assert.InRange(seen.PixelX, 640, 652);
        Assert.InRange(seen.PixelY, 356, 368);
        Assert.InRange(seen.Position.X, 0.93, 0.96);
        Assert.InRange(seen.Position.Y, 0.57, 0.61);
        Assert.True(engine.IsWalkable(seen.Position));
        Assert.False(engine.IsWalkable(seen.Bounds.ToNormalized(400, 300)));

        var scaled = engine.Observe(ScaleNearest(image, 2), T0.AddSeconds(1));
        Near(seen.Position.X, scaled.Position.X, 1e-6);
        Near(seen.Position.Y, scaled.Position.Y, 1e-6);

        string overlayPath = Path.Combine(Path.GetTempPath(), "walker-map-overlay.png");
        engine.UpdateMap(image);
        engine.DrawOverlay(T0).Save(overlayPath);
        Assert.True(File.Exists(overlayPath));
    }

    private static ColorImage BuildMap()
    {
        var image = new ColorImage(420, 300);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
                image[x, y] = Rgba32.Opaque(0, 0, 0);
        }

        for (int y = 30; y <= 250; y++)
        {
            for (int x = 40; x <= 340; x++)
                image[x, y] = Rgba32.Opaque(40, 40, 40);
        }

        for (int y = 80; y <= 99; y++)
        {
            for (int x = 100; x <= 119; x++)
                image[x, y] = Rgba32.Opaque(0, 0, 0);
        }

        for (int y = 100; y <= 104; y++)
        {
            for (int x = 300; x <= 304; x++)
                image[x, y] = Rgba32.Opaque(80, 220, 60);
        }

        for (int y = 1; y <= 6; y++)
        {
            for (int x = 1; x <= 6; x++)
                image[x, y] = Rgba32.Opaque(230, 230, 230);
        }

        for (int y = 10; y <= 29; y++)
        {
            for (int x = 360; x <= 379; x++)
                image[x, y] = Rgba32.Opaque(90, 230, 70);
        }

        image[80, 200] = Rgba32.Opaque(180, 40, 0);
        return image;
    }

    private static ColorImage ScaleNearest(ColorImage source, int factor)
    {
        var image = new ColorImage(source.Width * factor, source.Height * factor);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var color = source[x, y];
                int ox = x * factor;
                int oy = y * factor;
                for (int dy = 0; dy < factor; dy++)
                {
                    for (int dx = 0; dx < factor; dx++)
                        image[ox + dx, oy + dy] = color;
                }
            }
        }

        return image;
    }

    private static void Near(double expected, double actual, double tolerance = 1e-9)
        => Assert.True(Math.Abs(expected - actual) <= tolerance, $"ожидалось {expected}, получено {actual}");
}
