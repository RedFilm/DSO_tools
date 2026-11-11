using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed partial class MainForm : Form
{
    private readonly SettingsService _settings = new();
    private readonly Localization _loc = new();
    private readonly InputService _input = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly MacroRunner _macros;
    private readonly AutoClickerController _autoClicker;
    private readonly AutoCastController _autoCast;

    private readonly TableLayoutPanel _nav = new();
    private readonly Panel _contentHost = new();
    private readonly Dictionary<string, Panel> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private string _activePage = "inventory";
    private string _inventorySub = "sell";
    private GridOverlayForm? _gridOverlay;
    private bool _hidden;

    private Action? _refreshInventory;
    private Action? _refreshGearSwap;
    private Action? _refreshExtraSkill;
    private Action? _refreshTeleport;
    private Action? _refreshAutoCast;
    private Action? _refreshAllLabels;

    public MainForm()
    {
        _macros = new MacroRunner(_settings, _input);
        _autoClicker = new AutoClickerController(_settings, _input);
        _autoCast = new AutoCastController(_settings, _input);

        _settings.Load();
        _loc.Language = _settings.Current.Language;

        Text = "DrakenHub";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.UiFont;
        MinimumSize = new Size(1000, 700);
        if (_settings.Current.WindowWidth < 1000) _settings.Current.WindowWidth = 1100;
        if (_settings.Current.WindowHeight < 700) _settings.Current.WindowHeight = 760;
        Size = new Size(_settings.Current.WindowWidth, _settings.Current.WindowHeight);
        Location = new Point(_settings.Current.WindowX, _settings.Current.WindowY);
        KeyPreview = true;
        DoubleBuffered = true;

        BuildChrome();
        BuildPages();
        ShowPage("inventory");

        _hotkeys.Attach(this);
        _autoClicker.AttachRawKeys(_hotkeys);
        Load += (_, _) =>
        {
            RegisterAllHotkeys();
            _autoClicker.ApplySettings();
            _autoCast.ApplySettings();
            UpdateGridOverlay();
        };
        FormClosing += (_, _) =>
        {
            _settings.Current.WindowWidth = Width;
            _settings.Current.WindowHeight = Height;
            _settings.Current.WindowX = Left;
            _settings.Current.WindowY = Top;
            _settings.Save();
            _hotkeys.Dispose();
            _autoClicker.Dispose();
            _autoCast.Dispose();
            _gridOverlay?.Close();
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (_hotkeys.ProcessMessage(ref m)) return;
        base.WndProc(ref m);
    }

    private void BuildChrome()
    {
        _nav.Dock = DockStyle.Top;
        _nav.Height = 42;
        _nav.BackColor = Theme.Bg;
        _nav.Padding = new Padding(6, 4, 6, 4);
        _nav.Margin = new Padding(0);
        _nav.ColumnCount = 8;
        _nav.RowCount = 1;
        _nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++)
            _nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));

        var tabs = new[]
        {
            ("inventory", "tab.inventory"),
            ("autoclicker", "tab.autoclicker"),
            ("fastmount", "tab.fastmount"),
            ("extraskill", "tab.extraskill"),
            ("gearswap", "tab.gearswap"),
            ("autocast", "tab.autocast"),
            ("teleport", "tab.teleport"),
            ("settings", "tab.settings")
        };
        for (var i = 0; i < tabs.Length; i++)
        {
            var (id, key) = tabs[i];
            var pageId = id;
            var btn = UiFactory.Btn(_loc.T(key), Theme.TabIdle, 30);
            btn.MaximumSize = new Size(4096, 30);
            btn.MinimumSize = new Size(40, 30);
            btn.Dock = DockStyle.Fill;
            btn.Margin = new Padding(2, 0, 2, 0);
            btn.Padding = new Padding(2, 0, 2, 0);
            btn.ForeColor = Theme.Text;
            btn.Font = Theme.UiFontSmall;
            btn.Click += (_, _) => ShowPage(pageId);
            _navButtons[id] = btn;
            _nav.Controls.Add(btn, i, 0);
        }

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = Theme.Bg;
        _contentHost.Padding = new Padding(0);

        // Dock order: Fill first, then Top (z-order).
        Controls.Add(_contentHost);
        Controls.Add(_nav);
    }

    private void ShowPage(string id)
    {
        _activePage = id;
        foreach (var pair in _navButtons)
        {
            pair.Value.BackColor = pair.Key == id ? Theme.TabActive : Theme.TabIdle;
            pair.Value.ForeColor = Theme.Text;
        }

        _contentHost.Controls.Clear();
        if (_pages.TryGetValue(id, out var page))
        {
            page.Dock = DockStyle.Fill;
            _contentHost.Controls.Add(page);
        }
    }

    private void ToggleVisible()
    {
        _hidden = !_hidden;
        Visible = !_hidden;
        if (!_hidden)
        {
            Show();
            Activate();
        }
    }

    private void BuildPages()
    {
        _pages["inventory"] = BuildInventoryPage();
        _pages["autoclicker"] = BuildAutoClickerPage();
        _pages["fastmount"] = BuildFastMountPage();
        _pages["extraskill"] = BuildExtraSkillPage();
        _pages["gearswap"] = BuildGearSwapPage();
        _pages["autocast"] = BuildAutoCastPage();
        _pages["teleport"] = BuildTeleportPage();
        _pages["settings"] = BuildSettingsPage();
    }

    private void Persist()
    {
        SyncSharedBindings();
        _settings.Save();
        RegisterAllHotkeys();
        _autoClicker.ApplySettings();
        _autoCast.ApplySettings();
        UpdateGridOverlay();
    }

    private void SyncSharedBindings()
    {
        var s = _settings.Current;
        var inventoryKey = s.Inventory.InventoryHotkey ?? s.GearSwap.InventoryHotkey;
        s.Inventory.InventoryHotkey = inventoryKey;
        s.GearSwap.InventoryHotkey = inventoryKey;

        var main = s.ExtraSkill.MainPanelHotkey
                   ?? s.FastMount.MainPanelHotkey
                   ?? s.GearSwap.MainPanelHotkey;
        SetMainPanelHotkey(main);
    }

    private void SetMainPanelHotkey(string? value)
    {
        var s = _settings.Current;
        s.ExtraSkill.MainPanelHotkey = value;
        s.FastMount.MainPanelHotkey = value;
        s.GearSwap.MainPanelHotkey = value;
    }

    private bool TryAssignLaunchHotkey(Keys pressed, Action<string?> assign, string? exceptId = null)
    {
        if (pressed == Keys.Escape)
        {
            assign(null);
            Persist();
            return true;
        }

        if (IsLaunchConflict(pressed, exceptId))
        {
            MessageBox.Show(_loc.T("conflict"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        assign(KeyNames.Format(pressed));
        Persist();
        return true;
    }

    private bool IsLaunchConflict(Keys key, string? exceptId)
    {
        if (key == Keys.None) return false;
        var name = KeyNames.Format(key);
        var s = _settings.Current;
        bool Same(string? stored, string id) =>
            id != exceptId && KeyNames.Parse(stored) == key;

        if (Same(s.Inventory.SellAllHotkey, "sell.all")) return true;
        if (Same(s.Inventory.SellCurrentHotkey, "sell.current")) return true;
        if (Same(s.Inventory.SmeltHotkey, "smelt")) return true;
        if (Same(s.FastMount.FastMountHotkey, "fastmount")) return true;
        if (Same(s.FastMount.JumpMountHotkey, "jumpmount")) return true;
        foreach (var p in s.ExtraSkill.Profiles)
            if (Same(p.Hotkey, "es:" + p.Id)) return true;
        foreach (var p in s.GearSwap.Profiles)
            if (Same(p.Hotkey, "gs:" + p.Id)) return true;
        foreach (var p in s.FastTeleport.Locations)
            if (Same(p.Hotkey, "tp:" + p.Id)) return true;
        for (var i = 0; i < s.AutoCast.Rows.Count; i++)
            if (Same(s.AutoCast.Rows[i].ToggleHotkey, "ac:" + i)) return true;
        if (Same(s.WindowHotkeys.HideShow, "win.hide")) return true;
        if (Same(s.WindowHotkeys.Exit, "win.exit")) return true;
        if (Same(s.WindowHotkeys.EmergencyStop, "win.stop")) return true;
        // Auto-clicker hotkeys also launch behavior
        if (Same(s.AutoClicker.LeftHotkey, "aclick.left")) return true;
        if (Same(s.AutoClicker.RightHotkey, "aclick.right")) return true;
        return false;
    }

    private void BindLaunchButton(Button button, Func<string?> getter, Action<string?> setter, string id, string labelKey)
    {
        void Refresh() => button.Text = $"{_loc.T(labelKey)}: {KeyNames.Display(getter())}";
        Refresh();
        button.Click += (_, _) =>
        {
            button.Text = _loc.T("pressAny");
            _hotkeys.BeginCapture(key =>
            {
                TryAssignLaunchHotkey(key, setter, id);
                Refresh();
            });
        };
        _refreshAllLabels += Refresh;
    }

    private void BindUtilityKeyButton(Button button, Func<string?> getter, Action<string?> setter, string labelKey)
    {
        void Refresh() => button.Text = $"{_loc.T(labelKey)}: {KeyNames.Display(getter())}";
        Refresh();
        button.Click += (_, _) =>
        {
            button.Text = _loc.T("pressAny");
            _hotkeys.BeginCapture(key =>
            {
                if (key == Keys.Escape) setter(null);
                else setter(KeyNames.Format(key));
                Persist();
                Refresh();
            });
        };
        _refreshAllLabels += Refresh;
    }

    private void BindCoordButton(Button button, Func<ScreenPoint?> getter, Action<ScreenPoint?> setter, string labelKey, int? size = null)
    {
        void Refresh()
        {
            var p = getter();
            button.Text = p == null
                ? $"{_loc.T(labelKey)}: {_loc.T("none")}"
                : $"{_loc.T(labelKey)}: {p}";
        }
        Refresh();
        button.Click += (_, _) =>
        {
            var pickSize = size ?? Math.Max(16, _settings.Current.Inventory.Grid.CrosshairSize);
            using var overlay = new OverlayForm(pickSize, rectangle: true);
            Hide();
            overlay.ShowDialog();
            Show();
            if (overlay.Result == null)
            {
                // Escape clears
                setter(null);
            }
            else
            {
                setter(new ScreenPoint(overlay.Result.Value.X, overlay.Result.Value.Y));
            }
            Persist();
            Refresh();
        };
        _refreshAllLabels += Refresh;
    }

    private void BindCellButton(Button button, Func<CellRef?> getter, Action<CellRef?> setter, string labelKey)
    {
        void Refresh()
        {
            var c = getter();
            button.Text = c == null
                ? $"{_loc.T(labelKey)}: {_loc.T("none")}"
                : $"{_loc.T(labelKey)}: {c}";
        }
        Refresh();
        button.Click += (_, _) =>
        {
            var size = _settings.Current.Inventory.Grid.CellSize;
            using var overlay = new OverlayForm(size, rectangle: true);
            Hide();
            overlay.ShowDialog();
            Show();
            if (overlay.Result == null)
            {
                setter(null);
            }
            else
            {
                var cell = InventoryLayout.PointToCell(_settings.Current.Inventory, overlay.Result.Value)
                           ?? GuessCellFromPoint(overlay.Result.Value);
                setter(cell);
            }
            Persist();
            Refresh();
        };
        _refreshAllLabels += Refresh;
    }

    private CellRef GuessCellFromPoint(Point screen)
    {
        var inv = _settings.Current.Inventory;
        if (inv.FirstCell == null)
            return new CellRef { Row = 1, Col = 1 };
        var g = inv.Grid;
        var col = Math.Max(0, (screen.X - inv.FirstCell.X) / Math.Max(1, g.CellSize + g.GapX));
        var row = Math.Max(0, (screen.Y - inv.FirstCell.Y) / Math.Max(1, g.CellSize + g.GapY));
        return new CellRef
        {
            Row = Math.Clamp(row + 1, 1, g.Rows),
            Col = Math.Clamp(col + 1, 1, g.Columns)
        };
    }

    private void AddColorPoint(List<RelativeColorPoint> points)
    {
        if (_settings.Current.Inventory.FirstCell == null)
        {
            MessageBox.Show("Сначала задайте «Первая ячейка».", _loc.T("notify"));
            return;
        }

        var size = _settings.Current.Inventory.Grid.CrosshairSize;
        using var overlay = new OverlayForm(size, rectangle: false);
        Hide();
        overlay.ShowDialog();
        Show();
        if (overlay.Result == null) return;

        var inv = _settings.Current.Inventory;
        var cell = InventoryLayout.PointToCell(inv, overlay.Result.Value) ?? GuessCellFromPoint(overlay.Result.Value);
        var origin = InventoryLayout.CellOrigin(inv, cell.Row - 1, cell.Col - 1) ?? inv.FirstCell.AsPoint;
        var color = _input.GetPixel(overlay.Result.Value.X, overlay.Result.Value.Y);
        points.Add(new RelativeColorPoint
        {
            OffsetX = overlay.Result.Value.X - origin.X,
            OffsetY = overlay.Result.Value.Y - origin.Y,
            ColorArgb = color.ToArgb()
        });
        Persist();
    }

    private void UpdateGridOverlay()
    {
        var show = _settings.Current.Inventory.Grid.ShowGrid;
        if (!show)
        {
            if (_gridOverlay != null)
            {
                _gridOverlay.Close();
                _gridOverlay = null;
            }
            return;
        }

        var (cells, tabs, samples) = InventoryLayout.BuildOverlay(_settings.Current.Inventory);
        _gridOverlay?.Close();
        _gridOverlay = new GridOverlayForm(cells, tabs, samples);
        _gridOverlay.Show();
    }

    private void RegisterAllHotkeys()
    {
        _hotkeys.UnregisterAll();
        var s = _settings.Current;

        void Reg(string id, string? keyName, Action action)
        {
            var key = KeyNames.Parse(keyName);
            if (key == Keys.None) return;
            _hotkeys.Register(id, key, action);
        }

        Reg("win.hide", s.WindowHotkeys.HideShow, ToggleVisible);
        Reg("win.exit", s.WindowHotkeys.Exit, () => BeginInvoke(Close));
        Reg("win.stop", s.WindowHotkeys.EmergencyStop, () =>
        {
            _macros.EmergencyStop();
            _autoClicker.StopAll();
            _autoCast.StopAll();
            Persist();
            _refreshAutoCast?.Invoke();
        });

        Reg("sell.all", s.Inventory.SellAllHotkey, () => _macros.SellAll());
        Reg("sell.current", s.Inventory.SellCurrentHotkey, () => _macros.SellCurrent());
        Reg("smelt", s.Inventory.SmeltHotkey, () => _macros.Smelt());
        Reg("fastmount", s.FastMount.FastMountHotkey, () => _macros.FastMount());
        Reg("jumpmount", s.FastMount.JumpMountHotkey, () => _macros.JumpMount());

        foreach (var p in s.ExtraSkill.Profiles)
        {
            var profile = p;
            Reg("es:" + profile.Id, profile.Hotkey, () => _macros.ExtraSkill(profile));
        }
        foreach (var p in s.GearSwap.Profiles)
        {
            var profile = p;
            Reg("gs:" + profile.Id, profile.Hotkey, () => _macros.GearSwap(profile));
        }
        foreach (var p in s.FastTeleport.Locations)
        {
            var loc = p;
            Reg("tp:" + loc.Id, loc.Hotkey, () => _macros.Teleport(loc));
        }
        for (var i = 0; i < s.AutoCast.Rows.Count; i++)
        {
            var index = i;
            Reg("ac:" + index, s.AutoCast.Rows[index].ToggleHotkey, () =>
            {
                _autoCast.Toggle(index);
                _refreshAutoCast?.Invoke();
            });
        }
    }

    private void RelocalizeNav()
    {
        var map = new Dictionary<string, string>
        {
            ["inventory"] = "tab.inventory",
            ["autoclicker"] = "tab.autoclicker",
            ["fastmount"] = "tab.fastmount",
            ["extraskill"] = "tab.extraskill",
            ["gearswap"] = "tab.gearswap",
            ["autocast"] = "tab.autocast",
            ["teleport"] = "tab.teleport",
            ["settings"] = "tab.settings"
        };
        foreach (var pair in map)
            if (_navButtons.TryGetValue(pair.Key, out var btn))
                btn.Text = _loc.T(pair.Value);
    }
}
