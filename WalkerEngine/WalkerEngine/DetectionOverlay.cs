namespace WalkerEngine;

/// <summary>Рисует поверх кадра границу карты и прицел в том месте, где движок считает персонажа.</summary>
public static class DetectionOverlay
{
    private static readonly Rgba32 BoundsColor = Rgba32.Opaque(255, 0, 255);
    private static readonly Rgba32 CrossColor = Rgba32.Opaque(255, 255, 0);

    public static ColorImage Draw(ColorImage source, MapBounds bounds, bool hasMap, MapPoint? player)
    {
        ArgumentNullException.ThrowIfNull(source);
        var image = source.Clone();
        if (hasMap && !bounds.IsEmpty)
            DrawRectangle(image, bounds, BoundsColor);

        if (player is { } point && hasMap && !bounds.IsEmpty)
        {
            var (pixelX, pixelY) = bounds.ToPixel(point);
            DrawCross(image, (int)Math.Floor(pixelX + 0.5), (int)Math.Floor(pixelY + 0.5), 6, CrossColor);
        }

        return image;
    }

    public static void PaintDisc(ColorImage image, int centerX, int centerY, int radius, Rgba32 color)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));

        int radiusSquared = radius * radius;
        for (int y = centerY - radius; y <= centerY + radius; y++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
                    continue;
                int dx = x - centerX;
                int dy = y - centerY;
                if (dx * dx + dy * dy <= radiusSquared)
                    image[x, y] = color;
            }
        }
    }

    private static void DrawRectangle(ColorImage image, MapBounds bounds, Rgba32 color)
    {
        for (int x = bounds.Left; x <= bounds.Right; x++)
        {
            Set(image, x, bounds.Top, color);
            Set(image, x, bounds.Bottom, color);
        }

        for (int y = bounds.Top; y <= bounds.Bottom; y++)
        {
            Set(image, bounds.Left, y, color);
            Set(image, bounds.Right, y, color);
        }
    }

    private static void DrawCross(ColorImage image, int centerX, int centerY, int radius, Rgba32 color)
    {
        for (int d = -radius; d <= radius; d++)
        {
            Set(image, centerX + d, centerY, color);
            Set(image, centerX, centerY + d, color);
        }
    }

    private static void Set(ColorImage image, int x, int y, Rgba32 color)
    {
        if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height)
            return;
        image[x, y] = color;
    }
}
