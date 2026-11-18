using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DrakenHub.Models;

namespace DrakenHub.Services;

internal sealed class ColorScanService(InputService input)
{
    /// <summary>Minimum share of inset cell pixels that must match rarity color.</summary>
    private const double MinColorCoverage = 0.035;

    /// <summary>
    /// Solid icons (gems etc.) fill too much of the cell vs a "?" glyph.
    /// Real "?" screenshots peak around ~11–12%; keep headroom for glow/AA.
    /// </summary>
    private const double MaxColorCoverage = 0.16;

    /// <summary>Minimum absolute matching pixels (protects tiny cells).</summary>
    private const int MinColorPixels = 18;

    /// <summary>Share of scaled "?" stroke samples that must match color.</summary>
    private const double MinShapeHitRatio = 0.45;

    /// <summary>
    /// Fraction of the color blob that must lie on/near the scaled "?" stroke.
    /// Rejects blue/yellow auras and solid icons that only accidentally cover some stroke samples.
    /// </summary>
    private const double MinStrokePrecision = 0.72;

    /// <summary>Max share of interior hole samples that may still match (false-positive guard).</summary>
    private const double MaxHoleHitRatio = 0.28;

    public bool CellMatchesAny(InventorySettings inv, int row, int col, bool includeOtherItems)
        => TryFindClickPoint(inv, row, col, includeOtherItems, snapshot: null, out _);

    public bool CellMatchesAny(InventorySettings inv, int row, int col, bool includeOtherItems, GridSnapshot? snapshot)
        => TryFindClickPoint(inv, row, col, includeOtherItems, snapshot, out _);

    public List<(int Row, int Col, Point Center)> FindMatches(InventorySettings inv, bool includeOtherItems, GridSnapshot? snapshot)
    {
        var result = new List<(int, int, Point)>();
        for (var row = 0; row < InventoryLayout.Rows; row++)
        for (var col = 0; col < InventoryLayout.Columns; col++)
        {
            if (!TryFindClickPoint(inv, row, col, includeOtherItems, snapshot, out var center))
                continue;
            result.Add((row, col, center));
        }
        return result;
    }

    /// <summary>
    /// Searches the cell ROI for an enabled rarity "?" template (blob + shape),
    /// otherwise falls back to OtherItems multi-point profiles.
    /// Returns the click center of the best match.
    /// </summary>
    public bool TryFindClickPoint(
        InventorySettings inv,
        int row,
        int col,
        bool includeOtherItems,
        GridSnapshot? snapshot,
        out Point center)
    {
        center = default;
        var origin = InventoryLayout.CellOrigin(inv, row, col);
        if (origin == null) return false;

        var cellSize = inv.Grid.CellSize;
        if (TryFindQuestionMark(inv, origin.Value, cellSize, snapshot, out center))
            return true;

        if (includeOtherItems && TryMatchOtherItems(inv, origin.Value, snapshot, out center))
            return true;

        return false;
    }

    private bool TryFindQuestionMark(
        InventorySettings inv,
        Point origin,
        int cellSize,
        GridSnapshot? snapshot,
        out Point center)
    {
        center = default;
        foreach (var rarity in inv.Rarities)
        {
            if (!rarity.Enabled) continue;
            var template = QuestionMarkTemplates.Find(rarity.Id);
            if (template == null) continue;
            if (TryMatchTemplateInCell(origin, cellSize, template, snapshot, out center))
                return true;
        }
        return false;
    }

    private bool TryMatchTemplateInCell(
        Point origin,
        int cellSize,
        QuestionMarkTemplate template,
        GridSnapshot? snapshot,
        out Point center)
    {
        center = default;
        if (cellSize < 12) return false;

        // Ignore ornate border; search the inner icon area.
        var inset = Math.Max(2, cellSize / 10);
        var left = origin.X + inset;
        var top = origin.Y + inset;
        var right = origin.X + cellSize - inset;
        var bottom = origin.Y + cellSize - inset;
        if (right <= left || bottom <= top) return false;

        var area = (right - left) * (bottom - top);
        var minPixels = Math.Max(MinColorPixels, (int)(area * MinColorCoverage));
        var maxPixels = Math.Max(minPixels + 1, (int)(area * MaxColorCoverage));

        var blob = new List<Point>(Math.Min(area, 2048));
        long sumX = 0, sumY = 0;
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var c = Sample(x, y, snapshot);
            if (!template.MatchesColor(c)) continue;
            blob.Add(new Point(x, y));
            sumX += x;
            sumY += y;
            if (blob.Count > maxPixels) return false; // solid fill (gem/icon), not a "?"
        }

        if (blob.Count < minPixels) return false;

        var cx = (int)(sumX / blob.Count);
        var cy = (int)(sumY / blob.Count);
        // Scale shape from the 105px reference cell to the live CellSize (resolution-independent).
        var scale = cellSize / QuestionMarkTemplates.ReferenceCellSize;

        var shape = QuestionMarkTemplates.ShapeOffsets;
        var need = Math.Max(8, (int)Math.Ceiling(shape.Length * MinShapeHitRatio));
        var hits = CountOffsetHits(cx, cy, scale, shape, left, top, right, bottom, template, snapshot);
        if (hits < need) return false;

        var holes = QuestionMarkTemplates.HoleOffsets;
        var maxHoles = Math.Max(2, (int)Math.Floor(holes.Length * MaxHoleHitRatio));
        var holeHits = CountOffsetHits(cx, cy, scale, holes, left, top, right, bottom, template, snapshot);
        if (holeHits > maxHoles) return false;

        // Most of the color blob must sit on the "?" stroke (not a random blue/yellow aura).
        if (!PassesStrokePrecision(blob, cx, cy, scale, shape))
            return false;

        center = new Point(cx, cy);
        return true;
    }

    /// <summary>
    /// Precision = share of blob pixels within a scale-aware radius of any stroke sample.
    /// </summary>
    private static bool PassesStrokePrecision(List<Point> blob, int cx, int cy, float scale, Point[] shape)
    {
        var radius = Math.Max(2.0, 3.5 * scale);
        var radiusSq = radius * radius;
        var near = 0;
        for (var i = 0; i < blob.Count; i++)
        {
            var p = blob[i];
            for (var s = 0; s < shape.Length; s++)
            {
                var dx = p.X - (cx + shape[s].X * scale);
                var dy = p.Y - (cy + shape[s].Y * scale);
                if (dx * dx + dy * dy <= radiusSq)
                {
                    near++;
                    break;
                }
            }
        }

        return (double)near / blob.Count >= MinStrokePrecision;
    }

    private int CountOffsetHits(
        int cx,
        int cy,
        float scale,
        Point[] offsets,
        int left,
        int top,
        int right,
        int bottom,
        QuestionMarkTemplate template,
        GridSnapshot? snapshot)
    {
        var hits = 0;
        foreach (var offset in offsets)
        {
            var sx = cx + (int)Math.Round(offset.X * scale);
            var sy = cy + (int)Math.Round(offset.Y * scale);
            if (sx < left || sx >= right || sy < top || sy >= bottom)
                continue;
            if (template.MatchesColor(Sample(sx, sy, snapshot)))
                hits++;
        }
        return hits;
    }

    private bool TryMatchOtherItems(
        InventorySettings inv,
        Point origin,
        GridSnapshot? snapshot,
        out Point center)
    {
        center = default;
        foreach (var item in inv.OtherItems)
        {
            if (!item.Enabled || item.Points.Count == 0) continue;
            if (!Matches(origin, item.Points, inv.Grid.ColorTolerance, snapshot))
                continue;

            // Click near the average of matched profile points.
            var ax = origin.X + (int)item.Points.Average(p => p.OffsetX);
            var ay = origin.Y + (int)item.Points.Average(p => p.OffsetY);
            center = new Point(ax, ay);
            return true;
        }
        return false;
    }

    /// <summary>One screen grab for the whole inventory grid (+ OtherItems point offsets).</summary>
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

        foreach (var item in inv.OtherItems.Where(x => x.Enabled))
            Accrue(item.Points);

        var gridW = InventoryLayout.Columns * g.CellSize + Math.Max(0, InventoryLayout.Columns - 1) * g.GapX;
        var gridH = InventoryLayout.Rows * g.CellSize + Math.Max(0, InventoryLayout.Rows - 1) * g.GapY;
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
