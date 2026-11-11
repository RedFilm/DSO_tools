namespace WalkerEngine;

/// <summary>
/// Серое кольцо вокруг сундука или портала, когда курсор наведён на модель.
/// По нему выбирается точка клика. Исчезновение значка с карты здесь не проверяется.
/// </summary>
public static class ChestHover
{
    public const int Radius = 90;

    public const int MinPixels = 120;

    public static bool IsLit(ColorImage image, int x, int y)
        => Count(image, x, y) >= MinPixels;

    /// <summary>Центр рамки подсветки рядом с курсором. Клик ставится в модель, а не в край кольца.</summary>
    public static bool TryCenter(ColorImage image, int x, int y, out int centerX, out int centerY)
        => TryCenter(image, x, y, MapBounds.Empty, MapBounds.Empty, out centerX, out centerY);

    /// <summary>
    /// То же, но серые пиксели панели и миникарты не входят в подсветку.
    /// <paramref name="play"/> — поле мира. <paramref name="excluded"/> — миникарта.
    /// </summary>
    public static bool TryCenter(ColorImage image, int x, int y, MapBounds play, MapBounds excluded, out int centerX, out int centerY)
    {
        ArgumentNullException.ThrowIfNull(image);
        centerX = x;
        centerY = y;
        int radiusSquared = Radius * Radius;
        int left = Math.Max(0, x - Radius);
        int top = Math.Max(0, y - Radius);
        int right = Math.Min(image.Width - 1, x + Radius);
        int bottom = Math.Min(image.Height - 1, y + Radius);
        if (!play.IsEmpty)
        {
            left = Math.Max(left, play.Left);
            top = Math.Max(top, play.Top);
            right = Math.Min(right, play.Right);
            bottom = Math.Min(bottom, play.Bottom);
        }

        if (right < left || bottom < top)
            return false;

        int count = 0;
        int minX = right;
        int minY = bottom;
        int maxX = left;
        int maxY = top;
        for (int py = top; py <= bottom; py++)
        {
            for (int px = left; px <= right; px++)
            {
                int dx = px - x;
                int dy = py - y;
                if (dx * dx + dy * dy > radiusSquared || Inside(excluded, px, py) || !IsOutline(image[px, py]))
                    continue;
                count++;
                if (px < minX) minX = px;
                if (py < minY) minY = py;
                if (px > maxX) maxX = px;
                if (py > maxY) maxY = py;
            }
        }

        if (count < MinPixels)
            return false;

        centerX = (minX + maxX) / 2;
        centerY = (minY + maxY) / 2;
        return true;
    }

    public static int Count(ColorImage image, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(image);
        int radiusSquared = Radius * Radius;
        int left = Math.Max(0, x - Radius);
        int top = Math.Max(0, y - Radius);
        int right = Math.Min(image.Width - 1, x + Radius);
        int bottom = Math.Min(image.Height - 1, y + Radius);
        int count = 0;
        for (int py = top; py <= bottom; py++)
        {
            for (int px = left; px <= right; px++)
            {
                int dx = px - x;
                int dy = py - y;
                if (dx * dx + dy * dy > radiusSquared || !IsOutline(image[px, py]))
                    continue;
                count++;
            }
        }

        return count;
    }

    private static bool Inside(MapBounds bounds, int x, int y)
        => !bounds.IsEmpty && x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom;

    private static bool IsOutline(Rgba32 color)
    {
        int lum = (color.R + color.G + color.B) / 3;
        return lum >= 90
               && Math.Abs(color.R - color.G) <= 28
               && Math.Abs(color.G - color.B) <= 28
               && color.G <= color.R + 15;
    }
}
