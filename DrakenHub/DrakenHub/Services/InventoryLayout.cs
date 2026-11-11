using DrakenHub.Models;

namespace DrakenHub.Services;

internal static class InventoryLayout
{
    public static Point? CellCenter(InventorySettings inv, int row, int col)
    {
        if (inv.FirstCell == null) return null;
        var g = inv.Grid;
        var x = inv.FirstCell.X + col * (g.CellSize + g.GapX) + g.CellSize / 2;
        var y = inv.FirstCell.Y + row * (g.CellSize + g.GapY) + g.CellSize / 2;
        return new Point(x, y);
    }

    public static Point? CellOrigin(InventorySettings inv, int row, int col)
    {
        if (inv.FirstCell == null) return null;
        var g = inv.Grid;
        var x = inv.FirstCell.X + col * (g.CellSize + g.GapX);
        var y = inv.FirstCell.Y + row * (g.CellSize + g.GapY);
        return new Point(x, y);
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
        for (var row = 0; row < g.Rows; row++)
        for (var col = 0; col < g.Columns; col++)
        {
            var ox = inv.FirstCell.X + col * (g.CellSize + g.GapX);
            var oy = inv.FirstCell.Y + row * (g.CellSize + g.GapY);
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
            for (var row = 0; row < g.Rows; row++)
            for (var col = 0; col < g.Columns; col++)
            {
                var ox = inv.FirstCell.X + col * (g.CellSize + g.GapX);
                var oy = inv.FirstCell.Y + row * (g.CellSize + g.GapY);
                cells.Add(new Rectangle(ox, oy, g.CellSize, g.CellSize));
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
            // Show sample markers on the first cell using relative offsets.
            foreach (var rarity in inv.Rarities.Where(r => r.Enabled))
            foreach (var p in rarity.Points)
                samples.Add(new Point(inv.FirstCell.X + p.OffsetX, inv.FirstCell.Y + p.OffsetY));

            foreach (var item in inv.OtherItems.Where(i => i.Enabled))
            foreach (var p in item.Points)
                samples.Add(new Point(inv.FirstCell.X + p.OffsetX, inv.FirstCell.Y + p.OffsetY));
        }

        return (cells, tabs, samples);
    }
}
