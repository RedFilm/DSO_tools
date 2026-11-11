namespace WalkerEngine;

/// <summary>Один разбор кадра: где цветная карта и где на ней маркер персонажа.</summary>
public sealed class MapScan
{
    private readonly byte[] _mask;
    private readonly int _width;
    private readonly int _height;

    internal MapScan(
        int width,
        int height,
        byte[] mask,
        MapBounds bounds,
        bool playerFound,
        double playerX,
        double playerY,
        int playerPixels)
    {
        _width = width;
        _height = height;
        _mask = mask;
        Bounds = bounds;
        HasMap = !bounds.IsEmpty;
        PlayerFound = playerFound;
        PlayerX = playerX;
        PlayerY = playerY;
        PlayerPixels = playerPixels;
    }

    public int Width => _width;

    public int Height => _height;

    public bool HasMap { get; }

    public bool PlayerFound { get; }

    public MapBounds Bounds { get; }

    /// <summary>Индекс пикселя по горизонтали. Дробная часть — центр тяжести точки, а не целый пиксель.</summary>
    public double PlayerX { get; }

    /// <summary>Индекс пикселя по вертикали.</summary>
    public double PlayerY { get; }

    public int PlayerPixels { get; }

    public bool ContainsPixel(int x, int y)
    {
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            return false;
        return _mask[y * _width + x] != 0;
    }

    /// <summary>
    /// Точка лежит на цветной карте, а не в чёрной пустоте и не в дыре внутри контура.
    /// Значки, нарисованные поверх карты, тоже считаются картой: под ними нет отдельного цвета пола.
    /// </summary>
    public bool IsWalkable(MapPoint point)
    {
        if (!HasMap || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            return false;

        var (pixelX, pixelY) = Bounds.ToPixel(point);
        int x = (int)Math.Floor(pixelX + 0.5);
        int y = (int)Math.Floor(pixelY + 0.5);
        return ContainsPixel(x, y);
    }
}

/// <summary>Находит карту и персонажа по цветам одного кадра.</summary>
public static class MapScanner
{
    /// <summary>Пустой разбор — нет карты и маркера (для блокировки ложных кликов).</summary>
    public static MapScan None(int width = 1, int height = 1)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        return new MapScan(width, height, new byte[width * height], MapBounds.Empty, false, 0, 0, 0);
    }

    public static MapScan Scan(ColorImage image, ColorProfile? colors = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        colors ??= ColorProfile.GreenMarker;

        int width = image.Width;
        int height = image.Height;
        byte[] pixels = image.Pixels;
        var foreground = new byte[width * height];

        for (int i = 0, p = 0; i < foreground.Length; i++, p += 4)
        {
            if (!colors.IsBackground(pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]))
                foreground[i] = 1;
        }

        var labels = new int[width * height];
        var components = new List<Component>();
        var stack = new Stack<int>();

        for (int i = 0; i < foreground.Length; i++)
        {
            if (foreground[i] == 0 || labels[i] != 0)
                continue;

            int id = components.Count + 1;
            var component = new Component(id);
            labels[i] = id;
            stack.Push(i);

            while (stack.Count > 0)
            {
                int current = stack.Pop();
                int x = current % width;
                int y = current / width;
                component.Include(x, y);
                Push(x - 1, y);
                Push(x + 1, y);
                Push(x, y - 1);
                Push(x, y + 1);
            }

            components.Add(component);

            void Push(int x, int y)
            {
                if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                    return;
                int neighbor = y * width + x;
                if (foreground[neighbor] == 0 || labels[neighbor] != 0)
                    return;
                labels[neighbor] = id;
                stack.Push(neighbor);
            }
        }

        int largest = -1;
        for (int i = 0; i < components.Count; i++)
        {
            if (largest < 0 || components[i].Area > components[largest].Area)
                largest = i;
        }

        var mask = new byte[width * height];
        var bounds = MapBounds.Empty;
        if (largest >= 0)
        {
            var component = components[largest];
            bounds = new MapBounds(component.MinX, component.MinY, component.MaxX, component.MaxY);
            int id = component.Id;
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == id)
                    mask[i] = 1;
            }
        }

        FindPlayer(pixels, width, height, colors, mask, stack, out bool playerFound, out double playerX, out double playerY, out int playerPixels);
        return new MapScan(width, height, mask, bounds, playerFound, playerX, playerY, playerPixels);
    }

    private static void FindPlayer(
        byte[] pixels,
        int width,
        int height,
        ColorProfile colors,
        byte[] mapMask,
        Stack<int> stack,
        out bool playerFound,
        out double playerX,
        out double playerY,
        out int playerPixels)
    {
        var seen = new byte[width * height];
        playerFound = false;
        playerX = 0;
        playerY = 0;
        playerPixels = 0;
        bool bestOnMap = false;

        for (int i = 0; i < seen.Length; i++)
        {
            if (seen[i] != 0 || !IsPlayerPixel(pixels, i, colors))
                continue;

            stack.Clear();
            seen[i] = 1;
            stack.Push(i);
            int count = 0;
            int onMap = 0;
            long sumWeight = 0;
            long sumX = 0;
            long sumY = 0;

            while (stack.Count > 0)
            {
                int current = stack.Pop();
                int x = current % width;
                int y = current / width;
                int pixel = current * 4;
                int weight = pixels[pixel + 1] - Math.Max(pixels[pixel], pixels[pixel + 2]);
                if (weight < 1)
                    weight = 1;

                count++;
                sumWeight += weight;
                sumX += (long)x * weight;
                sumY += (long)y * weight;
                if (mapMask[current] != 0)
                    onMap++;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        int nx = x + dx;
                        int ny = y + dy;
                        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                            continue;
                        int neighbor = ny * width + nx;
                        if (seen[neighbor] != 0 || !IsPlayerPixel(pixels, neighbor, colors))
                            continue;
                        seen[neighbor] = 1;
                        stack.Push(neighbor);
                    }
                }
            }

            bool onMapBlob = onMap * 2 >= count && onMap > 0;
            bool better = !playerFound
                || (onMapBlob && !bestOnMap)
                || (onMapBlob == bestOnMap && count > playerPixels);
            if (!better)
                continue;

            playerFound = true;
            bestOnMap = onMapBlob;
            playerPixels = count;
            playerX = (double)sumX / sumWeight;
            playerY = (double)sumY / sumWeight;
        }
    }

    private static bool IsPlayerPixel(byte[] pixels, int index, ColorProfile colors)
    {
        int pixel = index * 4;
        byte r = pixels[pixel];
        byte g = pixels[pixel + 1];
        byte b = pixels[pixel + 2];
        byte a = pixels[pixel + 3];
        if (colors.IsBackground(r, g, b, a))
            return false;
        return colors.IsPlayer(r, g, b);
    }

    private sealed class Component(int id)
    {
        public int Id { get; } = id;
        public int MinX { get; private set; } = int.MaxValue;
        public int MinY { get; private set; } = int.MaxValue;
        public int MaxX { get; private set; } = int.MinValue;
        public int MaxY { get; private set; } = int.MinValue;
        public int Area { get; private set; }

        public void Include(int x, int y)
        {
            Area++;
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
        }
    }
}
