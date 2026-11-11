namespace WalkerEngine;

/// <summary>Вид квестового значка на миникарте.</summary>
public enum QuestMarkKind
{
    Question,
    Exclaim,
}

/// <summary>Один квестовый значок: центр пятна, размер и вид.</summary>
public readonly record struct QuestMark(MapPoint Point, int Pixels, QuestMarkKind Kind);

/// <summary>
/// Ищет на миникарте знаки вопроса и восклицания.
/// Не путает их с синим сундуком и бирюзовой аркой двери: у «?» преобладает зелёный, у «!» — жёлтый.
/// </summary>
public static class QuestMarkFinder
{
    public const int MinPixels = 12;

    public const int MaxPixels = 120;

    public const int MinSide = 4;

    public const int MaxSide = 22;

    public static List<QuestMark> FindAll(ColorImage image, MapBounds bounds, QuestMarkKind kind)
    {
        ArgumentNullException.ThrowIfNull(image);
        var result = new List<QuestMark>();
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
                if (seen[seenIndex] || !Matches(kind, image[x, y]))
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
                            if (seen[next] || !Matches(kind, image[nx, ny]))
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
                if (sideX < MinSide || sideY < MinSide || sideX > MaxSide || sideY > MaxSide)
                    continue;
                if (kind == QuestMarkKind.Exclaim && sideY < sideX)
                    continue;

                result.Add(new QuestMark(bounds.ToNormalized(sumX / count, sumY / count), count, kind));
            }
        }

        result.Sort(static (left, right) => right.Pixels.CompareTo(left.Pixels));
        return result;
    }

    private static bool Matches(QuestMarkKind kind, Rgba32 color)
        => kind == QuestMarkKind.Question ? IsQuestion(color) : IsExclaim(color);

    /// <summary>Зелёно-бирюзовый «?»: зелёный не слабее синего, не похож на сундук (там синий доминирует).</summary>
    private static bool IsQuestion(Rgba32 color)
        => color.G >= 150
           && color.B >= 40
           && color.R <= 120
           && color.G >= color.R + 60
           && color.G + 10 >= color.B
           && color.G >= color.B - 20;

    /// <summary>Жёлтый «!»: красный и зелёный высокие, синий низкий.</summary>
    private static bool IsExclaim(Rgba32 color)
        => color.R >= 160
           && color.G >= 110
           && color.G <= 230
           && color.B <= 90
           && color.R >= color.B + 70
           && color.G >= color.B + 40;
}
