namespace WalkerEngine;

/// <summary>
/// Маркеры лаунчера/клиента DSO: зелёные кнопки «Вход»/«Играть» и синий значок магазина.
/// Ориентируется на цвет и положение, не на текст.
/// </summary>
public static class LauncherUiFinder
{
    public enum GreenButtonKind
    {
        None,
        /// <summary>Форма логина: зелёная кнопка в середине правой колонки.</summary>
        Login,
        /// <summary>Уже вошли: зелёная «Играть» справа сверху.</summary>
        Play,
    }

    public static bool TryFindGreenButton(ColorImage image, out GreenButtonKind kind, out int x, out int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        kind = GreenButtonKind.None;
        x = 0;
        y = 0;
        if (image.Width < 200 || image.Height < 160)
            return false;

        int best = 0;
        int bestX = 0;
        int bestY = 0;
        int bestMinY = 0;
        int bestMaxY = 0;
        int startX = image.Width * 55 / 100;
        var seen = new bool[image.Width * image.Height];
        var queue = new Queue<int>();
        for (int py = 0; py < image.Height; py++)
        {
            for (int px = startX; px < image.Width; px++)
            {
                int index = py * image.Width + px;
                if (seen[index] || !IsLauncherGreen(image[px, py]))
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
                            if (nx < startX || ny < 0 || nx >= image.Width || ny >= image.Height)
                                continue;
                            int next = ny * image.Width + nx;
                            if (seen[next] || !IsLauncherGreen(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                if (count < 400
                    || sideX < 80
                    || sideY < 18
                    || sideY > 90
                    || count <= best)
                {
                    continue;
                }

                best = count;
                bestX = (int)Math.Round(sumX / count);
                bestY = (int)Math.Round(sumY / count);
                bestMinY = minY;
                bestMaxY = maxY;
            }
        }

        if (best <= 0)
            return false;

        x = bestX;
        y = bestY;
        double midY = (bestMinY + bestMaxY) * 0.5 / image.Height;
        // «Играть» у уже залогиненного — в верхней четверти; «Вход» — ниже.
        kind = midY <= 0.28 ? GreenButtonKind.Play : GreenButtonKind.Login;
        return true;
    }

    /// <summary>Синий кружок магазина в левом верхнем углу (выбор персонажа / мир).</summary>
    public static bool HasShopIcon(ColorImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width < 80 || image.Height < 80)
            return false;

        int endX = Math.Min(image.Width, Math.Max(48, image.Width * 12 / 100));
        int endY = Math.Min(image.Height, Math.Max(48, image.Height * 12 / 100));
        int blue = 0;
        for (int py = 0; py < endY; py++)
        {
            for (int px = 0; px < endX; px++)
            {
                if (IsShopBlue(image[px, py]))
                    blue++;
            }
        }

        return blue >= 80;
    }

    /// <summary>Ярко-зелёная кнопка лаунчера (Вход / Играть).</summary>
    public static bool IsLauncherGreen(Rgba32 color)
        => color.G >= 95
           && color.R <= 120
           && color.B <= 80
           && color.G >= color.R + 35
           && color.G >= color.B + 50;

    public static bool IsShopBlue(Rgba32 color)
        => color.B >= 150
           && color.B >= color.R + 40
           && color.B >= color.G + 20
           && color.R <= 140;
}
