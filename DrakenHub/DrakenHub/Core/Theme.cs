namespace DrakenHub.Core;

/// <summary>
/// Soft dark companion theme — lower luminance contrast, warm neutrals, calm accent.
/// Tuned for long sessions (less eye strain than pure black + near-white).
/// </summary>
internal static class Theme
{
    // Warm charcoal surfaces (not pure black)
    public static readonly Color Bg = Color.FromArgb(24, 26, 30);
    public static readonly Color Panel = Color.FromArgb(32, 35, 40);
    public static readonly Color PanelAlt = Color.FromArgb(38, 42, 48);
    public static readonly Color InputBg = Color.FromArgb(28, 31, 36);
    public static readonly Color Border = Color.FromArgb(52, 56, 64);
    public static readonly Color BorderHover = Color.FromArgb(78, 72, 62);

    // Soft ink — primary stays calm; secondary must stay readable (not "background").
    public static readonly Color Text = Color.FromArgb(208, 214, 222);
    public static readonly Color TextMuted = Color.FromArgb(168, 176, 188);
    public static readonly Color TextSubtle = Color.FromArgb(148, 156, 168);

    public static readonly Color NavIdle = Color.FromArgb(32, 35, 40);
    public static readonly Color NavHover = Color.FromArgb(42, 46, 52);
    public static readonly Color NavActive = Color.FromArgb(48, 42, 34);

    public static readonly Color Button = Color.FromArgb(42, 46, 52);
    public static readonly Color ButtonBorder = Color.FromArgb(62, 66, 74);

    /// <summary>Muted warm accent — less saturated than pure gold.</summary>
    public static readonly Color Accent = Color.FromArgb(186, 152, 112);
    public static readonly Color AccentDim = Color.FromArgb(52, 44, 34);

    public static readonly Color Success = Color.FromArgb(110, 158, 128);
    public static readonly Color Danger = Color.FromArgb(176, 110, 110);
    public static readonly Color DangerDim = Color.FromArgb(52, 36, 38);

    // Legacy aliases
    public static readonly Color AccentBlue = Accent;
    public static readonly Color AccentOrange = Accent;
    public static readonly Color AccentRed = Danger;
    public static readonly Color AccentYellow = Accent;
    public static readonly Color Green = Success;
    public static readonly Color Cyan = Accent;
    public static readonly Color Orange = Accent;
    public static readonly Color Red = Danger;
    public static readonly Color Tan = Accent;
    public static readonly Color Yellow = Accent;
    public static readonly Color TabIdle = NavIdle;
    public static readonly Color TabActive = NavActive;
    public static readonly Color BgElevated = Panel;

    public static readonly Font UiFont = SafeFont(10f, FontStyle.Regular);
    public static readonly Font UiFontSmall = SafeFont(9f, FontStyle.Regular);
    public static readonly Font UiFontTitle = SafeFont(11.25f, FontStyle.Bold);
    public static readonly Font UiFontNav = SafeFont(10f, FontStyle.Regular);
    public static readonly Font UiFontValue = SafeFont(10f, FontStyle.Bold);
    /// <summary>Deprecated alias — prefer UiFontValue (no Consolas).</summary>
    public static readonly Font UiFontMono = UiFontValue;

    private static Font SafeFont(float size, FontStyle style)
    {
        try
        {
            return new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
        catch
        {
            return SystemFonts.MessageBoxFont ?? new Font(FontFamily.GenericSansSerif, size, style);
        }
    }
}
