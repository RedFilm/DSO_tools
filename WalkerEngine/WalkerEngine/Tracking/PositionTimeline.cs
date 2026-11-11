namespace WalkerEngine;

/// <summary>
/// История положений. Между наблюдениями точка движется равномерно,
/// поэтому координату можно спросить в любой момент, а не только в кадр замера.
/// </summary>
internal sealed class PositionTimeline
{
    private readonly List<Sample> _samples = [];

    public int Count => _samples.Count;

    public DateTimeOffset? LatestTime => _samples.Count == 0 ? null : _samples[^1].Time;

    public MapPoint LatestPoint => _samples.Count == 0
        ? throw new InvalidOperationException("Позиция ещё неизвестна: нет наблюдений.")
        : _samples[^1].Point;

    public MapVector LatestVelocity
    {
        get
        {
            if (_samples.Count < 2)
                return MapVector.Zero;

            var previous = _samples[^2];
            var current = _samples[^1];
            double seconds = (current.Time - previous.Time).TotalSeconds;
            if (seconds <= 0)
                return MapVector.Zero;

            return new MapVector(
                (current.Point.X - previous.Point.X) / seconds,
                (current.Point.Y - previous.Point.Y) / seconds);
        }
    }

    public void Add(DateTimeOffset time, MapPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point), "Координата должна быть конечным числом.");

        int index = Search(time);
        var sample = new Sample(time, point);
        if (index >= 0)
            _samples[index] = sample;
        else
            _samples.Insert(~index, sample);
    }

    public MapPoint At(DateTimeOffset time)
    {
        if (_samples.Count == 0)
            throw new InvalidOperationException("Позиция ещё неизвестна: нет наблюдений.");
        if (time <= _samples[0].Time)
            return _samples[0].Point;
        if (time >= _samples[^1].Time)
            return _samples[^1].Point;

        int upper = Search(time);
        if (upper >= 0)
            return _samples[upper].Point;

        upper = ~upper;
        var before = _samples[upper - 1];
        var after = _samples[upper];
        double span = (after.Time - before.Time).TotalSeconds;
        double t = span <= 0 ? 0 : (time - before.Time).TotalSeconds / span;
        return MapPoint.Lerp(before.Point, after.Point, t);
    }

    public MapPoint Predict(DateTimeOffset time)
    {
        if (_samples.Count == 0)
            throw new InvalidOperationException("Позиция ещё неизвестна: нет наблюдений.");

        var last = _samples[^1];
        if (time <= last.Time)
            return At(time);

        var velocity = LatestVelocity;
        double seconds = (time - last.Time).TotalSeconds;
        return new MapPoint(last.Point.X + velocity.X * seconds, last.Point.Y + velocity.Y * seconds);
    }

    public void ForgetBefore(DateTimeOffset time)
    {
        int index = Search(time);
        int removeCount = index >= 0 ? index : ~index;
        if (removeCount > 0)
            _samples.RemoveRange(0, removeCount);
    }

    private int Search(DateTimeOffset time)
    {
        int lo = 0;
        int hi = _samples.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int comparison = _samples[mid].Time.CompareTo(time);
            if (comparison == 0)
                return mid;
            if (comparison < 0)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        return ~lo;
    }

    private readonly record struct Sample(DateTimeOffset Time, MapPoint Point);
}
