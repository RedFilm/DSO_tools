namespace WalkerEngine;

/// <summary>Центр бирюзового свечения портала на экране, не на миникарте.</summary>
public readonly record struct PortalMark(int X, int Y, int Pixels);

/// <summary>
/// Ищет портал в кадре клиента по бирюзовому свечению.
/// Значки миникарты в этот кадр не входят: их прямоугольник исключается.
/// </summary>
public static class PortalFinder
{
    public const int MinPixels = 48;

    public const int MinSide = 18;

    public const int MaxSide = 160;

    public static List<PortalMark> FindAll(ColorImage image, MapBounds excluded)
    {
        ArgumentNullException.ThrowIfNull(image);
        var result = new List<PortalMark>();
        if (image.Width < 8 || image.Height < 8)
            return result;

        int top = 70;
        int bottom = image.Height - 130;
        if (bottom <= top)
            return result;

        var seen = new bool[image.Width * image.Height];
        var queue = new Queue<int>();
        for (int py = top; py <= bottom; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                int index = py * image.Width + px;
                if (seen[index] || Inside(excluded, px, py) || !IsPortal(image[px, py]))
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
                            if (nx < 0 || nx >= image.Width || ny < top || ny > bottom)
                                continue;
                            int next = ny * image.Width + nx;
                            if (seen[next] || Inside(excluded, nx, ny) || !IsPortal(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                int side = Math.Max(sideX, sideY);
                if (count < MinPixels || sideX < MinSide || sideY < MinSide || side > MaxSide)
                    continue;

                result.Add(new PortalMark((int)Math.Round(sumX / count), (int)Math.Round(sumY / count), count));
            }
        }

        result.Sort(static (left, right) => right.Pixels.CompareTo(left.Pixels));
        return result;
    }

    private static bool Inside(MapBounds bounds, int x, int y)
        => !bounds.IsEmpty && x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom;

    private static bool IsPortal(Rgba32 color)
        => color.B >= 90
           && color.G >= 90
           && color.R <= 80
           && color.B + color.G >= color.R + 140
           && Math.Abs(color.B - color.G) <= 40;
}
