using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrakenHub.Models;

namespace DrakenHub.Services;

/// <summary>
/// Finds the inventory window by matching the tab strip (I–VIII, crown, chest, red button).
/// Geometry is the 2560×1440 capture at UI scale 1: cell pitch 107, 7×4.
/// A live match scales those offsets; the strip itself does not include cell faces,
/// so a full bag still matches.
/// </summary>
internal static class InventoryCalibrator
{
    // Sort-button center relative to the template's top-left. Scale pivots here,
    // because the close button sits ~600px above the strip and amplifies scale error.
    private const int SortAnchorX = 760;
    private const int SortAnchorY = 40;

    private const int FirstCellX = 30;
    private const int FirstCellY = 76;
    private const int CellSize = 105;
    private const int Pitch = 107;
    private const int FirstTabX = 43;
    private const int FirstTabY = 40;
    private const int TabOffsetX = 65;
    private const int TabMarker = 48;
    private const int CloseX = 785;
    private const int CloseY = -602;

    // Skills window, scale 1. The banner (arrow + diamonds, no text) is on every tab.
    // "Знания" is the second tab under that banner. The bag appears only after that click.
    // Center of the banner's gold blob, relative to the template's top-left.
    private const int BannerAnchorX = 639;
    private const int BannerAnchorY = 18;
    private const int WisdomX = 274;
    private const int WisdomY = 111;
    private const int ShopX = 241;
    private const int ShopY = 62;

    private const double AcceptScore = 0.78;

    public readonly record struct Hit(int OriginX, int OriginY, double Scale, double Score);

    private readonly record struct Sample(short X, short Y, byte G);

    private static readonly Lazy<(Sample[] Coarse, Sample[] Fine)> Samples = new(() => LoadSamples("InventoryTabStrip.png"));
    private static readonly Lazy<(Sample[] Coarse, Sample[] Fine)> SkillSamples = new(() => LoadSamples("SkillsShopButtons.png"));
    private static readonly Lazy<(Sample[] Coarse, Sample[] Fine)> BannerSamples = new(() => LoadSamples("SkillsBanner.png"));

    public static bool TryMatch(out Hit hit)
    {
        hit = default;
        try
        {
            using var grab = Capture();
            if (grab == null) return false;
            var (coarse, fine) = Samples.Value;
            return Search(grab, coarse, fine, FindRedBlobs(grab), SortAnchorX, SortAnchorY, out hit);
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(InventorySettings inv, Hit hit)
    {
        var s = hit.Scale;
        inv.FirstCell = new ScreenPoint(At(hit.OriginX, FirstCellX, s), At(hit.OriginY, FirstCellY, s));
        inv.FirstTab = new ScreenPoint(At(hit.OriginX, FirstTabX, s), At(hit.OriginY, FirstTabY, s));
        inv.CloseInventory = new ScreenPoint(At(hit.OriginX, CloseX, s), At(hit.OriginY, CloseY, s));

        var pitch = Math.Max(17, (int)Math.Round(Pitch * s));
        var cell = Math.Max(16, (int)Math.Round(CellSize * s));
        if (cell >= pitch) cell = pitch - 1;
        inv.Grid.CellSize = cell;
        inv.Grid.GapX = pitch - cell;
        inv.Grid.GapY = pitch - cell;
        inv.Grid.TabOffsetX = Math.Max(10, (int)Math.Round(TabOffsetX * s));
        inv.Grid.TabMarkerSize = Math.Clamp((int)Math.Round(TabMarker * s), 8, 96);
        inv.Grid.ShowGrid = true;
    }

    /// <summary>
    /// Finds the skills window by the title banner, which stays put on every tab.
    /// </summary>
    public static bool TryMatchWisdom(out Hit hit)
    {
        hit = default;
        try
        {
            using var grab = Capture();
            if (grab == null) return false;
            var (coarse, fine) = BannerSamples.Value;
            return Search(grab, coarse, fine, FindArrowBlobs(grab), BannerAnchorX, BannerAnchorY, out hit);
        }
        catch
        {
            return false;
        }
    }

    public static Point WisdomClick(Hit hit) => new(At(hit.OriginX, WisdomX, hit.Scale), At(hit.OriginY, WisdomY, hit.Scale));

    /// <summary>Finds the hammer and bag. They are only on the knowledge tab.</summary>
    public static bool TryMatchSkills(out Hit hit)
    {
        hit = default;
        try
        {
            using var grab = Capture();
            if (grab == null) return false;
            var (coarse, fine) = SkillSamples.Value;
            return Search(grab, coarse, fine, FindBagBlobs(grab), ShopX, ShopY, out hit);
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyWisdom(InventorySettings inv, Hit hit)
    {
        var p = WisdomClick(hit);
        inv.WisdomTab = new ScreenPoint(p.X, p.Y);
    }

    public static void ApplyShop(InventorySettings inv, Hit hit)
    {
        var s = hit.Scale;
        inv.ShopButton = new ScreenPoint(At(hit.OriginX, ShopX, s), At(hit.OriginY, ShopY, s));
    }

    private static bool Search(Grab grab, Sample[] coarse, Sample[] fine, List<RedBlob> blobs, int anchorX, int anchorY, out Hit hit)
    {
        hit = default;
        if (blobs.Count == 0) return false;

        var best = new Hit(0, 0, 1, -1);
        foreach (var blob in blobs)
        {
            for (var scale = 0.60; scale <= 1.80; scale += 0.04)
            {
                var ox = (int)Math.Round(blob.Cx - anchorX * scale);
                var oy = (int)Math.Round(blob.Cy - anchorY * scale);
                for (var jy = -24; jy <= 24; jy += 4)
                for (var jx = -24; jx <= 24; jx += 4)
                {
                    var score = Correlate(grab, coarse, ox + jx, oy + jy, scale, step: 4);
                    if (score > best.Score)
                        best = new Hit(ox + jx, oy + jy, scale, score);
                }
            }
        }

        if (best.Score < 0.35) return false;

        best = Refine(grab, fine, best, scaleWindow: 0.03, scaleStep: 0.01, pixelWindow: 12, pixelStep: 2);
        best = Refine(grab, fine, best, scaleWindow: 0.012, scaleStep: 0.004, pixelWindow: 3, pixelStep: 1);
        if (best.Score < AcceptScore) return false;

        hit = new Hit(
            best.OriginX + grab.ScreenX,
            best.OriginY + grab.ScreenY,
            best.Scale,
            best.Score);
        return true;
    }

    private static int At(int origin, int offset, double scale) => origin + (int)Math.Round(offset * scale);

    private static Hit Refine(Grab grab, Sample[] samples, Hit seed, double scaleWindow, double scaleStep, int pixelWindow, int pixelStep)
    {
        var best = seed;
        for (var scale = seed.Scale - scaleWindow; scale <= seed.Scale + scaleWindow + 0.0001; scale += scaleStep)
        for (var dy = -pixelWindow; dy <= pixelWindow; dy += pixelStep)
        for (var dx = -pixelWindow; dx <= pixelWindow; dx += pixelStep)
        {
            var score = Correlate(grab, samples, seed.OriginX + dx, seed.OriginY + dy, scale, step: 1);
            if (score > best.Score)
                best = new Hit(seed.OriginX + dx, seed.OriginY + dy, scale, score);
        }
        return best;
    }

    private static double Correlate(Grab grab, Sample[] samples, int ox, int oy, double scale, int step)
    {
        long n = 0, sI = 0, sT = 0, sII = 0, sTT = 0, sIT = 0;
        var coarse = step == 4;
        var gray = coarse ? grab.Small : grab.Gray;
        var width = coarse ? grab.SmallWidth : grab.Width;
        var height = coarse ? grab.SmallHeight : grab.Height;
        foreach (var p in samples)
        {
            var sx = ox + (int)(p.X * scale);
            var sy = oy + (int)(p.Y * scale);
            if (coarse)
            {
                sx >>= 2;
                sy >>= 2;
            }
            if ((uint)sx >= (uint)width || (uint)sy >= (uint)height) continue;
            var iv = gray[(sy * width) + sx];
            n++;
            sI += iv;
            sT += p.G;
            sII += (long)iv * iv;
            sTT += (long)p.G * p.G;
            sIT += (long)iv * p.G;
        }

        if (n < samples.Length * 0.85) return -1;
        var nd = (double)n;
        var num = nd * sIT - (double)sI * sT;
        var den = (nd * sII - (double)sI * sI) * (nd * sTT - (double)sT * sT);
        if (den <= 1) return -1;
        return num / Math.Sqrt(den);
    }

    private static List<RedBlob> FindRedBlobs(Grab grab)
    {
        var blobs = new List<RedBlob>();
        var buf = grab.Pixels;
        var stride = grab.Stride;
        for (var y = 0; y < grab.Height; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < grab.Width; x += 2)
            {
                var i = row + x * 4;
                var b = buf[i];
                var g = buf[i + 1];
                var r = buf[i + 2];
                if (r <= 150 || g >= 80 || b >= 90 || r <= g * 2 || r * 10 <= b * 16) continue;

                RedBlob? hit = null;
                foreach (var blob in blobs)
                {
                    if (x < blob.X0 - 16 || x > blob.X1 + 16 || y < blob.Y0 - 16 || y > blob.Y1 + 16) continue;
                    hit = blob;
                    break;
                }

                if (hit == null)
                {
                    blobs.Add(new RedBlob { X0 = x, Y0 = y, X1 = x, Y1 = y, Count = 1 });
                    continue;
                }

                if (x < hit.X0) hit.X0 = x;
                if (y < hit.Y0) hit.Y0 = y;
                if (x > hit.X1) hit.X1 = x;
                if (y > hit.Y1) hit.Y1 = y;
                hit.Count++;
            }
        }

        return blobs.Where(IsSortCandidate).ToList();
    }

    private static bool IsSortCandidate(RedBlob blob)
    {
        if (blob.Count < 6) return false;
        var w = blob.X1 - blob.X0;
        var h = blob.Y1 - blob.Y0;
        if (w < 14 || h < 12 || w > 100 || h > 100) return false;
        var aspect = Math.Max(w, h) / (double)Math.Min(w, h);
        return aspect <= 2.6;
    }

    private static List<RedBlob> FindArrowBlobs(Grab grab)
    {
        var blobs = new List<RedBlob>();
        var buf = grab.Pixels;
        var stride = grab.Stride;
        for (var y = 0; y < grab.Height; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < grab.Width; x += 2)
            {
                var i = row + x * 4;
                var b = buf[i];
                var g = buf[i + 1];
                var r = buf[i + 2];
                if (r <= 200 || g <= 150 || g >= 220 || b <= 60 || b >= 160 || r <= g || g <= b) continue;

                RedBlob? hit = null;
                foreach (var blob in blobs)
                {
                    if (x < blob.X0 - 30 || x > blob.X1 + 30 || y < blob.Y0 - 24 || y > blob.Y1 + 24) continue;
                    hit = blob;
                    break;
                }

                if (hit == null)
                {
                    blobs.Add(new RedBlob { X0 = x, Y0 = y, X1 = x, Y1 = y, Count = 1 });
                    continue;
                }

                if (x < hit.X0) hit.X0 = x;
                if (y < hit.Y0) hit.Y0 = y;
                if (x > hit.X1) hit.X1 = x;
                if (y > hit.Y1) hit.Y1 = y;
                hit.Count++;
            }
        }

        return blobs.Where(blob =>
        {
            if (blob.Count < 200) return false;
            var w = blob.X1 - blob.X0;
            var h = blob.Y1 - blob.Y0;
            return w >= 500 && h >= 40 && w <= 1500 && h <= 140;
        }).ToList();
    }

    private static List<RedBlob> FindBagBlobs(Grab grab)
    {
        var blobs = new List<RedBlob>();
        var buf = grab.Pixels;
        var stride = grab.Stride;
        for (var y = 0; y < grab.Height; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < grab.Width; x += 2)
            {
                var i = row + x * 4;
                var b = buf[i];
                var g = buf[i + 1];
                var r = buf[i + 2];
                // Warm cream of the bag. The hammer is whiter, so its blue stays high.
                if (r <= 220 || g <= 175 || g >= 235 || b <= 130 || b >= 195 || r <= g || g <= b) continue;

                RedBlob? hit = null;
                foreach (var blob in blobs)
                {
                    if (x < blob.X0 - 16 || x > blob.X1 + 16 || y < blob.Y0 - 16 || y > blob.Y1 + 16) continue;
                    hit = blob;
                    break;
                }

                if (hit == null)
                {
                    blobs.Add(new RedBlob { X0 = x, Y0 = y, X1 = x, Y1 = y, Count = 1 });
                    continue;
                }

                if (x < hit.X0) hit.X0 = x;
                if (y < hit.Y0) hit.Y0 = y;
                if (x > hit.X1) hit.X1 = x;
                if (y > hit.Y1) hit.Y1 = y;
                hit.Count++;
            }
        }

        return blobs.Where(blob =>
        {
            if (blob.Count < 20) return false;
            var w = blob.X1 - blob.X0;
            var h = blob.Y1 - blob.Y0;
            return w >= 10 && h >= 10 && w <= 90 && h <= 80;
        }).ToList();
    }

    private static (Sample[] Coarse, Sample[] Fine) LoadSamples(string fileName)
    {
        var asm = typeof(InventoryCalibrator).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Template is missing: " + fileName);
        using var stream = asm.GetManifestResourceStream(name)!;
        using var raw = new Bitmap(stream);
        using var bmp = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.DrawImage(raw, 0, 0);

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var coarse = new List<Sample>();
            var fine = new List<Sample>();
            var row = new byte[Math.Abs(data.Stride)];
            for (var y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (var x = 0; x < bmp.Width; x++)
                {
                    var i = x * 4;
                    var gray = (byte)((row[i + 2] * 3 + row[i + 1] * 4 + row[i]) >> 3);
                    if ((x & 1) == 0 && (y & 1) == 0)
                        fine.Add(new Sample((short)x, (short)y, gray));
                    if ((x & 7) == 0 && (y & 7) == 0)
                        coarse.Add(new Sample((short)x, (short)y, gray));
                }
            }
            return (coarse.ToArray(), fine.ToArray());
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Grab? Capture()
    {
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width < 200 || bounds.Height < 200) return null;

        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            var gray = new byte[bmp.Width * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
            {
                var src = y * stride;
                var dst = y * bmp.Width;
                for (var x = 0; x < bmp.Width; x++)
                {
                    var i = src + x * 4;
                    gray[dst + x] = (byte)((pixels[i + 2] * 3 + pixels[i + 1] * 4 + pixels[i]) >> 3);
                }
            }

            // Coarse correlation reads a 4× downsampled buffer through the same Gray array
            // by shifting coordinates. Build that buffer in a Grab the scorer selects via step.
            var cw = bmp.Width >> 2;
            var ch = bmp.Height >> 2;
            var small = new byte[cw * ch];
            for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
            {
                var sum = 0;
                var oy = y << 2;
                var ox = x << 2;
                for (var dy = 0; dy < 4; dy++)
                {
                    var row = (oy + dy) * bmp.Width + ox;
                    sum += gray[row] + gray[row + 1] + gray[row + 2] + gray[row + 3];
                }
                small[y * cw + x] = (byte)(sum >> 4);
            }

            return new Grab(bmp, gray, small, pixels, stride, bounds.X, bounds.Y);
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private sealed class RedBlob
    {
        public int X0, Y0, X1, Y1, Count;
        public int Cx => (X0 + X1) / 2;
        public int Cy => (Y0 + Y1) / 2;
    }

    private sealed class Grab : IDisposable
    {
        public readonly byte[] Gray;
        public readonly byte[] Small;
        public readonly byte[] Pixels;
        public readonly int Stride;
        public readonly int ScreenX;
        public readonly int ScreenY;
        public readonly int Width;
        public readonly int Height;
        public readonly int SmallWidth;
        public readonly int SmallHeight;
        private readonly Bitmap _bmp;

        public Grab(Bitmap bmp, byte[] gray, byte[] small, byte[] pixels, int stride, int screenX, int screenY)
        {
            _bmp = bmp;
            Gray = gray;
            Small = small;
            Pixels = pixels;
            Stride = stride;
            ScreenX = screenX;
            ScreenY = screenY;
            Width = bmp.Width;
            Height = bmp.Height;
            SmallWidth = bmp.Width >> 2;
            SmallHeight = bmp.Height >> 2;
        }

        public void Dispose() => _bmp.Dispose();
    }
}
