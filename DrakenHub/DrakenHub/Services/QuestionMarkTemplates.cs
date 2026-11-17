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
    /// Sparse "?" mask relative to glyph centroid (from yellow sample, step ~3px).
    /// </summary>
    public static readonly Point[] ShapeOffsets =
    [
        new(-21, -18), new(-21, -15), new(-21, -12),
        new(-18, -18), new(-18, -15), new(-18, -12),
        new(-15, -21), new(-15, -18), new(-15, -15), new(-15, -12),
        new(-12, -21), new(-12, -18), new(-12, -15), new(-12, -12),
        new(-12, 30), new(-12, 33), new(-12, 36),
        new(-9, -21), new(-9, -18), new(-9, -15), new(-9, -12),
        new(-9, 18), new(-9, 27), new(-9, 30), new(-9, 33), new(-9, 36), new(-9, 39),
        new(-6, -21), new(-6, -18), new(-6, 9), new(-6, 12), new(-6, 15), new(-6, 18),
        new(-6, 27), new(-6, 30), new(-6, 33), new(-6, 36), new(-6, 39),
        new(-3, -21), new(-3, 3), new(-3, 6), new(-3, 9), new(-3, 12), new(-3, 15), new(-3, 18),
        new(-3, 27), new(-3, 30), new(-3, 33), new(-3, 36), new(-3, 39),
        new(0, -21), new(0, -18), new(0, -3), new(0, 0), new(0, 3), new(0, 6), new(0, 9),
        new(0, 12), new(0, 15), new(0, 30), new(0, 33), new(0, 36), new(0, 39),
        new(3, -21), new(3, -18), new(3, -15), new(3, -12), new(3, -9), new(3, -6),
        new(3, -3), new(3, 0), new(3, 3), new(3, 6), new(3, 9),
        new(6, -21), new(6, -18), new(6, -15), new(6, -12), new(6, -9), new(6, -6),
        new(6, -3), new(6, 0), new(6, 3), new(6, 6),
        new(9, -21), new(9, -18), new(9, -15), new(9, -12), new(9, -9), new(9, -6),
        new(9, -3), new(9, 0), new(9, 3),
        new(12, -21), new(12, -18), new(12, -15), new(12, -12), new(12, -9), new(12, -6),
        new(12, -3), new(12, 0),
        new(15, -18), new(15, -15), new(15, -12), new(15, -9), new(15, -6), new(15, -3),
        new(18, -12), new(18, -9),
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
        // Medium blue "?"
        if (c.B < 120) return false;
        if (c.B < c.R + 35 || c.B < c.G + 15) return false;
        return Saturation(c) >= 45;
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
