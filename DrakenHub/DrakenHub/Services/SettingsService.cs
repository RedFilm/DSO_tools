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
        if (s.Inventory.Rarities.Count == 0)
            s.Inventory.Rarities = RarityProfile.CreateDefaults();
        if (s.AutoCast.Rows.Count < 5)
        {
            while (s.AutoCast.Rows.Count < 5)
                s.AutoCast.Rows.Add(new AutoCastRow { SkillHotkey = $"{s.AutoCast.Rows.Count + 1}", IntervalMs = 2000 });
        }
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
