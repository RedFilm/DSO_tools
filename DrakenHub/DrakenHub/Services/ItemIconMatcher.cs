using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrakenHub.Models;

namespace DrakenHub.Services;

/// <summary>
/// User gem templates. The stored image is the colored stone only: the slot frame,
/// the stack count and the white glow are left out. Matching is a color correlation
/// with a few pixels of shift, after a mean-color gate so dozens of templates do not
/// each get a full search.
/// </summary>
internal static class ItemIconMatcher
{
    private const double AcceptScore = 0.84;
    private const double Margin = 0.04;
    private const double CoarseKeep = 0.62;
    private const int ColorGate = 120;
    private const int Shift = 6;
    private const int MinGemPixels = 40;

    public readonly struct IconSample
    {
        public readonly short X;
        public readonly short Y;
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public IconSample(int x, int y, byte r, byte g, byte b)
        {
            X = (short)x;
            Y = (short)y;
            R = r;
            G = g;
            B = b;
        }
    }

    public sealed class Prepared
    {
        public required IconSample[] Coarse { get; init; }
        public required IconSample[] Fine { get; init; }
        public int MeanR { get; init; }
        public int MeanG { get; init; }
        public int MeanB { get; init; }
    }

    public sealed class Set
    {
        public static readonly Set Empty = new();

        public Prepared[] Icons { get; init; } = [];
        public int Inset { get; init; }
        public int Inner { get; init; }
        public int CellSize { get; init; }

        public bool TryMatch(Point origin, GridSnapshot? snapshot, InputService input, out Point center)
        {
            center = new Point(origin.X + CellSize / 2, origin.Y + CellSize / 2);
            if (Icons.Length == 0 || Inner < 8) return false;
            if (!TryCellMean(origin, snapshot, input, out var meanR, out var meanG, out var meanB))
                return false;

            var gate = ColorGate * ColorGate;
            var best = -1.0;
            var second = -1.0;
            var baseX = origin.X + Inset;
            var baseY = origin.Y + Inset;

            foreach (var icon in Icons)
            {
                var dr = meanR - icon.MeanR;
                var dg = meanG - icon.MeanG;
                var db = meanB - icon.MeanB;
                if (dr * dr + dg * dg + db * db > gate) continue;

                var coarse = BestShift(icon.Coarse, baseX, baseY, snapshot, input, -Shift, Shift, -Shift, Shift, 2, out var bx, out var by);
                if (coarse < CoarseKeep) continue;

                var fine = BestShift(
                    icon.Fine, baseX, baseY, snapshot, input,
                    Math.Max(-Shift, bx - 2), Math.Min(Shift, bx + 2),
                    Math.Max(-Shift, by - 2), Math.Min(Shift, by + 2),
                    1, out _, out _);

                if (fine > best)
                {
                    second = best;
                    best = fine;
                }
                else if (fine > second)
                {
                    second = fine;
                }
            }

            return best >= AcceptScore && (second < 0 || best - second >= Margin);
        }

        private bool TryCellMean(Point origin, GridSnapshot? snapshot, InputService input, out int meanR, out int meanG, out int meanB)
        {
            long sr = 0, sg = 0, sb = 0, n = 0;
            for (var y = 0; y < Inner; y += 2)
            for (var x = 0; x < Inner; x += 2)
            {
                if (IsCountCorner(x, y, Inner)) continue;
                if (!Read(snapshot, input, origin.X + Inset + x, origin.Y + Inset + y, out var r, out var g, out var b))
                    continue;
                if (!IsGem(r, g, b)) continue;
                sr += r;
                sg += g;
                sb += b;
                n++;
            }

            if (n < 24)
            {
                meanR = meanG = meanB = 0;
                return false;
            }

            meanR = (int)(sr / n);
            meanG = (int)(sg / n);
            meanB = (int)(sb / n);
            return true;
        }
    }

    public static Set Prepare(InventorySettings inv)
    {
        var cell = Math.Max(12, inv.Grid.CellSize);
        InnerSize(cell, out var inset, out var inner);
        var icons = new List<Prepared>();
        foreach (var item in inv.OtherItems)
        {
            if (!item.Enabled || string.IsNullOrEmpty(item.IconPng)) continue;
            if (TryPrepare(item.IconPng, inner, out var prepared))
                icons.Add(prepared);
        }

        return new Set
        {
            Icons = icons.ToArray(),
            Inset = inset,
            Inner = inner,
            CellSize = cell
        };
    }

    /// <summary>
    /// Crop the stone out of one cell. Call after the cursor has been moved off the cell,
    /// otherwise the pointer is baked into the template.
    /// </summary>
    public static bool TryCapture(Point cellOrigin, int cellSize, out string pngBase64)
    {
        pngBase64 = "";
        InnerSize(cellSize, out var inset, out var inner);
        if (inner < 8) return false;

        using var bmp = new Bitmap(inner, inner, PixelFormat.Format32bppArgb);
        try
        {
            using var gfx = Graphics.FromImage(bmp);
            gfx.CopyFromScreen(
                new Point(cellOrigin.X + inset, cellOrigin.Y + inset),
                Point.Empty,
                new Size(inner, inner),
                CopyPixelOperation.SourceCopy);
        }
        catch
        {
            return false;
        }

        var gem = MaskGem(bmp);
        if (gem < MinGemPixels) return false;

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        pngBase64 = Convert.ToBase64String(ms.ToArray());
        return true;
    }

    public static Bitmap? DecodePreview(string? pngBase64)
    {
        if (string.IsNullOrEmpty(pngBase64)) return null;
        try
        {
            var bytes = Convert.FromBase64String(pngBase64);
            using var ms = new MemoryStream(bytes);
            using var raw = new Bitmap(ms);
            return new Bitmap(raw);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryPrepare(string pngBase64, int target, out Prepared prepared)
    {
        prepared = null!;
        Bitmap? bmp = null;
        try
        {
            var bytes = Convert.FromBase64String(pngBase64);
            using var ms = new MemoryStream(bytes);
            using var raw = new Bitmap(ms);
            bmp = raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb);
        }
        catch
        {
            return false;
        }

        using (bmp)
        {
            var all = Resample(bmp, target);
            if (all.Count < MinGemPixels) return false;

            var fine = new List<IconSample>(all.Count / 2);
            var coarse = new List<IconSample>(all.Count / 8);
            long sr = 0, sg = 0, sb = 0;
            foreach (var p in all)
            {
                if ((p.X & 1) != 0 || (p.Y & 1) != 0) continue;
                fine.Add(p);
                sr += p.R;
                sg += p.G;
                sb += p.B;
                if ((p.X & 3) == 0 && (p.Y & 3) == 0)
                    coarse.Add(p);
            }

            if (fine.Count < 24)
            {
                fine.Clear();
                fine.AddRange(all);
                sr = sg = sb = 0;
                foreach (var p in fine)
                {
                    sr += p.R;
                    sg += p.G;
                    sb += p.B;
                }
            }

            if (coarse.Count < 12)
                coarse = fine;

            var n = fine.Count;
            prepared = new Prepared
            {
                Coarse = coarse.ToArray(),
                Fine = fine.ToArray(),
                MeanR = (int)(sr / n),
                MeanG = (int)(sg / n),
                MeanB = (int)(sb / n)
            };
            return true;
        }
    }

    /// <summary>
    /// Scale to the live cell. Nearest-neighbor drops a true match captured at another
    /// cell size to just under the accept bar, so color is filtered and the mask stays nearest.
    /// </summary>
    private static List<IconSample> Resample(Bitmap src, int target)
    {
        var list = new List<IconSample>(target * target / 2);
        var data = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var buffer = new byte[Math.Abs(stride) * src.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            var srcW = src.Width;
            var srcH = src.Height;

            byte A(int x, int y) => buffer[Clamp(y, srcH) * stride + Clamp(x, srcW) * 4 + 3];
            byte C(int x, int y, int ch) => buffer[Clamp(y, srcH) * stride + Clamp(x, srcW) * 4 + ch];

            for (var y = 0; y < target; y++)
            {
                var fy = (y + 0.5) * srcH / target - 0.5;
                var y0 = (int)Math.Floor(fy);
                var ty = fy - y0;
                for (var x = 0; x < target; x++)
                {
                    var fx = (x + 0.5) * srcW / target - 0.5;
                    var x0 = (int)Math.Floor(fx);
                    var tx = fx - x0;
                    var nx = (int)Math.Round(fx);
                    var ny = (int)Math.Round(fy);
                    if (A(nx, ny) < 128) continue;

                    double wr = 0, wg = 0, wb = 0, wsum = 0;
                    Take(x0, y0, (1 - tx) * (1 - ty));
                    Take(x0 + 1, y0, tx * (1 - ty));
                    Take(x0, y0 + 1, (1 - tx) * ty);
                    Take(x0 + 1, y0 + 1, tx * ty);
                    if (wsum < 0.01) continue;
                    list.Add(new IconSample(x, y, (byte)(wr / wsum), (byte)(wg / wsum), (byte)(wb / wsum)));

                    void Take(int sx, int sy, double w)
                    {
                        if (w <= 0 || A(sx, sy) < 128) return;
                        wr += C(sx, sy, 2) * w;
                        wg += C(sx, sy, 1) * w;
                        wb += C(sx, sy, 0) * w;
                        wsum += w;
                    }
                }
            }
        }
        finally
        {
            src.UnlockBits(data);
        }

        return list;
    }

    private static int Clamp(int v, int size) => v < 0 ? 0 : v >= size ? size - 1 : v;

    private static int MaskGem(Bitmap bmp)
    {
        var gem = 0;
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var buffer = new byte[Math.Abs(stride) * bmp.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var i = y * stride + x * 4;
                var keep = !IsCountCorner(x, y, bmp.Width) && IsGem(buffer[i + 2], buffer[i + 1], buffer[i]);
                buffer[i + 3] = keep ? (byte)255 : (byte)0;
                if (keep) gem++;
            }

            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return gem;
    }

    /// <summary>Frame inset and the square inside it, in cell-local pixels.</summary>
    public static void InnerSize(int cellSize, out int inset, out int inner)
    {
        inset = Math.Max(4, cellSize * 14 / 105);
        inner = Math.Max(8, cellSize - inset * 2);
    }

    private static bool IsCountCorner(int x, int y, int inner)
    {
        var cw = Math.Max(8, inner * 42 / 100);
        var ch = Math.Max(8, inner * 30 / 100);
        return x >= inner - cw && y >= inner - ch;
    }

    /// <summary>Colored stone. Near-white glow and the dark slot are not part of the gem.</summary>
    private static bool IsGem(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max < 24) return false;
        if (max > 214 && max - min < 36) return false;
        return true;
    }

    private static double BestShift(
        IconSample[] samples,
        int baseX,
        int baseY,
        GridSnapshot? snapshot,
        InputService input,
        int x0,
        int x1,
        int y0,
        int y1,
        int step,
        out int bestDx,
        out int bestDy)
    {
        bestDx = 0;
        bestDy = 0;
        var best = -1.0;
        if (samples.Length == 0 || step < 1) return best;
        for (var dy = y0; dy <= y1; dy += step)
        for (var dx = x0; dx <= x1; dx += step)
        {
            var score = Correlate(samples, baseX + dx, baseY + dy, snapshot, input);
            if (score <= best) continue;
            best = score;
            bestDx = dx;
            bestDy = dy;
        }

        return best;
    }

    private static double Correlate(IconSample[] samples, int ox, int oy, GridSnapshot? snapshot, InputService input)
    {
        long n = 0, sI = 0, sT = 0, sII = 0, sTT = 0, sIT = 0;
        var pixels = 0;
        foreach (var p in samples)
        {
            if (!Read(snapshot, input, ox + p.X, oy + p.Y, out var r, out var g, out var b))
                continue;
            pixels++;
            Acc(r, p.R);
            Acc(g, p.G);
            Acc(b, p.B);
        }

        if (pixels < samples.Length * 0.85) return -1;
        var nd = (double)n;
        var num = nd * sIT - (double)sI * sT;
        var den = (nd * sII - (double)sI * sI) * (nd * sTT - (double)sT * sT);
        if (den <= 1) return -1;
        return num / Math.Sqrt(den);

        void Acc(int iv, int tv)
        {
            n++;
            sI += iv;
            sT += tv;
            sII += (long)iv * iv;
            sTT += (long)tv * tv;
            sIT += (long)iv * tv;
        }
    }

    private static bool Read(GridSnapshot? snapshot, InputService input, int x, int y, out byte r, out byte g, out byte b)
    {
        if (snapshot != null)
            return snapshot.TryGetRgb(x, y, out r, out g, out b);

        var c = input.GetPixel(x, y);
        r = c.R;
        g = c.G;
        b = c.B;
        return true;
    }
}
