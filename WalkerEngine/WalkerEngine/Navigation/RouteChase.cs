namespace WalkerEngine;

/// <summary>
/// Собирает путь по уже проставленному маршруту от персонажа к цели.
/// Сначала идёт вдоль ломаной до ближайшего к цели места, затем последняя точка — сам значок.
/// </summary>
public static class RouteChase
{
    public static List<MapPoint> Toward(IReadOnlyList<MapPoint> route, MapBounds bounds, MapPoint from, MapPoint target)
    {
        var path = new List<MapPoint>();
        if (route.Count < 2 || bounds.IsEmpty
            || !PathCourse.TryProject(route, bounds, from, out PathHit start)
            || !PathCourse.TryProject(route, bounds, target, out PathHit end))
        {
            Add(path, from, bounds);
            Add(path, target, bounds);
            return path;
        }

        Add(path, start.Point, bounds);
        if (end.AlongPixels + 1 >= start.AlongPixels)
        {
            for (int i = start.SegmentIndex + 1; i <= end.SegmentIndex; i++)
                Add(path, route[i], bounds);
        }
        else
        {
            for (int i = start.SegmentIndex; i > end.SegmentIndex; i--)
                Add(path, route[i], bounds);
        }

        Add(path, end.Point, bounds);
        Add(path, target, bounds);
        return path;
    }

    private static void Add(List<MapPoint> path, MapPoint point, MapBounds bounds)
    {
        if (path.Count == 0)
        {
            path.Add(point);
            return;
        }

        if (bounds.IsEmpty || PathCourse.Distance(bounds, path[^1], point) >= 2)
            path.Add(point);
        else
            path[^1] = point;
    }
}
