namespace WalkerEngine;

/// <summary>Настройки коррекции. Пиксели — пиксели цветной карты, не экрана, кроме дальности клика.</summary>
public sealed class PilotTuning
{
    public double LookAheadPixels { get; init; } = 80;

    public double RejoinLookAheadPixels { get; init; } = 28;

    public double CorrectionDistancePixels { get; init; } = 16;

    public double ArrivalDistancePixels { get; init; } = 10;

    /// <summary>Радиус последней точки. Меньше нуля — как у остальных точек маршрута.</summary>
    public double FinalArrivalDistancePixels { get; init; } = -1;

    public double StuckMovePixels { get; init; } = 2;

    public int StuckSampleCount { get; init; } = 3;

    public double HeadingToleranceDegrees { get; init; } = 55;

    public double ClickDistancePixels { get; init; } = 180;

    public bool InvertX { get; init; }

    public bool InvertY { get; init; }

    /// <summary>Поворот направления карты относительно экрана, по часовой, в градусах.</summary>
    public double RotationDegrees { get; init; }
}

/// <summary>Куда кликнуть относительно персонажа, чтобы вернуться на маршрут или продолжить его.</summary>
public readonly record struct PilotCommand(
    bool Click,
    double OffsetX,
    double OffsetY,
    double HeadingDegrees,
    double CrossTrackPixels,
    double RemainingPixels,
    bool Correcting,
    bool Stuck,
    bool Arrived,
    string Reason,
    int SegmentIndex = -1);

/// <summary>
/// Ведёт маркер по точкам маршрута вперёд. При старте цель — ближайшая точка впереди,
/// а не начало пути. Дальше клик смотрит только на текущую точку.
/// Точка засчитана, когда маркер вошёл в радиус прибытия или прошёл её в коридоре этого отрезка.
/// </summary>
public sealed class RoutePilot
{
    private readonly PilotTuning _tuning;
    private MapPoint _previous;
    private bool _hasPrevious;
    private int _stillSamples;
    private int _checkpoint = -1;

    public RoutePilot(PilotTuning? tuning = null)
    {
        _tuning = tuning ?? new PilotTuning();
    }

    public void Reset()
    {
        _hasPrevious = false;
        _stillSamples = 0;
        _checkpoint = -1;
    }

    /// <summary>Сбросить только учёт движения (после увода из застревания), checkpoint сохранить.</summary>
    public void ClearMotion()
    {
        _hasPrevious = false;
        _stillSamples = 0;
    }

    /// <summary>
    /// Продолжить с известной точки маршрута, без поиска ближайшей проекции
    /// (иначе после сдвига можно перепрыгнуть вперёд по петле пути).
    /// </summary>
    public void ResumeAtCheckpoint(int checkpoint)
    {
        _hasPrevious = false;
        _stillSamples = 0;
        _checkpoint = Math.Max(0, checkpoint);
    }

    public PilotCommand Advance(bool markerFound, MapPoint position, MapBounds bounds, IReadOnlyList<MapPoint> route)
    {
        if (!markerFound)
            return Idle("Маркер на карте не найден.");

        if (route.Count < 2 || bounds.IsEmpty)
            return Idle("Маршрут или границы карты ещё не готовы.");

        bool hasPrevious = _hasPrevious;
        MapPoint previous = _previous;
        bool stuck = UpdateMotion(position, bounds);
        EnsureCheckpoint(route, bounds, position);
        while (_checkpoint < route.Count && Reached(route, bounds, _checkpoint, position))
            _checkpoint++;

        double remaining = Remaining(route, bounds, position);
        int segment = _checkpoint > 0 && _checkpoint < route.Count ? _checkpoint - 1 : -1;
        if (_checkpoint >= route.Count)
        {
            return new PilotCommand(
                false, 0, 0, 0,
                0, 0,
                false, stuck, true,
                "Персонаж на финальной точке.");
        }

        int leg = Math.Max(0, _checkpoint - 1);
        double cross = _checkpoint == 0
            ? 0
            : PathCourse.LineOffset(bounds, route[leg], route[_checkpoint], position).Perpendicular;
        bool wrongHeading = hasPrevious && WrongHeading(previous, position, bounds, route, leg);
        bool offCourse = Math.Abs(cross) >= _tuning.CorrectionDistancePixels;
        bool correcting = offCourse || wrongHeading;

        var (offsetX, offsetY) = ScreenOffset(position, route[_checkpoint], bounds);
        double heading = (Math.Atan2(offsetY, offsetX) * 180 / Math.PI + 360) % 360;
        if (Math.Abs(offsetX) < 0.5 && Math.Abs(offsetY) < 0.5)
        {
            return new PilotCommand(
                false, 0, 0, heading,
                cross, remaining,
                correcting, stuck, false,
                "Направление клика слишком короткое.",
                segment);
        }

        return new PilotCommand(
            true, offsetX, offsetY, heading,
            cross, remaining,
            correcting, stuck, false,
            Reason(stuck, offCourse, wrongHeading),
            segment);
    }

    private void EnsureCheckpoint(IReadOnlyList<MapPoint> route, MapBounds bounds, MapPoint position)
    {
        if (_checkpoint >= 0)
            return;

        if (!PathCourse.TryProject(route, bounds, position, out PathHit hit))
        {
            _checkpoint = Math.Min(1, route.Count - 1);
            return;
        }

        _checkpoint = hit.SegmentIndex + 1;
        if (hit.SegmentIndex == 0 && AheadOf(bounds, route[0], route[1], position) < 0)
            _checkpoint = 0;
    }

    private static double AheadOf(MapBounds bounds, MapPoint from, MapPoint to, MapPoint position)
    {
        var (along, _, length) = PathCourse.LineOffset(bounds, from, to, position);
        if (length <= 0.0001)
            return 0;
        return along;
    }

    private bool Reached(IReadOnlyList<MapPoint> route, MapBounds bounds, int index, MapPoint position)
    {
        bool exactFinish = index == route.Count - 1 && _tuning.FinalArrivalDistancePixels >= 0;
        double radius = Math.Max(1, exactFinish ? _tuning.FinalArrivalDistancePixels : _tuning.ArrivalDistancePixels);
        if (PathCourse.Distance(bounds, position, route[index]) <= radius)
            return true;
        if (index <= 0 || exactFinish)
            return false;

        var (along, perpendicular, length) = PathCourse.LineOffset(bounds, route[index - 1], route[index], position);
        double corridor = Math.Max(radius, _tuning.CorrectionDistancePixels);
        return length > 0.0001 && along >= length && Math.Abs(perpendicular) <= corridor;
    }

    private double Remaining(IReadOnlyList<MapPoint> route, MapBounds bounds, MapPoint position)
    {
        if (_checkpoint < 0 || _checkpoint >= route.Count)
            return 0;

        double left = PathCourse.Distance(bounds, position, route[_checkpoint]);
        for (int i = _checkpoint; i < route.Count - 1; i++)
            left += PathCourse.Distance(bounds, route[i], route[i + 1]);
        return left;
    }

    private bool UpdateMotion(MapPoint position, MapBounds bounds)
    {
        if (!_hasPrevious)
        {
            _previous = position;
            _hasPrevious = true;
            _stillSamples = 0;
            return false;
        }

        double moved = PathCourse.Distance(bounds, _previous, position);
        _previous = position;
        if (moved <= Math.Max(0, _tuning.StuckMovePixels))
            _stillSamples++;
        else
            _stillSamples = 0;

        return _stillSamples >= Math.Max(1, _tuning.StuckSampleCount);
    }

    private bool WrongHeading(MapPoint previous, MapPoint position, MapBounds bounds, IReadOnlyList<MapPoint> route, int leg)
    {
        double moved = PathCourse.Distance(bounds, previous, position);
        if (moved <= Math.Max(0, _tuning.StuckMovePixels))
            return false;

        int segment = Math.Clamp(leg, 0, route.Count - 2);
        var (x0, y0) = bounds.ToPixel(previous);
        var (x1, y1) = bounds.ToPixel(position);
        var (ax, ay) = bounds.ToPixel(route[segment]);
        var (bx, by) = bounds.ToPixel(route[segment + 1]);
        double actual = Math.Atan2(y1 - y0, x1 - x0);
        double desired = Math.Atan2(by - ay, bx - ax);
        double delta = Math.Abs(Normalize(actual - desired)) * 180 / Math.PI;
        return delta >= _tuning.HeadingToleranceDegrees;
    }

    private (double X, double Y) ScreenOffset(MapPoint from, MapPoint to, MapBounds bounds)
    {
        var (x0, y0) = bounds.ToPixel(from);
        var (x1, y1) = bounds.ToPixel(to);
        double dx = x1 - x0;
        double dy = y1 - y0;
        double radians = _tuning.RotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double x = dx * cos - dy * sin;
        double y = dx * sin + dy * cos;
        if (_tuning.InvertX)
            x = -x;
        if (_tuning.InvertY)
            y = -y;

        double length = Math.Sqrt(x * x + y * y);
        if (length < 0.001)
            return (0, 0);

        double click = Math.Max(1, _tuning.ClickDistancePixels);
        return (x / length * click, y / length * click);
    }

    private static PilotCommand Idle(string reason)
        => new(false, 0, 0, 0, 0, 0, false, false, false, reason);

    private static string Reason(bool stuck, bool offCourse, bool wrongHeading)
    {
        if (stuck && (offCourse || wrongHeading))
            return "Персонаж стоит и сбился с пути. Клик к следующей точке.";
        if (stuck)
            return "Персонаж остановился. Повтор клика к следующей точке.";
        if (offCourse)
            return "Персонаж сбоку от пути. Клик к следующей точке.";
        if (wrongHeading)
            return "Персонаж идёт не в ту сторону. Клик к следующей точке.";
        return "Движение к следующей точке маршрута.";
    }

    private static double Normalize(double radians)
    {
        double turn = radians % (Math.PI * 2);
        if (turn > Math.PI)
            turn -= Math.PI * 2;
        if (turn < -Math.PI)
            turn += Math.PI * 2;
        return turn;
    }
}
