namespace WalkerEngine;

/// <summary>
/// Окно входа в локацию: синяя кнопка Enter в нижней половине клиента.
/// Экран сбоя соединения: широкая синяя «Новая попытка» по центру низа.
/// Загрузка — горизонтальная полоса у нижнего края: сначала серый трек, затем зелёная заливка.
/// </summary>
public static class EntranceFinder
{
    /// <summary>
    /// Клик по шкале сложности относительно центра кнопки Enter.
    /// Замерено на живом клиенте 1024×722: Enter ≈ (786,574), слоты x≈343…685 шаг 57, y≈375
    /// (шкала над блоком наград; старый y≈514 попадал в пустой камень и закрывал окно).
    /// </summary>
    public static void DifficultyPoint(int enterX, int enterY, int difficulty, out int x, out int y)
    {
        difficulty = Math.Clamp(difficulty, 1, 7);
        const int firstOffsetX = -443;
        const int stepX = 57;
        const int offsetY = -199;
        x = enterX + firstOffsetX + (difficulty - 1) * stepX;
        y = enterY + offsetY;
    }

    /// <summary>
    /// Кнопка «Новая попытка» после сбоя соединения.
    /// Замерено на кадре 1024×723: центр ≈ (514,689), bbox ≈ (417,668)–(603,708).
    /// Шире и ниже Enter; по горизонтали у центра экрана (Enter справа).
    /// </summary>
    public static bool TryFindRetryButton(ColorImage image, out int x, out int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        x = 0;
        y = 0;
        if (image.Width < 200 || image.Height < 120)
            return false;

        int best = 0;
        int startY = image.Height * 70 / 100;
        int midX = image.Width / 2;
        int maxCenterOffset = Math.Max(80, image.Width * 18 / 100);
        var seen = new bool[image.Width * image.Height];
        var queue = new Queue<int>();
        for (int py = startY; py < image.Height; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                int index = py * image.Width + px;
                if (seen[index] || !IsButton(image[px, py]))
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
                            if (nx < 0 || ny < startY || nx >= image.Width || ny >= image.Height)
                                continue;
                            int next = ny * image.Width + nx;
                            if (seen[next] || !IsButton(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                int centerX = (int)Math.Round(sumX / count);
                int centerY = (int)Math.Round(sumY / count);
                if (count < 200
                    || sideX < 100
                    || sideX > image.Width * 45 / 100
                    || sideY < 18
                    || sideY > 52
                    || Math.Abs(centerX - midX) > maxCenterOffset
                    || count <= best)
                {
                    continue;
                }

                best = count;
                x = centerX;
                y = centerY;
            }
        }

        return best > 0;
    }

    public static bool TryFindButton(ColorImage image, MapBounds excluded, out int x, out int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        x = 0;
        y = 0;
        int best = 0;
        int startY = image.Height * 45 / 100;
        var seen = new bool[image.Width * image.Height];
        var queue = new Queue<int>();
        for (int py = startY; py < image.Height; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                int index = py * image.Width + px;
                if (seen[index] || Inside(excluded, px, py) || !IsButton(image[px, py]))
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
                            if (nx < 0 || ny < startY || nx >= image.Width || ny >= image.Height)
                                continue;
                            int next = ny * image.Width + nx;
                            if (seen[next] || Inside(excluded, nx, ny) || !IsButton(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                if (count < 80 || sideX < 48 || sideY < 8 || sideY > 36 || count <= best)
                    continue;

                best = count;
                x = (int)Math.Round(sumX / count);
                y = (int)Math.Round(sumY / count);
            }
        }

        return best > 0;
    }

    public static bool HasLoadingBar(ColorImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width < 80 || image.Height < 40)
            return false;

        // Полоса у самого низа экрана (серый трек с 0% или зелёная заливка).
        int startY = Math.Max(0, image.Height * 78 / 100);
        int minWidth = Math.Max(80, image.Width * 18 / 100);
        var seen = new bool[image.Width * image.Height];
        var queue = new Queue<int>();
        for (int py = startY; py < image.Height; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                int index = py * image.Width + px;
                if (seen[index] || !IsBarPixel(image[px, py]))
                    continue;

                queue.Clear();
                queue.Enqueue(index);
                seen[index] = true;
                int count = 0;
                int fill = 0;
                int track = 0;
                int minX = px;
                int maxX = px;
                int minY = py;
                int maxY = py;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int cx = current % image.Width;
                    int cy = current / image.Width;
                    count++;
                    Rgba32 color = image[cx, cy];
                    if (IsBarFill(color))
                        fill++;
                    else
                        track++;
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
                            if (nx < 0 || ny < startY || nx >= image.Width || ny >= image.Height)
                                continue;
                            int next = ny * image.Width + nx;
                            if (seen[next] || !IsBarPixel(image[nx, ny]))
                                continue;
                            seen[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                int sideX = maxX - minX + 1;
                int sideY = maxY - minY + 1;
                // Тонкая широкая полоса: заливка и/или серый трек.
                if (count >= 40 && sideX >= minWidth && sideY >= 2 && sideY <= 22 && (fill >= 20 || track >= 40))
                    return true;
            }
        }

        // Запасной путь: длинный непрерывный ряд пикселей полосы в одной строке.
        for (int py = startY; py < image.Height; py++)
        {
            int run = 0;
            int best = 0;
            for (int px = 0; px < image.Width; px++)
            {
                if (IsBarPixel(image[px, py]))
                {
                    run++;
                    if (run > best)
                        best = run;
                }
                else
                {
                    run = 0;
                }
            }

            if (best >= minWidth)
                return true;
        }

        return false;
    }

    private static bool Inside(MapBounds bounds, int x, int y)
        => !bounds.IsEmpty && x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom;

    private static bool IsButton(Rgba32 color)
        => color.R <= 70
           && color.G >= 30
           && color.G <= 160
           && color.B >= 65
           && color.B <= 190
           && color.B >= color.R + 35
           && color.G + 20 >= color.R
           && color.B + 20 >= color.G;

    private static bool IsBarPixel(Rgba32 color)
        => IsBarFill(color) || IsBarTrack(color);

    /// <summary>Зелёная заливка (включая тусклую середину прогресса).</summary>
    private static bool IsBarFill(Rgba32 color)
        => color.G >= 95
           && color.R <= 110
           && color.B <= 120
           && color.G >= color.R + 25
           && color.G >= color.B + 15;

    /// <summary>Серый трек пустой полосы (до появления зелёной заливки).</summary>
    private static bool IsBarTrack(Rgba32 color)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));
        int avg = (color.R + color.G + color.B) / 3;
        return max - min <= 28 && avg is >= 50 and <= 145;
    }
}
