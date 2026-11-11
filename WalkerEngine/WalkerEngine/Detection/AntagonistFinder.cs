namespace WalkerEngine;

/// <summary>Один значок антагониста: центр пятна и сколько красных пикселей в него вошло.</summary>
public readonly record struct AntagonistMark(MapPoint Point, int Pixels);

/// <summary>
/// Ищет красные значки врагов на миникарте.
/// Отсекает артефакты мира, просвечивающие через прозрачную карту: они лежат в чёрной пустоте
/// вне серого пола и часто вытянуты / «грязные» по цвету.
/// </summary>
public static class AntagonistFinder
{
    public const int MinPixels = 25;

    public const int MinSide = 2;

    /// <summary>Значок черепа компактный; длинные красные полосы — обычно bleed-through.</summary>
    public const int MaxSide = 26;

    public const double MaxAspect = 2.15;

    /// <summary>Доля пикселей пятна, которые должны лежать на маске карты (не в пустоте).</summary>
    public const double MinOnMapRatio = 0.35;

    public static bool TryFind(ColorImage image, MapBounds bounds, out MapPoint point)
    {
        List<AntagonistMark> marks = FindAll(image, bounds, scan: null);
        if (marks.Count == 0)
        {
            point = default;
            return false;
        }

        point = marks[0].Point;
        return true;
    }

    public static bool TryFind(ColorImage image, MapScan scan, out MapPoint point)
    {
        List<AntagonistMark> marks = FindAll(image, scan);
        if (marks.Count == 0)
        {
            point = default;
            return false;
        }

        point = marks[0].Point;
        return true;
    }

    public static List<AntagonistMark> FindAll(ColorImage image, MapScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (!scan.HasMap)
            return [];
        return FindAll(image, scan.Bounds, scan);
    }

    /// <summary>Все подходящие значки, от больших к меньшим. Точка — центр пятна.</summary>
    public static List<AntagonistMark> FindAll(ColorImage image, MapBounds bounds, MapScan? scan = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var result = new List<AntagonistMark>();
        if (bounds.IsEmpty)
            return result;

        int left = Math.Clamp(bounds.Left, 0, image.Width - 1);
        int top = Math.Clamp(bounds.Top, 0, image.Height - 1);
        int right = Math.Clamp(bounds.Right, 0, image.Width - 1);
        int bottom = Math.Clamp(bounds.Bottom, 0, image.Height - 1);
        int width = right - left + 1;
        int height = bottom - top + 1;
        if (width < MinSide || height < MinSide)
            return result;

        var seen = new bool[width * height];
        var queue = new Queue<(int X, int Y)>();
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                int seenIndex = (y - top) * width + (x - left);
                if (seen[seenIndex] || !IsRed(image[x, y]))
                    continue;

                queue.Clear();
                queue.Enqueue((x, y));
                seen[seenIndex] = true;
                int count = 0;
                int onMap = 0;
                int minX = x;
                int maxX = x;
                int minY = y;
                int maxY = y;
                double sumX = 0;
                double sumY = 0;
                int sumR = 0;
                int sumG = 0;
                int sumB = 0;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    count++;
                    sumX += cx;
                    sumY += cy;
                    Rgba32 color = image[cx, cy];
                    sumR += color.R;
                    sumG += color.G;
                    sumB += color.B;
                    if (scan is null || scan.ContainsPixel(cx, cy))
                        onMap++;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (nx < left || nx > right || ny < top || ny > bottom)
                                continue;
                            int next = (ny - top) * width + (nx - left);
                            if (seen[next] || !IsRed(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                if (count < MinPixels || sideX < MinSide || sideY < MinSide)
                    continue;
                if (sideX > MaxSide || sideY > MaxSide)
                    continue;
                double aspect = Math.Max(sideX, sideY) / (double)Math.Min(sideX, sideY);
                if (aspect > MaxAspect)
                    continue;

                // Артефакты мира: красное в чёрной пустоте вне серого пола карты.
                if (scan is not null && onMap < count * MinOnMapRatio)
                    continue;

                // «Грязный» красный bleed (высокий синий/зелёный) — не UI-череп.
                int avgR = sumR / count;
                int avgG = sumG / count;
                int avgB = sumB / count;
                if (avgB >= 35 && avgB + 15 >= avgG && avgR < avgB + 75)
                    continue;

                result.Add(new AntagonistMark(bounds.ToNormalized(sumX / count, sumY / count), count));
            }
        }

        result.Sort(static (leftMark, rightMark) => rightMark.Pixels.CompareTo(leftMark.Pixels));
        return result;
    }

    private static bool IsRed(Rgba32 color)
        => color.R >= 100
           && color.G <= 80
           && color.B <= 50
           && color.R >= color.G + 40
           && color.R >= color.B + 50;
}
