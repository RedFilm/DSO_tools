using DrakenHub.Models;

namespace DrakenHub.Services;

internal static class InventoryLayout
{
    public const int Columns = 7;
    public const int Rows = 4;

    // At cell size 105 the slots step 107, then 108. The saved gap keeps only the
    // whole pixels, and the anchor was taken on the inner rim, 5px down-right of
    // the slot center. Both are put back here so the first cells share the lattice.
    private const double SlotPitch = 107.5;
    private const double SlotCell = 105.0;
    private const double AnchorBias = 5.0;

    public static int CellOffset(int index, int cell, int gap) => Span(index, cell, gap);

    public static Point? CellCenter(InventorySettings inv, int row, int col)
    {
        var origin = CellOrigin(inv, row, col);
        if (origin == null) return null;
        var g = inv.Grid;
        return new Point(origin.Value.X + g.CellSize / 2, origin.Value.Y + g.CellSize / 2);
    }

    public static Point? CellOrigin(InventorySettings inv, int row, int col)
    {
        if (inv.FirstCell == null) return null;
        var g = inv.Grid;
        var x = inv.FirstCell.X + Span(col, g.CellSize, g.GapX);
        var y = inv.FirstCell.Y + Span(row, g.CellSize, g.GapY);
        return new Point(x, y);
    }

    private static int Span(int index, int cell, int gap)
    {
        if (gap > Math.Max(2, cell / 30))
            return index * (cell + gap);

        var scale = cell / SlotCell;
        return (int)Math.Round(index * SlotPitch * scale - AnchorBias * scale, MidpointRounding.AwayFromZero);
    }

    public static Point? TabCenter(InventorySettings inv, int tabIndexZeroBased)
    {
        if (inv.FirstTab == null) return null;
        var x = inv.FirstTab.X + tabIndexZeroBased * inv.Grid.TabOffsetX;
        var y = inv.FirstTab.Y;
        return new Point(x, y);
    }

    public static CellRef? PointToCell(InventorySettings inv, Point screen)
    {
        if (inv.FirstCell == null) return null;
        var g = inv.Grid;
        for (var row = 0; row < Rows; row++)
        for (var col = 0; col < Columns; col++)
        {
            var ox = inv.FirstCell.X + Span(col, g.CellSize, g.GapX);
            var oy = inv.FirstCell.Y + Span(row, g.CellSize, g.GapY);
            if (screen.X >= ox && screen.X < ox + g.CellSize &&
                screen.Y >= oy && screen.Y < oy + g.CellSize)
                return new CellRef { Row = row + 1, Col = col + 1 };
        }
        return null;
    }

    public static (List<Rectangle> Cells, List<Rectangle> Tabs, List<Point> Samples) BuildOverlay(InventorySettings inv)
    {
        var cells = new List<Rectangle>();
        var tabs = new List<Rectangle>();
        var samples = new List<Point>();
        if (inv.FirstCell != null)
        {
            var g = inv.Grid;
            // A couple of pixels so neighbouring strokes don't fuse. The border itself stays put.
            var inset = Math.Max(2, g.CellSize / 28);
            var drawW = Math.Max(8, g.CellSize - inset * 2);
            var drawH = drawW;
            for (var row = 0; row < Rows; row++)
            for (var col = 0; col < Columns; col++)
            {
                var ox = inv.FirstCell.X + Span(col, g.CellSize, g.GapX) + inset;
                var oy = inv.FirstCell.Y + Span(row, g.CellSize, g.GapY) + inset;
                cells.Add(new Rectangle(ox, oy, drawW, drawH));
            }
        }

        if (inv.FirstTab != null)
        {
            var size = inv.Grid.TabMarkerSize;
            for (var i = 0; i < 9; i++)
            {
                var x = inv.FirstTab.X + i * inv.Grid.TabOffsetX - size / 2;
                var y = inv.FirstTab.Y - size / 2;
                tabs.Add(new Rectangle(x, y, size, size));
            }
        }

        if (inv.FirstCell != null)
        {
            // OtherItems still use manual color points; rarities use built-in "?" templates.
            foreach (var item in inv.OtherItems.Where(i => i.Enabled))
            foreach (var p in item.Points)
                samples.Add(new Point(inv.FirstCell.X + p.OffsetX, inv.FirstCell.Y + p.OffsetY));
        }

        return (cells, tabs, samples);
    }
}
