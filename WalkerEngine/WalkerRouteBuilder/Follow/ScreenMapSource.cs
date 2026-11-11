using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal static class ScreenMapSource
{
    public static ColorImage Capture(Rectangle region)
    {
        if (region.Width < 8 || region.Height < 8)
            throw new InvalidOperationException("Прямоугольник миникарты слишком маленький. Задайте mapRegion в follow-config.json.");

        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);

        return ToColorImage(bitmap);
    }

    public static Bitmap? Copy(Rectangle region)
    {
        if (region.Width < 8 || region.Height < 8)
            return null;

        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(region.Location, Point.Empty, region.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap?.Dispose();
            return null;
        }
    }

    private static ColorImage ToColorImage(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            var rgba = new byte[width * height * 4];
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < width; x++)
                {
                    int source = x * 4;
                    int destination = (y * width + x) * 4;
                    rgba[destination] = row[source + 2];
                    rgba[destination + 1] = row[source + 1];
                    rgba[destination + 2] = row[source];
                    rgba[destination + 3] = row[source + 3] == 0 ? (byte)255 : row[source + 3];
                }
            }

            return new ColorImage(width, height, rgba);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
