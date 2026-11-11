namespace WalkerEngine;

/// <summary>Один значок сундука: центр пятна и сколько синих пикселей в него вошло.</summary>
public readonly record struct ChestMark(MapPoint Point, int Pixels);

/// <summary>
/// Ищет на кадре синий значок сундука.
/// Сундук крупнее круглых синих меток. Арка того же цвета отсекается по просвету между стойками.
/// </summary>
public static class ChestFinder
{
    public const int MinPixels = 100;

    public const int MinSide = 13;

    public const int MaxSide = 36;

    /// <summary>Доля пустых клеток внутри контура ряда. У сундука просвет меньше, у арки больше.</summary>
    public const int MaxGapPercent = 20;

    /// <summary>Все подходящие значки, от больших к меньшим. Точка — центр пятна.</summary>
    public static List<ChestMark> FindAll(ColorImage image, MapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(image);
        var result = new List<ChestMark>();
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
                if (seen[seenIndex] || !IsChest(image[x, y]))
                    continue;

                queue.Clear();
                queue.Enqueue((x, y));
                seen[seenIndex] = true;
                var points = new List<(int X, int Y)>();
                int minX = x;
                int maxX = x;
                int minY = y;
                int maxY = y;
                double sumX = 0;
                double sumY = 0;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    points.Add((cx, cy));
                    sumX += cx;
                    sumY += cy;
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
                            if (seen[next] || !IsChest(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                int count = points.Count;
                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                int side = Math.Max(sideX, sideY);
                if (count < MinPixels || sideX < MinSide || sideY < MinSide || side > MaxSide)
                    continue;
                if (!IsSolid(points, minX, minY, sideY))
                    continue;

                result.Add(new ChestMark(bounds.ToNormalized(sumX / count, sumY / count), count));
            }
        }

        result.Sort(static (left, right) => right.Pixels.CompareTo(left.Pixels));
        return result;
    }

    /// <summary>
    /// Сколько синих пикселей сундука осталось вокруг точки.
    /// Зелёная точка персонажа разбивает значок, и он выпадает из <see cref="FindAll"/>, хотя сундук ещё на карте.
    /// </summary>
    public static int CountNear(ColorImage image, MapBounds bounds, MapPoint point, int radius)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (bounds.IsEmpty || radius < 1)
            return 0;

        var (px, py) = bounds.ToPixel(point);
        int centerX = (int)Math.Round(px);
        int centerY = (int)Math.Round(py);
        int radiusSquared = radius * radius;
        int left = Math.Max(Math.Max(0, bounds.Left), centerX - radius);
        int top = Math.Max(Math.Max(0, bounds.Top), centerY - radius);
        int right = Math.Min(Math.Min(image.Width - 1, bounds.Right), centerX + radius);
        int bottom = Math.Min(Math.Min(image.Height - 1, bounds.Bottom), centerY + radius);
        int count = 0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy > radiusSquared || !IsChest(image[x, y]))
                    continue;
                count++;
            }
        }

        return count;
    }

    private static bool IsSolid(List<(int X, int Y)> points, int minX, int minY, int sideY)
    {
        var count = new int[sideY];
        var first = new int[sideY];
        var last = new int[sideY];
        Array.Fill(first, int.MaxValue);
        foreach ((int x, int y) in points)
        {
            int row = y - minY;
            int col = x - minX;
            count[row]++;
            if (col < first[row])
                first[row] = col;
            if (col > last[row])
                last[row] = col;
        }

        int holes = 0;
        int span = 0;
        for (int row = 0; row < sideY; row++)
        {
            if (last[row] < first[row])
                continue;
            int width = last[row] - first[row] + 1;
            span += width;
            holes += width - count[row];
        }

        return span > 0 && holes * 100 <= span * MaxGapPercent;
    }

    private static bool IsChest(Rgba32 color)
        => color.B >= 160
           && color.R <= 120
           && color.G >= 50
           && color.B >= color.R + 70
           && color.B >= color.G + 20;
}
