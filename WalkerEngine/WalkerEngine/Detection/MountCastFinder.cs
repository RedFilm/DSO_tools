namespace WalkerEngine;

/// <summary>
/// Голубая полоска прогресса посадки на коня в центре клиентской области.
/// </summary>
public static class MountCastFinder
{
    public const int MinWidth = 24;

    public const int MaxWidth = 220;

    public const int MinHeight = 3;

    public const int MaxHeight = 24;

    public const int MinPixels = 28;

    public static bool IsVisible(ColorImage image, MapBounds excluded)
        => TryFind(image, excluded, out _, out _, out _, out _);

    public static bool TryFind(ColorImage image, MapBounds excluded, out int x, out int y, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(image);
        x = 0;
        y = 0;
        width = 0;
        height = 0;

        int left = image.Width * 28 / 100;
        int right = image.Width * 72 / 100;
        int top = image.Height * 42 / 100;
        int bottom = image.Height * 68 / 100;
        if (right - left < MinWidth || bottom - top < MinHeight)
            return false;

        var seen = new bool[image.Width * image.Height];
        var queue = new Queue<int>();
        int best = 0;
        int bestX = 0;
        int bestY = 0;
        int bestW = 0;
        int bestH = 0;

        for (int py = top; py < bottom; py++)
        {
            for (int px = left; px < right; px++)
            {
                int index = py * image.Width + px;
                if (seen[index] || Inside(excluded, px, py) || !IsCastBar(image[px, py]))
                    continue;

                queue.Clear();
                queue.Enqueue(index);
                seen[index] = true;
                int count = 0;
                int minX = px;
                int maxX = px;
                int minY = py;
                int maxY = py;
                double sumX = 0;
                double sumY = 0;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int cx = current % image.Width;
                    int cy = current / image.Width;
                    count++;
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
                            if (nx < left || ny < top || nx >= right || ny >= bottom)
                                continue;
                            int next = ny * image.Width + nx;
                            if (seen[next] || Inside(excluded, nx, ny) || !IsCastBar(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                if (count < MinPixels)
                    continue;
                if (sideX < MinWidth || sideX > MaxWidth || sideY < MinHeight || sideY > MaxHeight)
                    continue;
                // Полоска горизонтальная: ширина заметно больше высоты.
                if (sideX < sideY * 2)
                    continue;
                if (count <= best)
                    continue;

                best = count;
                bestX = (int)Math.Round(sumX / count);
                bestY = (int)Math.Round(sumY / count);
                bestW = sideX;
                bestH = sideY;
            }
        }

        if (best <= 0)
            return false;

        x = bestX;
        y = bestY;
        width = bestW;
        height = bestH;
        return true;
    }

    private static bool Inside(MapBounds bounds, int x, int y)
        => !bounds.IsEmpty && x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom;

    /// <summary>Ярко-голубой/бирюзовый fill полоски каста (как на скрине посадки).</summary>
    internal static bool IsCastBar(Rgba32 color)
        => color.B >= 180
           && color.G >= 120
           && color.R <= 160
           && color.B >= color.R + 50
           && color.G >= color.R + 20
           && color.B >= color.G + 20;
}
