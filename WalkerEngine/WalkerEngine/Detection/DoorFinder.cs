namespace WalkerEngine;

/// <summary>Значок двери на миникарте: узкая арка, выше, чем шире. Круглые синие метки сюда не входят.</summary>
public readonly record struct DoorMark(MapPoint Point, int Pixels);

public static class DoorFinder
{
    public const int MinPixels = 36;

    public const int MaxPixels = 160;

    public const int MinWidth = 3;

    public const int MaxWidth = 12;

    public const int MinHeight = 8;

    public const int MaxHeight = 20;

    public static List<DoorMark> FindAll(ColorImage image, MapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(image);
        var result = new List<DoorMark>();
        if (bounds.IsEmpty)
            return result;

        int left = Math.Clamp(bounds.Left, 0, image.Width - 1);
        int top = Math.Clamp(bounds.Top, 0, image.Height - 1);
        int right = Math.Clamp(bounds.Right, 0, image.Width - 1);
        int bottom = Math.Clamp(bounds.Bottom, 0, image.Height - 1);
        int width = right - left + 1;
        int height = bottom - top + 1;
        if (width < MinWidth || height < MinHeight)
            return result;

        var seen = new bool[width * height];
        var queue = new Queue<(int X, int Y)>();
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                int seenIndex = (y - top) * width + (x - left);
                if (seen[seenIndex] || !IsDoor(image[x, y]))
                    continue;

                queue.Clear();
                queue.Enqueue((x, y));
                seen[seenIndex] = true;
                int count = 0;
                int minX = x;
                int maxX = x;
                int minY = y;
                int maxY = y;
                double sumX = 0;
                double sumY = 0;
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
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
                            if (nx < left || nx > right || ny < top || ny > bottom)
                                continue;
                            int next = (ny - top) * width + (nx - left);
                            if (seen[next] || !IsDoor(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                if (count < MinPixels || count > MaxPixels)
                    continue;
                if (sideX < MinWidth || sideX > MaxWidth || sideY < MinHeight || sideY > MaxHeight)
                    continue;
                if (sideY <= sideX)
                    continue;

                result.Add(new DoorMark(bounds.ToNormalized(sumX / count, sumY / count), count));
            }
        }

        result.Sort(static (left, right) => right.Pixels.CompareTo(left.Pixels));
        return result;
    }

    /// <summary>Всё тело значка, не только яркая полоска. У круглой метки эта полоска узкая, а диск вокруг неё широкий.</summary>
    private static bool IsDoor(Rgba32 color)
        => color.B >= 90
           && color.G >= 50
           && color.B > color.R + 40
           && color.B + 15 >= color.G;
}
