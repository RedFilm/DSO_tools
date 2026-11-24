using System.Text.Json;
using DrakenHub.Models;

namespace DrakenHub.Services;

internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DrakenHub", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public event Action? Changed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(DefaultPath))
            {
                var json = File.ReadAllText(DefaultPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                Current = new AppSettings();
                DetectResolutionPreset(Current);
            }
        }
        catch
        {
            Current = new AppSettings();
        }

        EnsureIntegrity(Current);
        return Current;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultPath)!);
        File.WriteAllText(DefaultPath, JsonSerializer.Serialize(Current, JsonOptions));
        Changed?.Invoke();
    }

    public void Export(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void Import(string path)
    {
        var json = File.ReadAllText(path);
        Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        EnsureIntegrity(Current);
        Save();
    }

    public void NotifyChanged() => Changed?.Invoke();

    public static void DetectResolutionPreset(AppSettings settings)
    {
        var w = Screen.PrimaryScreen?.Bounds.Width ?? 0;
        var h = Screen.PrimaryScreen?.Bounds.Height ?? 0;
        settings.ResolutionPreset = (w, h) switch
        {
            (1366, 768) => "1366x768",
            (1920, 1080) => "1920x1080",
            (2560, 1440) => "2560x1440",
            (3840, 2160) => "3840x2160",
            _ => "Auto / Custom"
        };
    }

    private static void EnsureIntegrity(AppSettings s)
    {
        s.Inventory.SelectedTabs = NormalizeTabs(s.Inventory.SelectedTabs);
        s.Inventory.Rarities = NormalizeRarities(s.Inventory.Rarities);
        EnsureItemGroups(s.Inventory);
        if (s.AutoCast.Rows.Count < 5)
        {
            while (s.AutoCast.Rows.Count < 5)
                s.AutoCast.Rows.Add(new AutoCastRow { SkillHotkey = $"{s.AutoCast.Rows.Count + 1}", IntervalMs = 2000 });
        }
    }

    /// <summary>
    /// Merge saved Enabled flags onto the built-in rarity list; drop legacy per-rarity color points.
    /// </summary>
    private static List<RarityProfile> NormalizeRarities(List<RarityProfile>? saved)
    {
        var defaults = RarityProfile.CreateDefaults();
        if (saved == null || saved.Count == 0)
            return defaults;

        foreach (var def in defaults)
        {
            var match = saved.FirstOrDefault(r =>
                r.Id.Equals(def.Id, StringComparison.OrdinalIgnoreCase) ||
                r.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                def.Enabled = match.Enabled;
            def.Points = [];
            if (!QuestionMarkTemplates.HasTemplate(def.Id))
                def.Enabled = false;
            else if (def.Id.Equals("set", StringComparison.OrdinalIgnoreCase) && match is { Enabled: false })
                def.Enabled = true; // previously disabled only because Set template was missing
        }
        return defaults;
    }

    /// <summary>
    /// Every custom item lives in a group. Older saves had none, so they all land in the first one.
    /// </summary>
    private static void EnsureItemGroups(InventorySettings inv)
    {
        inv.ItemGroups ??= [];
        if (inv.ItemGroups.Count == 0)
            inv.ItemGroups.Add(new ItemGroup());

        var ids = inv.ItemGroups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var fallback = inv.ItemGroups[0].Id;
        foreach (var item in inv.OtherItems)
        {
            if (string.IsNullOrEmpty(item.GroupId) || !ids.Contains(item.GroupId))
                item.GroupId = fallback;
        }

        if (string.IsNullOrEmpty(inv.ActiveItemGroupId)
            || (inv.ActiveItemGroupId != ItemGroup.SystemId && !ids.Contains(inv.ActiveItemGroupId)))
            inv.ActiveItemGroupId = fallback;
    }

    private static bool[] NormalizeTabs(bool[]? tabs)
    {
        var result = new bool[9];
        if (tabs == null) return result;
        for (var i = 0; i < Math.Min(9, tabs.Length); i++)
            result[i] = tabs[i];
        return result;
    }
}
