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

    private readonly SplitContainer _shell = new();
    private readonly Panel _navRail = new();
    private readonly Panel _contentHost = new();
    private readonly Dictionary<string, Panel> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly System.Windows.Forms.Timer _persistTimer = new() { Interval = 280 };

    private string _activePage = "inventory";
    private string _inventorySub = "sell";
    private GridOverlayForm? _gridOverlay;
    private bool _hidden;
    private bool _persistQueued;
    private bool _persistHotkeys;
    private bool _persistGrid;

    private Action? _refreshInventory;
    private Action? _refreshGearSwap;
    private Action? _refreshExtraSkill;
    private Action? _refreshTeleport;
    private Action? _refreshAutoCast;
    private Action? _refreshAllLabels;

    private static readonly (string Id, string Key)[] NavItems =
    [
        ("inventory", "tab.inventory"),
        ("autoclicker", "tab.autoclicker"),
        ("fastmount", "tab.fastmount"),
        ("extraskill", "tab.extraskill"),
        ("gearswap", "tab.gearswap"),
        ("autocast", "tab.autocast"),
        ("teleport", "tab.teleport"),
        ("settings", "tab.settings")
    ];

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
        MinimumSize = new Size(980, 640);
        if (_settings.Current.WindowWidth < 980) _settings.Current.WindowWidth = 1120;
        if (_settings.Current.WindowHeight < 640) _settings.Current.WindowHeight = 760;
        Size = new Size(_settings.Current.WindowWidth, _settings.Current.WindowHeight);
        Location = new Point(_settings.Current.WindowX, _settings.Current.WindowY);
        KeyPreview = true;
        DoubleBuffered = true;

        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            if (_persistQueued)
            {
                _persistQueued = false;
                FlushPersist();
            }
        };

        BuildChrome();
        BuildPages();
        ShowPage("inventory");

        _hotkeys.Attach(this);
        _autoClicker.AttachRawKeys(_hotkeys);
        Load += (_, _) =>
        {
            PlaceShell();
            RegisterAllHotkeys();
            _autoClicker.ApplySettings();
            _autoCast.ApplySettings();
            UpdateGridOverlay();
        };
        Shown += (_, _) => PlaceShell();
        FormClosing += (_, _) =>
        {
            if (_persistQueued)
            {
                _persistTimer.Stop();
                FlushPersist();
            }
            _settings.Current.WindowWidth = Width;
            _settings.Current.WindowHeight = Height;
            _settings.Current.WindowX = Left;
            _settings.Current.WindowY = Top;
            _settings.Save();
            _hotkeys.Dispose();
            _autoClicker.Dispose();
            _autoCast.Dispose();
            _persistTimer.Dispose();
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
        _shell.Dock = DockStyle.Fill;
        _shell.FixedPanel = FixedPanel.Panel1;
        _shell.SplitterWidth = 6;
        _shell.BackColor = Theme.Border;
        // Min sizes / distance set in PlaceShell after real width is known.

        _navRail.Dock = DockStyle.Fill;
        _navRail.BackColor = Theme.Panel;
        _navRail.Padding = new Padding(0);

        var brand = new Label
        {
            Text = "DrakenHub",
            Dock = DockStyle.Top,
            Height = 48,
            Font = Theme.UiFontTitle,
            ForeColor = Theme.Accent,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            BackColor = Theme.Panel
        };

        var navButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Panel,
            Padding = new Padding(0, 4, 0, 8)
        };

        foreach (var (id, key) in NavItems)
        {
            var pageId = id;
            var btn = UiFactory.NavBtn(_loc.T(key));
            btn.Dock = DockStyle.None;
            btn.Width = 188;
            btn.Height = 40;
            btn.Margin = new Padding(0, 0, 0, 2);
            btn.Click += (_, _) => ShowPage(pageId);
            _navButtons[id] = btn;
            navButtons.Controls.Add(btn);
        }

        _navRail.Controls.Add(navButtons);
        _navRail.Controls.Add(brand);

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = Theme.Bg;
        _contentHost.Padding = Padding.Empty;

        _shell.Panel1.Controls.Add(_navRail);
        _shell.Panel2.Controls.Add(_contentHost);
        Controls.Add(_shell);
    }

    private void PlaceShell()
    {
        const int panel1Min = 168;
        const int panel2MinPreferred = 480;
        const int target = 200;

        var room = _shell.Width - _shell.SplitterWidth;
        if (room <= 0) return;

        var p1 = Math.Clamp(panel1Min, 0, Math.Max(0, room - 1));
        var p2 = Math.Clamp(panel2MinPreferred, 0, Math.Max(0, room - p1));
        if (p1 + p2 > room)
            p2 = Math.Max(0, room - p1);
        if (p1 + p2 > room) return;

        var min = p1;
        var max = room - p2;
        if (max < min) return;

        try
        {
            _shell.Panel1MinSize = p1;
            _shell.Panel2MinSize = p2;
            _shell.SplitterDistance = Math.Clamp(target, min, max);
        }
        catch (InvalidOperationException)
        {
            // Container not laid out yet — retry on next Shown/Load.
        }
        catch (ArgumentException)
        {
        }
    }

    private void ShowPage(string id)
    {
        _activePage = id;
        foreach (var pair in _navButtons)
        {
            var active = pair.Key == id;
            pair.Value.BackColor = active ? Theme.NavActive : Theme.NavIdle;
            pair.Value.ForeColor = Theme.Text;
        }

        foreach (var pair in _pages)
            pair.Value.Visible = pair.Key == id;

        if (_pages.TryGetValue(id, out var page) && !_contentHost.Controls.Contains(page))
        {
            page.Dock = DockStyle.Fill;
            page.Visible = true;
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
        _contentHost.Controls.Clear();
        _pages.Clear();

        void Put(string id, Panel page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _pages[id] = page;
            _contentHost.Controls.Add(page);
        }

        Put("inventory", BuildInventoryPage());
        Put("autoclicker", BuildAutoClickerPage());
        Put("fastmount", BuildFastMountPage());
        Put("extraskill", BuildExtraSkillPage());
        Put("gearswap", BuildGearSwapPage());
        Put("autocast", BuildAutoCastPage());
        Put("teleport", BuildTeleportPage());
        Put("settings", BuildSettingsPage());
    }

    private void Persist(bool immediate = false, bool hotkeys = true, bool grid = false)
    {
        SyncSharedBindings();
        if (hotkeys) _persistHotkeys = true;
        if (grid) _persistGrid = true;
        if (immediate)
        {
            _persistTimer.Stop();
            _persistQueued = false;
            FlushPersist();
            return;
        }

        _persistQueued = true;
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void FlushPersist()
    {
        _settings.Save();
        if (_persistHotkeys)
        {
            RegisterAllHotkeys();
            _autoClicker.ApplySettings();
            _autoCast.ApplySettings();
            _persistHotkeys = false;
        }
        if (_persistGrid)
        {
            UpdateGridOverlay();
            _persistGrid = false;
        }
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
            Persist(immediate: true);
            return true;
        }

        if (IsLaunchConflict(pressed, exceptId))
        {
            MessageBox.Show(_loc.T("conflict"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        assign(KeyNames.Format(pressed));
        Persist(immediate: true);
        return true;
    }

    private bool IsLaunchConflict(Keys key, string? exceptId)
    {
        if (key == Keys.None) return false;
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
                Persist(immediate: true);
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
            setter(overlay.Result == null ? null : new ScreenPoint(overlay.Result.Value.X, overlay.Result.Value.Y));
            Persist(immediate: true);
            Refresh();
        };
        _refreshAllLabels += Refresh;
    }

    private void BindCoordButton(Button button, Func<ScreenPoint?> getter, Action<ScreenPoint?> setter, string labelKey, Func<int> sizeProvider)
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
            using var overlay = new OverlayForm(sizeProvider(), rectangle: true);
            Hide();
            overlay.ShowDialog();
            Show();
            setter(overlay.Result == null ? null : new ScreenPoint(overlay.Result.Value.X, overlay.Result.Value.Y));
            Persist(immediate: true);
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
                setter(null);
            else
            {
                var cell = InventoryLayout.PointToCell(_settings.Current.Inventory, overlay.Result.Value)
                           ?? GuessCellFromPoint(overlay.Result.Value);
                setter(cell);
            }
            Persist(immediate: true);
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
        Persist(immediate: true, hotkeys: false, grid: true);
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
            Persist(immediate: true);
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
        foreach (var (id, key) in NavItems)
            if (_navButtons.TryGetValue(id, out var btn))
                btn.Text = _loc.T(key);
    }

    private void RebuildUi(string? stayOn = null)
    {
        var current = stayOn ?? _activePage;
        _refreshAllLabels = null;
        _refreshInventory = null;
        _refreshGearSwap = null;
        _refreshExtraSkill = null;
        _refreshTeleport = null;
        _refreshAutoCast = null;
        BuildPages();
        RelocalizeNav();
        ShowPage(current);
    }
}
