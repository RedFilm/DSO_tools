namespace WalkerEngine;

/// <summary>Ближайшее место маркера на ломаной маршрута.</summary>
/// <summary>
/// CrossTrackPixels: плюс — правее направления пути, минус — левее.
/// Ось Y карты смотрит вниз.
/// </summary>
public readonly record struct PathHit(
    MapPoint Point,
    int SegmentIndex,
    double AlongPixels,
    double CrossTrackPixels,
    double RemainingPixels,
    double DistanceToEndPixels);

/// <summary>Проекция точки на маршрут и точка, которая лежит на маршруте через заданное число пикселей.</summary>
public static class PathCourse
{
    public static bool TryProject(IReadOnlyList<MapPoint> route, MapBounds bounds, MapPoint position, out PathHit hit)
    {
        hit = default;
        if (route.Count < 2 || bounds.IsEmpty)
            return false;

        double bestDistance = double.MaxValue;
        double bestAlong = -1;
        bool found = false;
        double walked = 0;

        for (int i = 0; i < route.Count - 1; i++)
        {
            var (ax, ay) = bounds.ToPixel(route[i]);
            var (bx, by) = bounds.ToPixel(route[i + 1]);
            double abx = bx - ax;
            double aby = by - ay;
            double length = Math.Sqrt(abx * abx + aby * aby);
            if (length <= 0.0001)
                continue;

            var (px, py) = bounds.ToPixel(position);
            double apx = px - ax;
            double apy = py - ay;
            double t = Math.Clamp((apx * abx + apy * aby) / (length * length), 0, 1);
            double cx = ax + abx * t;
            double cy = ay + aby * t;
            double dx = px - cx;
            double dy = py - cy;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double along = walked + t * length;

            if (!found || distance < bestDistance - 0.01 || (Math.Abs(distance - bestDistance) <= 0.01 && along > bestAlong))
            {
                double cross = abx * apy - aby * apx;
                double signed = Math.Abs(cross) <= 0.0001 ? 0 : Math.Sign(cross) * distance;
                hit = new PathHit(
                    MapPoint.Lerp(route[i], route[i + 1], t),
                    i,
                    along,
                    signed,
                    0,
                    0);
                bestDistance = distance;
                bestAlong = along;
                found = true;
            }

            walked += length;
        }

        if (!found)
            return false;

        double total = walked;
        double endDistance = Distance(bounds, position, route[^1]);
        hit = hit with
        {
            RemainingPixels = Math.Max(0, total - hit.AlongPixels),
            DistanceToEndPixels = endDistance,
        };
        return true;
    }

    public static MapPoint PointAlong(IReadOnlyList<MapPoint> route, MapBounds bounds, double distance)
    {
        if (route.Count == 0)
            return default;
        if (route.Count == 1 || bounds.IsEmpty || distance <= 0)
            return route[0];

        double walked = 0;
        for (int i = 0; i < route.Count - 1; i++)
        {
            double length = Distance(bounds, route[i], route[i + 1]);
            if (length <= 0.0001)
                continue;
            if (walked + length >= distance)
                return MapPoint.Lerp(route[i], route[i + 1], (distance - walked) / length);
            walked += length;
        }

        return route[^1];
    }

    /// <summary>
    /// Положение точки относительно отрезка. Along — расстояние вдоль линии от начала,
    /// Perpendicular — боковое отклонение от бесконечной линии (плюс — правее, ось Y вниз).
    /// </summary>
    public static (double Along, double Perpendicular, double Length) LineOffset(MapBounds bounds, MapPoint from, MapPoint to, MapPoint position)
    {
        var (ax, ay) = bounds.ToPixel(from);
        var (bx, by) = bounds.ToPixel(to);
        var (px, py) = bounds.ToPixel(position);
        double abx = bx - ax;
        double aby = by - ay;
        double length = Math.Sqrt(abx * abx + aby * aby);
        if (length <= 0.0001)
            return (0, 0, 0);

        double apx = px - ax;
        double apy = py - ay;
        double along = (apx * abx + apy * aby) / length;
        double perpendicular = (abx * apy - aby * apx) / length;
        return (along, perpendicular, length);
    }

    public static double Distance(MapBounds bounds, MapPoint from, MapPoint to)
    {
        var (x0, y0) = bounds.ToPixel(from);
        var (x1, y1) = bounds.ToPixel(to);
        double dx = x1 - x0;
        double dy = y1 - y0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
