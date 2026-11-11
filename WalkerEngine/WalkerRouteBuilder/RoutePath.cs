using System.Globalization;
using System.Text.Json;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal readonly record struct RoutePose(
    MapPoint Position,
    double HeadingRadians,
    int SegmentIndex,
    double Distance,
    double TotalLength,
    bool Finished);

internal static class RouteGeometry
{
    public static double HeadingRadians(MapBounds bounds, MapPoint from, MapPoint to)
    {
        var (x0, y0) = bounds.ToPixel(from);
        var (x1, y1) = bounds.ToPixel(to);
        return Math.Atan2(y1 - y0, x1 - x0);
    }

    public static double HeadingDegrees(MapBounds bounds, MapPoint from, MapPoint to)
    {
        double degrees = HeadingRadians(bounds, from, to) * 180.0 / Math.PI;
        return (degrees % 360 + 360) % 360;
    }

    public static double PixelLength(MapBounds bounds, MapPoint from, MapPoint to)
    {
        var (x0, y0) = bounds.ToPixel(from);
        var (x1, y1) = bounds.ToPixel(to);
        double dx = x1 - x0;
        double dy = y1 - y0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double TotalLength(IReadOnlyList<MapPoint> points, MapBounds bounds)
    {
        double total = 0;
        for (int i = 0; i < points.Count - 1; i++)
            total += PixelLength(bounds, points[i], points[i + 1]);
        return total;
    }

    public static RoutePose Pose(IReadOnlyList<MapPoint> points, MapBounds bounds, double distance)
    {
        if (points.Count == 0)
            return new RoutePose(default, 0, -1, 0, 0, true);

        if (points.Count == 1 || bounds.IsEmpty)
            return new RoutePose(points[0], 0, 0, 0, 0, true);

        double total = 0;
        for (int i = 0; i < points.Count - 1; i++)
            total += PixelLength(bounds, points[i], points[i + 1]);

        if (total <= 0.0001)
            return new RoutePose(points[^1], 0, points.Count - 2, 0, total, true);

        if (distance >= total)
        {
            double heading = HeadingRadians(bounds, points[^2], points[^1]);
            return new RoutePose(points[^1], heading, points.Count - 2, total, total, true);
        }

        if (distance < 0)
            distance = 0;

        double walked = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double length = PixelLength(bounds, points[i], points[i + 1]);
            if (length <= 0.0001)
                continue;

            if (walked + length >= distance)
            {
                double t = (distance - walked) / length;
                return new RoutePose(
                    MapPoint.Lerp(points[i], points[i + 1], t),
                    HeadingRadians(bounds, points[i], points[i + 1]),
                    i,
                    distance,
                    total,
                    false);
            }

            walked += length;
        }

        double lastHeading = HeadingRadians(bounds, points[^2], points[^1]);
        return new RoutePose(points[^1], lastHeading, points.Count - 2, total, total, true);
    }

    public static HashSet<int> OffMapSegments(
        IReadOnlyList<MapPoint> points,
        MapBounds bounds,
        Func<MapPoint, bool> isWalkable)
    {
        var bad = new HashSet<int>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (!SegmentStaysOnMap(points[i], points[i + 1], bounds, isWalkable))
                bad.Add(i);
        }

        return bad;
    }

    private static bool SegmentStaysOnMap(
        MapPoint from,
        MapPoint to,
        MapBounds bounds,
        Func<MapPoint, bool> isWalkable)
    {
        if (!isWalkable(from) || !isWalkable(to))
            return false;

        double length = bounds.IsEmpty ? 0 : PixelLength(bounds, from, to);
        int steps = Math.Clamp((int)Math.Ceiling(length / 2.0), 1, 4000);
        for (int step = 1; step < steps; step++)
        {
            double t = step / (double)steps;
            if (!isWalkable(MapPoint.Lerp(from, to, t)))
                return false;
        }

        return true;
    }
}

internal sealed class RouteDocument
{
    public int Version { get; set; } = 1;

    public string? MapFile { get; set; }

    public List<RoutePointDto> Points { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static RouteDocument FromPoints(IReadOnlyList<MapPoint> points, string? mapFile)
    {
        var document = new RouteDocument { MapFile = mapFile };
        foreach (var point in points)
            document.Points.Add(new RoutePointDto { X = point.X, Y = point.Y });
        return document;
    }

    public List<MapPoint> ToPoints()
    {
        var points = new List<MapPoint>(Points.Count);
        foreach (var point in Points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidDataException("В маршруте есть координата, которая не является числом.");
            points.Add(new MapPoint(point.X, point.Y));
        }

        return points;
    }

    public void Save(string path)
    {
        if (!string.IsNullOrWhiteSpace(MapFile))
            MapFile = MapLibrary.ToStoredPath(MapLibrary.ToFullPath(MapFile));
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static RouteDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<RouteDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Файл маршрута пуст.");
        if (document.Points is null)
            document.Points = [];
        return document;
    }
}

internal sealed class RoutePointDto
{
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Клавиши вектора, который заканчивается в этой точке. У старта их нет.</summary>
    public List<RouteKeyDto>? Keys { get; set; }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{X:0.000},{Y:0.000}");
}

internal sealed class RouteKeyDto
{
    public string Key { get; set; } = "";

    public int IntervalMilliseconds { get; set; } = 1000;
}
