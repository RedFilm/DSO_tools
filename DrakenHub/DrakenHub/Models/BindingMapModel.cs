namespace DrakenHub.Models;

internal sealed class BindingEntry
{
    public required string Id { get; init; }
    public required string GroupKey { get; init; }
    public required string LabelKey { get; init; }
    public string? LabelOverride { get; init; }
    public string? KeyName { get; set; }
    public required Action<string?> Assign { get; init; }
    public bool IsConflict { get; set; }
}

internal static class BindingMapCollector
{
    public static List<BindingEntry> Collect(AppSettings s, bool includeMuted)
    {
        var list = new List<BindingEntry>();

        void Add(string id, string group, string labelKey, string? key, Action<string?> assign, string? labelOverride = null)
        {
            list.Add(new BindingEntry
            {
                Id = id,
                GroupKey = group,
                LabelKey = labelKey,
                LabelOverride = labelOverride,
                KeyName = key,
                Assign = assign
            });
        }

        Add("win.stop", "bind.group.window", "stop", s.WindowHotkeys.EmergencyStop, v => s.WindowHotkeys.EmergencyStop = v);

        Add("sell.all", "bind.group.sell", "sell.all", s.Inventory.SellAllHotkey, v => s.Inventory.SellAllHotkey = v);
        Add("util.inv", "bind.group.sell", "btn.inventory", s.Inventory.InventoryHotkey, v =>
        {
            s.Inventory.InventoryHotkey = v;
            s.GearSwap.InventoryHotkey = v;
        });

        Add("fastmount", "bind.group.mount", "fastmount", s.FastMount.FastMountHotkey, v => s.FastMount.FastMountHotkey = v);
        Add("jumpmount", "bind.group.mount", "jumpmount", s.FastMount.JumpMountHotkey, v => s.FastMount.JumpMountHotkey = v);
        Add("util.mount", "bind.group.mount", "mount", s.FastMount.MountHotkey, v => s.FastMount.MountHotkey = v);

        Add("util.main", "bind.group.extraskill", "mainPanel", s.ExtraSkill.MainPanelHotkey, v =>
        {
            s.ExtraSkill.MainPanelHotkey = v;
            s.FastMount.MainPanelHotkey = v;
            s.GearSwap.MainPanelHotkey = v;
        });
        foreach (var p in s.ExtraSkill.Profiles)
        {
            var profile = p;
            Add("es:" + profile.Id, "bind.group.extraskill", "hotkey", profile.Hotkey,
                v => profile.Hotkey = v, profile.Name);
        }

        foreach (var p in s.FastTeleport.Locations)
        {
            var loc = p;
            Add("tp:" + loc.Id, "bind.group.teleport", "hotkey", loc.Hotkey,
                v => loc.Hotkey = v, loc.Name);
        }

        Add("speedboost.toggle", "bind.group.speedboost", "sb.hotkey", s.SpeedBoost.ToggleHotkey,
            v => s.SpeedBoost.ToggleHotkey = v);

        Add("aclick.left", "bind.group.clicker", "click.left", s.AutoClicker.LeftHotkey, v => s.AutoClicker.LeftHotkey = v);
        Add("aclick.right", "bind.group.clicker", "click.right", s.AutoClicker.RightHotkey, v => s.AutoClicker.RightHotkey = v);

        if (includeMuted)
        {
            if (!string.IsNullOrWhiteSpace(s.Inventory.SmeltHotkey))
                Add("smelt", "bind.group.sell", "smelt.start", s.Inventory.SmeltHotkey, v => s.Inventory.SmeltHotkey = v);
            foreach (var p in s.GearSwap.Profiles)
            {
                var profile = p;
                Add("gs:" + profile.Id, "bind.group.gearswap", "hotkey", profile.Hotkey,
                    v => profile.Hotkey = v, profile.Name);
            }
            for (var i = 0; i < s.AutoCast.Rows.Count; i++)
            {
                var index = i;
                var row = s.AutoCast.Rows[index];
                Add("ac:" + index, "bind.group.autocast", "hotkey", row.ToggleHotkey,
                    v => row.ToggleHotkey = v, $"AutoCast {index + 1}");
            }
        }

        MarkConflicts(list);
        return list;
    }

    private static void MarkConflicts(List<BindingEntry> list)
    {
        var byKey = new Dictionary<string, List<BindingEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in list)
        {
            if (string.IsNullOrWhiteSpace(e.KeyName) || e.KeyName is "None" or "Нет") continue;
            if (!byKey.TryGetValue(e.KeyName, out var bucket))
            {
                bucket = [];
                byKey[e.KeyName] = bucket;
            }
            bucket.Add(e);
        }
        foreach (var e in list)
            e.IsConflict = !string.IsNullOrWhiteSpace(e.KeyName)
                           && byKey.TryGetValue(e.KeyName!, out var b)
                           && b.Count > 1;
    }
}
