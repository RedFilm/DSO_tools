namespace WalkerEngine;

/// <summary>
/// Точка на карте в долях её собственного размера.
/// (0, 0) — левый верхний край цветной области, (1, 1) — правый нижний.
/// При масштабе картинки эти числа не меняются.
/// </summary>
public readonly record struct MapPoint(double X, double Y)
{
    public static MapPoint Lerp(MapPoint from, MapPoint to, double t)
        => new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);

    public MapPoint Offset(MapVector delta) => new(X + delta.X, Y + delta.Y);
}

/// <summary>Смещение или скорость в долях карты. Скорость измеряется в долях за секунду.</summary>
public readonly record struct MapVector(double X, double Y)
{
    public static MapVector Zero => default;

    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>Ограничивающий прямоугольник цветной области карты в пикселях кадра.</summary>
public readonly record struct MapBounds(int Left, int Top, int Right, int Bottom)
{
    public static MapBounds Empty => new(0, 0, -1, -1);

    public bool IsEmpty => Right < Left || Bottom < Top;

    public int Width => IsEmpty ? 0 : Right - Left + 1;

    public int Height => IsEmpty ? 0 : Bottom - Top + 1;

    /// <summary>
    /// Переводит индекс пикселя в долю карты. Учитывается центр пикселя,
    /// поэтому одинаковая точка остаётся на месте после целочисленного масштаба.
    /// </summary>
    public MapPoint ToNormalized(double pixelX, double pixelY)
    {
        if (IsEmpty)
            throw new InvalidOperationException("Границы карты пусты.");

        return new MapPoint(
            (pixelX + 0.5 - Left) / Width,
            (pixelY + 0.5 - Top) / Height);
    }

    /// <summary>Обратное преобразование: доля карты → индекс пикселя (может быть дробным).</summary>
    public (double X, double Y) ToPixel(MapPoint point)
        => (Left + point.X * Width - 0.5, Top + point.Y * Height - 0.5);
}

/// <summary>Результат одного взгляда на кадр: нашлась ли карта и где на ней зелёная точка.</summary>
public readonly record struct MapObservation(
    DateTimeOffset Time,
    bool HasMap,
    bool PlayerFound,
    MapPoint Position,
    double PixelX,
    double PixelY,
    MapBounds Bounds,
    int PlayerPixels,
    int FrameWidth,
    int FrameHeight);
