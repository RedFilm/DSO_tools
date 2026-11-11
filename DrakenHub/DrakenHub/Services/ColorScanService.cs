using DrakenHub.Models;

namespace DrakenHub.Services;

internal sealed class ColorScanService(InputService input)
{
    public bool CellMatchesAny(InventorySettings inv, int row, int col, bool includeOtherItems)
    {
        var origin = InventoryLayout.CellOrigin(inv, row, col);
        if (origin == null) return false;

        foreach (var rarity in inv.Rarities.Where(r => r.Enabled && r.Points.Count > 0))
        {
            if (Matches(origin.Value, rarity.Points, inv.Grid.ColorTolerance))
                return true;
        }

        if (includeOtherItems)
        {
            foreach (var item in inv.OtherItems.Where(i => i.Enabled && i.Points.Count > 0))
            {
                if (Matches(origin.Value, item.Points, inv.Grid.ColorTolerance))
                    return true;
            }
        }

        return false;
    }

    private bool Matches(Point origin, List<RelativeColorPoint> points, int tolerance)
    {
        if (points.Count == 0) return false;
        var hits = 0;
        foreach (var p in points)
        {
            var c = input.GetPixel(origin.X + p.OffsetX, origin.Y + p.OffsetY);
            var expected = Color.FromArgb(p.ColorArgb);
            if (ColorDistance(c, expected) <= tolerance)
                hits++;
        }

        // Require majority of points to match for reliability.
        return hits * 2 >= points.Count;
    }

    private static int ColorDistance(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}
