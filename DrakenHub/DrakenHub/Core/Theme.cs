namespace DrakenHub.Core;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(17, 19, 22);
    public static readonly Color Panel = Color.FromArgb(22, 24, 28);
    public static readonly Color PanelAlt = Color.FromArgb(28, 30, 34);
    public static readonly Color InputBg = Color.FromArgb(16, 18, 21);
    public static readonly Color Border = Color.FromArgb(58, 64, 72);
    public static readonly Color Text = Color.FromArgb(220, 224, 230);
    public static readonly Color TextMuted = Color.FromArgb(148, 156, 168);
    public static readonly Color NavIdle = Color.FromArgb(22, 24, 28);
    public static readonly Color NavHover = Color.FromArgb(32, 36, 42);
    public static readonly Color NavActive = Color.FromArgb(42, 78, 68);
    public static readonly Color Button = Color.FromArgb(48, 52, 58);
    public static readonly Color ButtonBorder = Color.FromArgb(82, 88, 96);
    public static readonly Color Accent = Color.FromArgb(72, 214, 154);
    public static readonly Color AccentBlue = Color.FromArgb(96, 164, 255);
    public static readonly Color AccentOrange = Color.FromArgb(232, 164, 72);
    public static readonly Color AccentRed = Color.FromArgb(232, 104, 104);
    public static readonly Color AccentYellow = Color.FromArgb(255, 196, 92);

    // Semantic aliases kept for call sites
    public static readonly Color Green = Accent;
    public static readonly Color Cyan = AccentBlue;
    public static readonly Color Orange = AccentOrange;
    public static readonly Color Red = AccentRed;
    public static readonly Color Tan = Color.FromArgb(180, 170, 140);
    public static readonly Color Yellow = AccentYellow;
    public static readonly Color TabIdle = NavIdle;
    public static readonly Color TabActive = NavActive;
    public static readonly Color BgElevated = Panel;

    public static readonly Font UiFont = new("Segoe UI", 9.25f, FontStyle.Regular);
    public static readonly Font UiFontSmall = new("Segoe UI", 8.5f, FontStyle.Regular);
    public static readonly Font UiFontTitle = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font UiFontNav = new("Segoe UI", 9.5f, FontStyle.Regular);
}
