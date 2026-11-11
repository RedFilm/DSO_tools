using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrakenHub.Models;

namespace DrakenHub.Services;

internal sealed class ColorScanService(InputService input)
{
    public bool CellMatchesAny(InventorySettings inv, int row, int col, bool includeOtherItems)
        => CellMatchesAny(inv, row, col, includeOtherItems, snapshot: null);

    public bool CellMatchesAny(InventorySettings inv, int row, int col, bool includeOtherItems, GridSnapshot? snapshot)
    {
        var origin = InventoryLayout.CellOrigin(inv, row, col);
        if (origin == null) return false;

        foreach (var rarity in inv.Rarities)
        {
            if (!rarity.Enabled || rarity.Points.Count == 0) continue;
            if (Matches(origin.Value, rarity.Points, inv.Grid.ColorTolerance, snapshot))
                return true;
        }

        if (includeOtherItems)
        {
            foreach (var item in inv.OtherItems)
            {
                if (!item.Enabled || item.Points.Count == 0) continue;
                if (Matches(origin.Value, item.Points, inv.Grid.ColorTolerance, snapshot))
                    return true;
            }
        }

        return false;
    }

    public List<(int Row, int Col, Point Center)> FindMatches(InventorySettings inv, bool includeOtherItems, GridSnapshot? snapshot)
    {
        var result = new List<(int, int, Point)>();
        for (var row = 0; row < inv.Grid.Rows; row++)
        for (var col = 0; col < inv.Grid.Columns; col++)
        {
            if (!CellMatchesAny(inv, row, col, includeOtherItems, snapshot))
                continue;
            var center = InventoryLayout.CellCenter(inv, row, col);
            if (center == null) continue;
            result.Add((row, col, center.Value));
        }
        return result;
    }

    /// <summary>One screen grab for the whole inventory grid (+ rarity point offsets).</summary>
    public static GridSnapshot? CaptureGrid(InventorySettings inv)
    {
        if (inv.FirstCell == null) return null;
        var g = inv.Grid;
        var maxOx = 0;
        var maxOy = 0;
        var minOx = 0;
        var minOy = 0;

        void Accrue(IEnumerable<RelativeColorPoint> points)
        {
            foreach (var p in points)
            {
                if (p.OffsetX > maxOx) maxOx = p.OffsetX;
                if (p.OffsetY > maxOy) maxOy = p.OffsetY;
                if (p.OffsetX < minOx) minOx = p.OffsetX;
                if (p.OffsetY < minOy) minOy = p.OffsetY;
            }
        }

        foreach (var r in inv.Rarities.Where(x => x.Enabled))
            Accrue(r.Points);
        foreach (var item in inv.OtherItems.Where(x => x.Enabled))
            Accrue(item.Points);

        var gridW = g.Columns * g.CellSize + Math.Max(0, g.Columns - 1) * g.GapX;
        var gridH = g.Rows * g.CellSize + Math.Max(0, g.Rows - 1) * g.GapY;
        var left = inv.FirstCell.X + Math.Min(0, minOx) - 2;
        var top = inv.FirstCell.Y + Math.Min(0, minOy) - 2;
        var width = Math.Max(1, gridW + Math.Max(0, maxOx) - Math.Min(0, minOx) + 4);
        var height = Math.Max(1, gridH + Math.Max(0, maxOy) - Math.Min(0, minOy) + 4);
        var bounds = new Rectangle(left, top, width, height);

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var gfx = Graphics.FromImage(bmp))
            gfx.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

        return new GridSnapshot(bmp, bounds);
    }

    private bool Matches(Point origin, List<RelativeColorPoint> points, int tolerance, GridSnapshot? snapshot)
    {
        if (points.Count == 0) return false;
        var need = (points.Count + 1) / 2;
        var hits = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var c = Sample(origin.X + p.OffsetX, origin.Y + p.OffsetY, snapshot);
            var expected = Color.FromArgb(p.ColorArgb);
            if (ColorDistance(c, expected) <= tolerance)
                hits++;

            var remaining = points.Count - i - 1;
            if (hits >= need) return true;
            if (hits + remaining < need) return false;
        }

        return hits >= need;
    }

    private Color Sample(int x, int y, GridSnapshot? snapshot)
    {
        if (snapshot != null && snapshot.TryGetPixel(x, y, out var c))
            return c;
        return input.GetPixel(x, y);
    }

    private static int ColorDistance(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}

internal sealed class GridSnapshot : IDisposable
{
    private readonly Bitmap _bmp;
    private readonly Rectangle _bounds;
    private readonly byte[] _buffer;
    private readonly int _stride;

    public GridSnapshot(Bitmap bmp, Rectangle bounds)
    {
        _bmp = bmp;
        _bounds = bounds;
        var data = _bmp.LockBits(new Rectangle(0, 0, _bmp.Width, _bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            _stride = data.Stride;
            _buffer = new byte[Math.Abs(_stride) * _bmp.Height];
            Marshal.Copy(data.Scan0, _buffer, 0, _buffer.Length);
        }
        finally
        {
            _bmp.UnlockBits(data);
        }
    }

    public bool TryGetPixel(int screenX, int screenY, out Color color)
    {
        var x = screenX - _bounds.X;
        var y = screenY - _bounds.Y;
        if ((uint)x >= (uint)_bmp.Width || (uint)y >= (uint)_bmp.Height)
        {
            color = Color.Empty;
            return false;
        }

        var i = y * _stride + x * 4;
        color = Color.FromArgb(_buffer[i + 2], _buffer[i + 1], _buffer[i]);
        return true;
    }

    public void Dispose() => _bmp.Dispose();
}
