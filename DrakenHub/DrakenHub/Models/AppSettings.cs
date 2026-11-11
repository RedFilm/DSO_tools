using System.Text.Json.Serialization;

namespace DrakenHub.Models;

public sealed class AppSettings
{
    public string Language { get; set; } = "ru";
    public string ResolutionPreset { get; set; } = "Auto / Custom";
    public int WindowWidth { get; set; } = 980;
    public int WindowHeight { get; set; } = 720;
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;

    /// <summary>Substring of the game window title (fullscreen). Empty = do not bind/focus.</summary>
    public string GameWindowTitleContains { get; set; } = "Drakensang Online";

    public WindowHotkeys WindowHotkeys { get; set; } = new();
    public InventorySettings Inventory { get; set; } = new();
    public AutoClickerSettings AutoClicker { get; set; } = new();
    public FastMountSettings FastMount { get; set; } = new();
    public GearSwapSettings GearSwap { get; set; } = new();
    public ExtraSkillSettings ExtraSkill { get; set; } = new();
    public AutoCastSettings AutoCast { get; set; } = new();
    public FastTeleportSettings FastTeleport { get; set; } = new();
}

public sealed class WindowHotkeys
{
    public string? HideShow { get; set; } = "Insert";
    public string? Exit { get; set; } = "End";
    public string? EmergencyStop { get; set; } = "Home";
}

public sealed class InventorySettings
{
    public string? SellAllHotkey { get; set; }
    public string? SellCurrentHotkey { get; set; }
    public string? InventoryHotkey { get; set; } = "E";
    public ScreenPoint? CloseInventory { get; set; }
    public ScreenPoint? FirstTab { get; set; }
    public ScreenPoint? FirstCell { get; set; }
    public string? SkillsWindowHotkey { get; set; } = "S";
    public ScreenPoint? WisdomTab { get; set; }
    public ScreenPoint? ShopButton { get; set; }

    public GridSettings Grid { get; set; } = new();
    public bool[] SelectedTabs { get; set; } = [true, false, true, true, true, false, false, false, false];
    public List<RarityProfile> Rarities { get; set; } = RarityProfile.CreateDefaults();
    public List<CustomItemProfile> OtherItems { get; set; } = [];

    public ScreenPoint? SmelterItem { get; set; }
    public ScreenPoint? SmeltButton { get; set; }
    public string? SmeltHotkey { get; set; }
}

public sealed class GridSettings
{
    public bool ShowGrid { get; set; }
    public bool FastFirstScan { get; set; } = true;
    public int CellSize { get; set; } = 48;
    public int TabMarkerSize { get; set; } = 24;
    public int GapX { get; set; } = 4;
    public int GapY { get; set; } = 4;
    public int TabOffsetX { get; set; } = 40;
    public int CrosshairSize { get; set; } = 24;
    public int ScanLevel { get; set; } = 1;
    public int Columns { get; set; } = 7;
    public int Rows { get; set; } = 4;
    public int ColorTolerance { get; set; } = 18;
    public int SellClickDelayMs { get; set; } = 40;
}

public sealed class RarityProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int DisplayColorArgb { get; set; }
    public List<RelativeColorPoint> Points { get; set; } = [];

    public static List<RarityProfile> CreateDefaults() =>
    [
        new() { Id = "uncommon", Name = "Uncommon", DisplayColorArgb = Color.LimeGreen.ToArgb() },
        new() { Id = "magic", Name = "Magic", DisplayColorArgb = Color.DodgerBlue.ToArgb() },
        new() { Id = "rare", Name = "Rare", DisplayColorArgb = Color.Magenta.ToArgb() },
        new() { Id = "epic", Name = "Epic", DisplayColorArgb = Color.Orange.ToArgb() },
        new() { Id = "unique", Name = "Unique", DisplayColorArgb = Color.Gold.ToArgb() },
        new() { Id = "set", Name = "Set", DisplayColorArgb = Color.Cyan.ToArgb() },
    ];
}

public sealed class CustomItemProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Item";
    public bool Enabled { get; set; } = true;
    public List<RelativeColorPoint> Points { get; set; } = [];
}

public sealed class RelativeColorPoint
{
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int ColorArgb { get; set; }
}

public sealed class ScreenPoint
{
    public int X { get; set; }
    public int Y { get; set; }

    public ScreenPoint() { }
    public ScreenPoint(int x, int y) { X = x; Y = y; }

    public override string ToString() => $"X {X}, Y {Y}";

    [JsonIgnore]
    public Point AsPoint => new(X, Y);
}

public sealed class AutoClickerSettings
{
    public string LeftMode { get; set; } = "hold";
    public string? LeftHotkey { get; set; }
    public int LeftDelayMs { get; set; } = 21;
    public string RightMode { get; set; } = "hold";
    public string? RightHotkey { get; set; }
    public int RightDelayMs { get; set; } = 30;
}

public sealed class FastMountSettings
{
    public CellRef? CapeCell { get; set; }
    public int CapeTab { get; set; } = 1;
    public string? MountHotkey { get; set; }
    public string? FastMountHotkey { get; set; }
    public int ClickDelayMs { get; set; } = 30;
    public string? MainPanelHotkey { get; set; } = "F1";
    public string? JumpTargetPanelHotkey { get; set; }
    public string? JumpSkillHotkey { get; set; }
    public string? JumpMountHotkey { get; set; }
}

public sealed class CellRef
{
    public int Row { get; set; }
    public int Col { get; set; }
    public override string ToString() => $"{Row}:{Col}";
}

public sealed class GearSwapSettings
{
    public string? InventoryHotkey { get; set; } = "E";
    public string? MainPanelHotkey { get; set; } = "F1";
    public int ClickDelayMs { get; set; } = 30;
    public List<GearSwapProfile> Profiles { get; set; } = [];
}

public sealed class GearSwapProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Gear Swap";
    public string? Hotkey { get; set; }
    public string? TargetPanelHotkey { get; set; }
    public string? SkillSlotHotkey { get; set; }
    public int InventoryTab { get; set; } = 1;
    public List<CellRef> Items { get; set; } = [];
}

public sealed class ExtraSkillSettings
{
    public string? MainPanelHotkey { get; set; } = "F1";
    public List<ExtraSkillProfile> Profiles { get; set; } = [];
}

public sealed class ExtraSkillProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Extra Skill";
    public string? Hotkey { get; set; }
    public string? TargetPanelHotkey { get; set; }
    public string? SkillSlotHotkey { get; set; }
}

public sealed class AutoCastSettings
{
    public List<AutoCastRow> Rows { get; set; } =
    [
        new() { SkillHotkey = "1", IntervalMs = 2000 },
        new() { SkillHotkey = "2", IntervalMs = 2000 },
        new() { SkillHotkey = "3", IntervalMs = 2000 },
        new() { SkillHotkey = "4", IntervalMs = 2000 },
        new() { SkillHotkey = "5", IntervalMs = 4000 },
    ];
}

public sealed class AutoCastRow
{
    public string? SkillHotkey { get; set; }
    public int IntervalMs { get; set; } = 2000;
    public bool Enabled { get; set; }
    public string? ToggleHotkey { get; set; }
}

public sealed class FastTeleportSettings
{
    public ScreenPoint? MapItem { get; set; }
    public ScreenPoint? BackButton { get; set; }
    public ScreenPoint? EnterButton { get; set; }
    public int ClickDelayMs { get; set; } = 50;
    public List<TeleportLocation> Locations { get; set; } = [];
}

public sealed class TeleportLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Location";
    public string? Hotkey { get; set; }
    public ScreenPoint? Region { get; set; }
    public ScreenPoint? Location { get; set; }
    public ScreenPoint? Difficulty { get; set; }
}
