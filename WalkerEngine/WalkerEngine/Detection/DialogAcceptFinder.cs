namespace WalkerEngine;

/// <summary>
/// Зелёная галочка на кнопке принятия диалога.
/// Ищет вне миникарты, в верхней части клиента — там обычно окно квеста.
/// </summary>
public static class DialogAcceptFinder
{
    public const int MinPixels = 18;

    public const int MaxPixels = 220;

    public const int MinSide = 6;

    public const int MaxSide = 36;

    public static bool TryFind(ColorImage image, MapBounds excluded, out int x, out int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        x = 0;
        y = 0;
        int best = 0;
        int endY = Math.Max(1, image.Height * 65 / 100);
        var seen = new bool[image.Width * image.Height];
        var queue = new Queue<int>();
        for (int py = 40; py < endY; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                int index = py * image.Width + px;
                if (seen[index] || Inside(excluded, px, py) || !IsAccept(image[px, py]))
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
                            if (nx < 0 || ny < 40 || nx >= image.Width || ny >= endY)
                                continue;
                            int next = ny * image.Width + nx;
                            if (seen[next] || Inside(excluded, nx, ny) || !IsAccept(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                if (count < MinPixels || count > MaxPixels)
                    continue;
                if (sideX < MinSide || sideY < MinSide || sideX > MaxSide || sideY > MaxSide)
                    continue;
                if (count <= best)
                    continue;

                best = count;
                x = (int)Math.Round(sumX / count);
                y = (int)Math.Round(sumY / count);
            }
        }

        return best > 0;
    }

    private static bool Inside(MapBounds bounds, int x, int y)
        => !bounds.IsEmpty && x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom;

    private static bool IsAccept(Rgba32 color)
        => color.G >= 130
           && color.G >= color.R + 35
           && color.G >= color.B + 35
           && color.R <= 130
           && color.B <= 120;
}
