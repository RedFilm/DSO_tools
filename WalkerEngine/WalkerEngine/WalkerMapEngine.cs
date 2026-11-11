namespace WalkerEngine;

/// <summary>
/// Графический трекер персонажа. Положение читается по цветам кадра и хранится
/// в долях карты, поэтому масштаб картинки не сдвигает координату.
/// Тот же таймлайн потом принимает реальное перемещение через <see cref="Report"/> и <see cref="TryMove"/>.
/// </summary>
public sealed class WalkerMapEngine
{
    private readonly ColorProfile _colors;
    private readonly PositionTimeline _timeline = new();
    private MapScan? _map;
    private ColorImage? _source;

    public WalkerMapEngine(ColorProfile? colors = null)
    {
        _colors = colors ?? ColorProfile.GreenMarker;
    }

    public int SampleCount => _timeline.Count;

    public DateTimeOffset? LatestTime => _timeline.LatestTime;

    public MapPoint? LatestPosition => _timeline.Count == 0 ? null : _timeline.LatestPoint;

    /// <summary>Скорость последнего участка трека, в долях карты за секунду.</summary>
    public MapVector LatestVelocity => _timeline.LatestVelocity;

    public bool HasMap => _map?.HasMap == true;

    public MapBounds MapBounds => _map?.Bounds ?? MapBounds.Empty;

    public MapObservation? LastObservation { get; private set; }

    /// <summary>
    /// Читает кадр: находит цветную область карты, зелёную точку и записывает её положение на этот момент.
    /// Повторный вызов на кадре другого размера обновляет и проходимость, и позицию.
    /// </summary>
    public MapObservation Observe(ColorImage frame, DateTimeOffset? time = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var stamp = time ?? DateTimeOffset.UtcNow;
        Adopt(frame);

        var scan = _map!;
        if (!scan.PlayerFound)
        {
            var missing = new MapObservation(
                stamp,
                scan.HasMap,
                false,
                default,
                0,
                0,
                scan.Bounds,
                0,
                frame.Width,
                frame.Height);
            LastObservation = missing;
            return missing;
        }

        var bounds = scan.HasMap
            ? scan.Bounds
            : new MapBounds(0, 0, frame.Width - 1, frame.Height - 1);
        var position = bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
        _timeline.Add(stamp, position);

        var observation = new MapObservation(
            stamp,
            scan.HasMap,
            true,
            position,
            scan.PlayerX,
            scan.PlayerY,
            bounds,
            scan.PlayerPixels,
            frame.Width,
            frame.Height);
        LastObservation = observation;
        return observation;
    }

    /// <summary>
    /// Обновляет цветную карту и её масштаб, не трогая уже известную позицию персонажа.
    /// Нужно, когда картинка увеличилась, а точка дальше живёт в симуляции перемещения.
    /// </summary>
    public void UpdateMap(ColorImage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Adopt(frame);
    }

    /// <summary>Записывает положение из внешней симуляции в ту же систему координат, что и цветной трекер.</summary>
    public void Report(MapPoint position, DateTimeOffset? time = null)
        => _timeline.Add(time ?? DateTimeOffset.UtcNow, position);

    /// <summary>
    /// Где персонаж был, есть или останется между уже известными замерами.
    /// До первого замера возвращается первое положение, после последнего — последнее, без угадывания.
    /// </summary>
    public MapPoint PositionAt(DateTimeOffset time) => _timeline.At(time);

    /// <summary>
    /// То же, что <see cref="PositionAt"/>, но после последнего замера продолжает движение с последней скоростью.
    /// </summary>
    public MapPoint Predict(DateTimeOffset time) => _timeline.Predict(time);

    /// <summary>Забывает замеры старше указанного времени. Последний актуальный участок трека остаётся.</summary>
    public void ForgetBefore(DateTimeOffset time) => _timeline.ForgetBefore(time);

    /// <summary>Лежит ли точка на цветной карте последнего кадра. Чёрный фон и внутренние дыры — нет.</summary>
    public bool IsWalkable(MapPoint position)
    {
        if (_map is null)
            throw new InvalidOperationException("Карта ещё не прочитана. Сначала вызовите Observe.");
        return _map.IsWalkable(position);
    }

    /// <summary>
    /// Шаг реального перемещения в долях карты. Если прямой шаг упирается в чёрную пустоту,
    /// пробует сдвиг только по одной оси. Непроходимый шаг оставляет персонажа на месте.
    /// </summary>
    public bool TryMove(MapVector delta, DateTimeOffset time, out MapPoint position)
    {
        if (_map is null || !_map.HasMap)
            throw new InvalidOperationException("Карта ещё не прочитана. Сначала вызовите Observe.");
        if (_timeline.Count == 0)
            throw new InvalidOperationException("Позиция ещё неизвестна.");
        if (_timeline.LatestTime is { } latest && time < latest)
            throw new ArgumentOutOfRangeException(nameof(time), "Время шага раньше последнего наблюдения.");
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y))
            throw new ArgumentOutOfRangeException(nameof(delta), "Смещение должно быть конечным числом.");

        var current = _timeline.At(time);
        if (delta.X == 0 && delta.Y == 0)
        {
            _timeline.Add(time, current);
            position = current;
            return false;
        }

        var next = current.Offset(delta);
        MapPoint accepted;
        bool moved;
        if (_map.IsWalkable(next))
        {
            accepted = next;
            moved = true;
        }
        else if (delta.X != 0 && _map.IsWalkable(new MapPoint(next.X, current.Y)))
        {
            accepted = new MapPoint(next.X, current.Y);
            moved = true;
        }
        else if (delta.Y != 0 && _map.IsWalkable(new MapPoint(current.X, next.Y)))
        {
            accepted = new MapPoint(current.X, next.Y);
            moved = true;
        }
        else
        {
            accepted = current;
            moved = false;
        }

        _timeline.Add(time, accepted);
        position = accepted;
        return moved;
    }

    /// <summary>
    /// Копия последнего кадра с пурпурной границей карты и жёлтым прицелом в положении на указанный момент.
    /// </summary>
    public ColorImage DrawOverlay(DateTimeOffset? time = null)
    {
        if (_source is null || _map is null)
            throw new InvalidOperationException("Нет кадра. Сначала вызовите Observe.");

        MapPoint? point = _timeline.Count == 0
            ? null
            : _timeline.At(time ?? _timeline.LatestTime!.Value);
        return DetectionOverlay.Draw(_source, _map.Bounds, _map.HasMap, point);
    }

    /// <summary>
    /// Рисует зелёную точку персонажа на переданном кадре в положении на указанный момент.
    /// Старый маркер, если он уже нарисован на картинке, не стирается.
    /// Кадр должен быть того же масштаба, что и последняя прочитанная карта.
    /// </summary>
    public void PaintPlayer(ColorImage target, DateTimeOffset time, int radius = 4)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_map is null || !_map.HasMap)
            throw new InvalidOperationException("Нет карты, относительно которой рисовать точку. Сначала вызовите Observe.");

        var (pixelX, pixelY) = _map.Bounds.ToPixel(_timeline.At(time));
        DetectionOverlay.PaintDisc(
            target,
            (int)Math.Floor(pixelX + 0.5),
            (int)Math.Floor(pixelY + 0.5),
            radius,
            Rgba32.Opaque(96, 220, 80));
    }

    private void Adopt(ColorImage frame)
    {
        _map = MapScanner.Scan(frame, _colors);
        _source = frame.Clone();
    }
}
