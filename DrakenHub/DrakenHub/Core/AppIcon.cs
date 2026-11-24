using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DrakenHub.Core;

/// <summary>Amber gem on a dark rounded tile. Used for the window, taskbar, and tray.</summary>
internal static class AppIcon
{
    public static Icon Copy()
    {
        using var bmp = Render(32);
        var handle = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    internal static void Save(string path) => File.WriteAllBytes(path, BuildIco(16, 32, 48, 256));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Bitmap Bitmap(int size) => Render(size);

    private static byte[] BuildIco(params int[] sizes)
    {
        var pngs = sizes.Select(s =>
        {
            using var bmp = Render(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToArray();

        using var output = new MemoryStream();
        using var bw = new BinaryWriter(output);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)pngs.Length);
        var offset = 6 + 16 * pngs.Length;
        for (var i = 0; i < pngs.Length; i++)
        {
            var side = sizes[i] >= 256 ? 0 : sizes[i];
            bw.Write((byte)side);
            bw.Write((byte)side);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(pngs[i].Length);
            bw.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs)
            bw.Write(png);
        bw.Flush();
        return output.ToArray();
    }

    private static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        var pad = Math.Max(1f, size * 0.06f);
        var tile = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
        var radius = size * 0.22f;
        using (var path = RoundRect(tile, radius))
        using (var bg = new SolidBrush(Color.FromArgb(255, 34, 30, 26)))
        using (var edge = new Pen(Color.FromArgb(255, 224, 168, 72), Math.Max(1f, size / 18f)))
        {
            g.FillPath(bg, path);
            g.DrawPath(edge, path);
        }

        var cx = size / 2f;
        var cy = size / 2f + size * 0.02f;
        var gem = size * 0.26f;
        PointF[] diamond =
        [
            new(cx, cy - gem * 1.35f),
            new(cx + gem * 1.05f, cy),
            new(cx, cy + gem * 1.35f),
            new(cx - gem * 1.05f, cy)
        ];
        using (var amber = new SolidBrush(Color.FromArgb(255, 214, 164, 86)))
            g.FillPolygon(amber, diamond);

        PointF[] facet =
        [
            diamond[0],
            new(cx + gem * 0.34f, cy - gem * 0.08f),
            new(cx, cy + gem * 0.18f),
            new(cx - gem * 0.55f, cy - gem * 0.08f)
        ];
        using (var light = new SolidBrush(Color.FromArgb(230, 242, 214, 156)))
            g.FillPolygon(light, facet);

        var slit = gem * 0.34f;
        PointF[] pupil =
        [
            new(cx, cy - slit * 1.35f),
            new(cx + slit * 0.42f, cy),
            new(cx, cy + slit * 1.35f),
            new(cx - slit * 0.42f, cy)
        ];
        using (var dark = new SolidBrush(Color.FromArgb(255, 32, 26, 22)))
            g.FillPolygon(dark, pupil);

        return bmp;
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
