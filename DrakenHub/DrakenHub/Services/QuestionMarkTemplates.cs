using System.Drawing;

namespace DrakenHub.Services;

/// <summary>
/// Built-in unidentified-item "?" templates per rarity.
/// Shape offsets are sampled from a ~105px cell and scaled to the live CellSize.
/// Cyan/Set is omitted until a screenshot is provided.
/// </summary>
internal static class QuestionMarkTemplates
{
    /// <summary>Reference cell size used when capturing shape offsets.</summary>
    public const float ReferenceCellSize = 105f;

    /// <summary>
    /// Dense "?" stroke relative to glyph centroid (from yellow sample @ 105px, step 2px).
    /// Live matching scales these by <c>cellSize / ReferenceCellSize</c>.
    /// </summary>
    public static readonly Point[] ShapeOffsets =
    [
        new(-22, -16), new(-22, -14), new(-22, -12),
        new(-20, -16), new(-20, -14), new(-20, -12),
        new(-18, -16), new(-18, -14), new(-18, -12),
        new(-16, -16), new(-16, -14), new(-16, -12),
        new(-14, -20), new(-14, -16), new(-14, -14), new(-14, -12),
        new(-12, -18), new(-12, -16), new(-12, -14), new(-12, -12), new(-12, 36),
        new(-10, -20), new(-10, -18), new(-10, -16), new(-10, -14),
        new(-10, 28), new(-10, 30), new(-10, 32), new(-10, 34), new(-10, 36), new(-10, 38),
        new(-8, -20), new(-8, -18), new(-8, 32), new(-8, 34), new(-8, 36), new(-8, 38), new(-8, 40),
        new(-6, -22), new(-6, -20), new(-6, 30), new(-6, 32), new(-6, 34), new(-6, 36), new(-6, 38), new(-6, 40),
        new(-4, -22), new(-4, 6), new(-4, 8), new(-4, 10), new(-4, 12), new(-4, 14), new(-4, 16), new(-4, 18),
        new(-4, 28), new(-4, 30), new(-4, 32), new(-4, 34), new(-4, 36), new(-4, 38), new(-4, 40),
        new(-2, -22), new(-2, 2), new(-2, 4), new(-2, 6), new(-2, 8), new(-2, 10), new(-2, 12), new(-2, 14),
        new(-2, 30), new(-2, 32), new(-2, 34), new(-2, 36), new(-2, 38),
        new(0, -22), new(0, 2), new(0, 4), new(0, 6), new(0, 8), new(0, 10), new(0, 12),
        new(2, -22), new(2, -20), new(2, -18), new(2, -16), new(2, -14), new(2, -6), new(2, -4), new(2, -2),
        new(2, 0), new(2, 2), new(2, 4), new(2, 6), new(2, 8),
        new(4, -20), new(4, -18), new(4, -16), new(4, -14), new(4, -12), new(4, -10), new(4, -8), new(4, -6),
        new(4, -4), new(4, -2), new(4, 0), new(4, 2), new(4, 4), new(4, 6),
        new(6, -20), new(6, -18), new(6, -16), new(6, -14), new(6, -12), new(6, -10), new(6, -8), new(6, -6),
        new(6, -4), new(6, -2), new(6, 0), new(6, 2), new(6, 4),
        new(8, -20), new(8, -18), new(8, -16), new(8, -14), new(8, -12), new(8, -10), new(8, -8), new(8, -6),
        new(8, -4), new(8, -2), new(8, 0), new(8, 2),
        new(10, -20), new(10, -18), new(10, -16), new(10, -14), new(10, -12), new(10, -10), new(10, -8),
        new(10, -6), new(10, -4), new(10, -2), new(10, 0),
        new(12, -18), new(12, -16), new(12, -14), new(12, -12), new(12, -10), new(12, -8), new(12, -6),
        new(12, -4), new(12, -2),
        new(14, -16), new(14, -14), new(14, -12), new(14, -10), new(14, -8), new(14, -6), new(14, -4),
    ];

    /// <summary>
    /// Interior holes of the "?" (left bowl, gap above the dot). Solid icons light these up; real "?" mostly does not.
    /// </summary>
    public static readonly Point[] HoleOffsets =
    [
        new(-18, 0), new(-15, 0), new(-12, 0), new(-9, 0), new(-6, 0),
        new(-18, 3), new(-15, 3), new(-12, 3), new(-9, 3), new(-6, 3),
        new(-15, 6), new(-12, 6), new(-9, 6),
        new(-15, 12), new(-12, 12), new(-9, 12),
        new(-6, 21), new(-3, 21), new(0, 21), new(3, 21),
        new(6, 18), new(9, 18), new(12, 12), new(12, 18),
        new(-3, -6), new(0, -9), new(3, -12),
    ];

    public static readonly QuestionMarkTemplate[] All =
    [
        new("uncommon", "Uncommon", Color.FromArgb(141, 176, 125), MatchesUncommon),
        new("magic", "Magic", Color.FromArgb(66, 138, 206), MatchesMagic),
        new("rare", "Rare", Color.FromArgb(181, 52, 99), MatchesRare),
        new("epic", "Epic", Color.FromArgb(255, 149, 60), MatchesEpic),
        new("unique", "Unique", Color.FromArgb(255, 255, 0), MatchesUnique),
        new("set", "Set", Color.FromArgb(59, 187, 220), MatchesSet),
    ];

    public static QuestionMarkTemplate? Find(string rarityId)
        => All.FirstOrDefault(t => t.Id.Equals(rarityId, StringComparison.OrdinalIgnoreCase));

    public static bool HasTemplate(string rarityId) => Find(rarityId) != null;

    // --- Color predicates tuned from unidentified "?" screenshots ---

    private static bool MatchesUncommon(Color c)
    {
        // Pale mint-green, lower saturation than others.
        if (c.G < 130 || c.R < 90 || c.B < 90) return false;
        if (c.G < c.R + 8 || c.G < c.B + 8) return false;
        return Saturation(c) >= 22 && c.G - Math.Min(c.R, c.B) >= 18;
    }

    private static bool MatchesMagic(Color c)
    {
        // Saturated medium-blue "?" — rejects dark/blue item auras (e.g. glowing gear).
        if (c.B < 140 || c.R > 120 || c.G > 160) return false;
        if (c.B < c.R + 50 || c.B < c.G + 25) return false;
        if ((c.R + c.G + c.B) / 3 < 90) return false;
        return Saturation(c) >= 55 && c.B - Math.Max(c.R, c.G) >= 30;
    }

    private static bool MatchesRare(Color c)
    {
        // Magenta / pink "?"
        if (c.R < 100 || c.B < 45 || c.G > 95) return false;
        if (c.R < c.G + 35 || c.B <= c.G) return false;
        return Saturation(c) >= 45;
    }

    private static bool MatchesEpic(Color c)
    {
        // Bright orange "?"
        if (c.R < 200 || c.G < 90 || c.G > 220 || c.B > 120) return false;
        if (c.R <= c.G || c.R - c.B < 90) return false;
        return Saturation(c) >= 70;
    }

    private static bool MatchesUnique(Color c)
    {
        // Pure bright yellow "?" — rejects warmer/dimmer yellow gems (higher blue, lower R/G).
        if (c.R < 230 || c.G < 225 || c.B > 90) return false;
        if (Math.Abs(c.R - c.G) > 18) return false;
        return (c.R + c.G) / 2 - c.B >= 140 && Saturation(c) >= 140;
    }

    private static bool MatchesSet(Color c)
    {
        // Cyan "?" — high G+B, low R; G close to B (unlike Magic where B >> G).
        if (c.G < 155 || c.B < 185 || c.R > 140) return false;
        if (c.B < c.R + 60 || c.G < c.R + 40) return false;
        if (Math.Abs(c.B - c.G) > 55) return false;
        if (c.B < c.G - 15) return false;
        return Saturation(c) >= 70;
    }

    private static int Saturation(Color c)
        => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
}

internal sealed class QuestionMarkTemplate
{
    public string Id { get; }
    public string Name { get; }
    public Color DisplayColor { get; }
    public Func<Color, bool> MatchesColor { get; }

    public QuestionMarkTemplate(string id, string name, Color displayColor, Func<Color, bool> matchesColor)
    {
        Id = id;
        Name = name;
        DisplayColor = displayColor;
        MatchesColor = matchesColor;
    }
}
