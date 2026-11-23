using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed partial class MainForm : Form
{
    // Flip these when features leave WIP.
    private static readonly HashSet<string> MutedNavIds = ["gearswap"];
    private static readonly bool SmeltMuted = true;

    private readonly SettingsService _settings = new();
    private readonly Localization _loc = new();
    private readonly InputService _input = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly MacroRunner _macros;
    private readonly AutoClickerController _autoClicker;
    private readonly AutoCastController _autoCast;
    private readonly SpeedBoostService _speedBoost;

    private readonly SplitContainer _shell = new();
    private readonly Panel _navRail = new();
    private readonly Panel _contentHost = new();
    private readonly Panel _statusBar = new();
    private readonly Label _statusLeft = new();
    private readonly Label _statusRight = new();
    private readonly Dictionary<string, Panel> _pages = new();
    private readonly Dictionary<string, NavItemButton> _navButtons = new();
    private readonly System.Windows.Forms.Timer _persistTimer = new() { Interval = 280 };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 400 };
    private readonly ToolTip _toolTips = new();
    private NotifyIcon? _tray;

    private string _activePage = "play";
    private string _inventorySub = "sell";
    private GridOverlayForm? _gridOverlay;
    private bool _hidden;
    private bool _persistQueued;
    private bool _persistHotkeys;
    private bool _persistGrid;
    private string _statusMessage = "";
    private DateTime _statusMessageUntil;

    private Action? _refreshInventory;
    private Action? _refreshGearSwap;
    private Action? _refreshExtraSkill;
    private Action? _refreshTeleport;
    private Action? _refreshAutoCast;
    private Action? _refreshSpeedBoost;
    private Action? _refreshAllLabels;
    private Action? _refreshChecklist;
    private Action? _refreshBindingMap;
    private string _speedBoostStatusKey = "disabled";
    /// <summary>Suppress combo SelectedIndexChanged side-effects while relocalizing / refreshing labels.</summary>
    private bool _suppressUiEvents;

    private static readonly (string Id, string Key)[] NavItems =
    [
        ("play", "nav.binding"),
        ("inventory", "tab.inventory"),
        ("autoclicker", "tab.autoclicker"),
        ("fastmount", "tab.fastmount"),
        ("extraskill", "tab.extraskill"),
        ("teleport", "tab.teleport"),
        ("speedboost", "tab.speedboost"),
        ("settings", "tab.settings"),
        ("gearswap", "tab.gearswap"),
        ("autocast", "tab.autocast")
    ];

    public MainForm()
    {
        _macros = new MacroRunner(_settings, _input);
        _autoClicker = new AutoClickerController(_settings, _input);
        _autoCast = new AutoCastController(_settings, _input);
        _speedBoost = new SpeedBoostService(_settings);
        _speedBoost.StatusChanged += key =>
        {
            _speedBoostStatusKey = key;
            if (IsHandleCreated)
                BeginInvoke(() =>
                {
                    _refreshSpeedBoost?.Invoke();
                    RefreshStatusBar();
                });
        };
        _speedBoost.StateChanged += () =>
        {
            if (IsHandleCreated)
                BeginInvoke(() =>
                {
                    _refreshSpeedBoost?.Invoke();
                    RefreshStatusBar();
                });
        };
        _autoClicker.ActivityChanged += () =>
        {
            if (IsHandleCreated)
                BeginInvoke(RefreshStatusBar);
        };

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
        ApplySavedBounds();
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
        _statusTimer.Tick += (_, _) => RefreshStatusBar();

        BuildChrome();
        BuildTray();
        BuildPages();
        ShowPage("play");

        _hotkeys.Attach(this);
        _autoClicker.AttachRawKeys(_hotkeys);
        Load += (_, _) =>
        {
            PlaceShell();
            RegisterAllHotkeys();
            _autoClicker.ApplySettings();
            _autoCast.ApplySettings();
            UpdateGridOverlay();
            _statusTimer.Start();
            RefreshStatusBar();
            MaybeShowFirstRunWizard();
        };
        Shown += (_, _) => PlaceShell();
        FormClosing += (_, _) =>
        {
            if (_persistQueued)
            {
                _persistTimer.Stop();
                FlushPersist();
            }
            SaveWindowBounds();
            _settings.Save();
            _hotkeys.Dispose();
            _autoClicker.Dispose();
            _autoCast.Dispose();
            _speedBoost.Dispose();
            _persistTimer.Dispose();
            _statusTimer.Dispose();
            _toolTips.Dispose();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            _gridOverlay?.Close();
        };
    }

    private void ApplySavedBounds()
    {
        var s = _settings.Current;
        var w = Math.Max(MinimumSize.Width, s.WindowWidth < 980 ? 1120 : s.WindowWidth);
        var h = Math.Max(MinimumSize.Height, s.WindowHeight < 640 ? 760 : s.WindowHeight);
        var x = s.WindowX;
        var y = s.WindowY;

        if (x <= -10000 || y <= -10000 || w < MinimumSize.Width || h < MinimumSize.Height)
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Math.Max(1120, MinimumSize.Width), Math.Max(760, MinimumSize.Height));
            return;
        }

        var bounds = new Rectangle(x, y, w, h);
        if (!IsReasonablyOnScreen(bounds))
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(w, h);
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Size = new Size(w, h);
        Location = new Point(x, y);
    }

    private void SaveWindowBounds()
    {
        if (WindowState == FormWindowState.Minimized || Left <= -10000 || Top <= -10000)
            return;

        var bounds = new Rectangle(Left, Top, Width, Height);
        if (!IsReasonablyOnScreen(bounds))
            return;

        _settings.Current.WindowWidth = Width;
        _settings.Current.WindowHeight = Height;
        _settings.Current.WindowX = Left;
        _settings.Current.WindowY = Top;
    }

    private static bool IsReasonablyOnScreen(Rectangle bounds)
    {
        const int minVisible = 80;
        foreach (var screen in Screen.AllScreens)
        {
            var overlap = Rectangle.Intersect(screen.WorkingArea, bounds);
            if (overlap.Width >= minVisible && overlap.Height >= minVisible)
                return true;
        }
        return false;
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
            Padding = new Padding(0, 2, 0, 12)
        };

        void SyncNavWidths()
        {
            var w = Math.Max(120, navButtons.ClientSize.Width);
            foreach (Control c in navButtons.Controls)
                c.Width = w;
        }

        void AddGroup(string key, bool first = false)
        {
            var header = UiFactory.NavGroupLabel(_loc.T(key));
            header.Tag = key;
            if (first)
                header.Margin = new Padding(0, 4, 0, 4);
            navButtons.Controls.Add(header);
        }

        AddGroup("nav.tools", first: true);
        foreach (var id in new[] { "inventory", "autoclicker", "fastmount", "extraskill", "autocast", "teleport", "speedboost" })
        {
            var item = NavItems.First(n => n.Id == id);
            AddNavButton(navButtons, item.Id, item.Key);
        }

        AddGroup("nav.play");
        AddNavButton(navButtons, "play", "nav.binding");
        AddNavButton(navButtons, "settings", "tab.settings");

        navButtons.Resize += (_, _) => SyncNavWidths();
        navButtons.HandleCreated += (_, _) => SyncNavWidths();
        SyncNavWidths();

        _navRail.Controls.Add(navButtons);
        _navRail.Controls.Add(brand);

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = Theme.Bg;
        _contentHost.Padding = Padding.Empty;

        _statusBar.Dock = DockStyle.Bottom;
        _statusBar.Height = 28;
        _statusBar.BackColor = Theme.Panel;
        _statusBar.Padding = new Padding(10, 0, 10, 0);
        _statusLeft.Dock = DockStyle.Fill;
        _statusLeft.ForeColor = Theme.TextMuted;
        _statusLeft.Font = Theme.UiFontSmall;
        _statusLeft.TextAlign = ContentAlignment.MiddleLeft;
        _statusRight.Dock = DockStyle.Right;
        _statusRight.AutoSize = false;
        _statusRight.Width = 220;
        _statusRight.ForeColor = Theme.TextMuted;
        _statusRight.Font = Theme.UiFontSmall;
        _statusRight.TextAlign = ContentAlignment.MiddleRight;
        _statusBar.Controls.Add(_statusLeft);
        _statusBar.Controls.Add(_statusRight);
        _statusBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, _statusBar.Width, 0);
        };

        _shell.Panel1.Controls.Add(_navRail);
        _shell.Panel2.Controls.Add(_contentHost);
        Controls.Add(_shell);
        Controls.Add(_statusBar);
    }

    private void AddNavButton(FlowLayoutPanel host, string id, string key)
    {
        var pageId = id;
        var muted = MutedNavIds.Contains(id);
        var btn = UiFactory.NavBtn(_loc.T(key));
        btn.Width = Math.Max(120, host.ClientSize.Width);
        btn.IsMuted = muted;
        if (muted)
            _toolTips.SetToolTip(btn, _loc.T("wip.tooltip"));
        else
            btn.Click += (_, _) => ShowPage(pageId);

        _navButtons[id] = btn;
        host.Controls.Add(btn);
    }

    private void BuildTray()
    {
        _tray = new NotifyIcon
        {
            Text = "DrakenHub",
            Icon = SystemIcons.Application,
            Visible = true
        };
        var menu = new ContextMenuStrip();
        var showItem = new ToolStripMenuItem(_loc.T("tray.show"));
        showItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new ToolStripMenuItem(_loc.T("tray.exit"));
        exitItem.Click += (_, _) => Close();
        menu.Items.Add(showItem);
        menu.Items.Add(exitItem);
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        _tray.Tag = menu;
    }

    private void RelocalizeTray()
    {
        if (_tray?.ContextMenuStrip is not { } menu || menu.Items.Count < 2) return;
        menu.Items[0].Text = _loc.T("tray.show");
        menu.Items[1].Text = _loc.T("tray.exit");
    }

    private void RestoreFromTray()
    {
        _hidden = false;
        Visible = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
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
        catch (InvalidOperationException) { }
        catch (ArgumentException) { }
    }

    private void ShowPage(string id)
    {
        if (MutedNavIds.Contains(id))
            id = "play";

        _activePage = id;
        foreach (var pair in _navButtons)
        {
            pair.Value.IsMuted = MutedNavIds.Contains(pair.Key);
            pair.Value.IsActive = pair.Key == id;
        }

        foreach (var pair in _pages)
            pair.Value.Visible = pair.Key == id;
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
        else if (_tray != null)
        {
            _tray.Visible = true;
            _tray.ShowBalloonTip(1500, "DrakenHub", _loc.T("tray.show"), ToolTipIcon.Info);
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

        Put("play", BuildPlayPage());
        Put("inventory", BuildInventoryPage());
        Put("autoclicker", BuildAutoClickerPage());
        Put("fastmount", BuildFastMountPage());
        Put("extraskill", BuildExtraSkillPage());
        if (!MutedNavIds.Contains("gearswap"))
            Put("gearswap", BuildGearSwapPage());
        if (!MutedNavIds.Contains("autocast"))
            Put("autocast", BuildAutoCastPage());
        Put("teleport", BuildTeleportPage());
        Put("speedboost", BuildSpeedBoostPage());
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
            _refreshBindingMap?.Invoke();
        }
        if (_persistGrid)
        {
            UpdateGridOverlay();
            _persistGrid = false;
        }
        _refreshChecklist?.Invoke();
        RefreshStatusBar();
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

    private string FormatHotkeyLabel(string labelKey, string? stored)
    {
        var key = KeyNames.Parse(stored);
        var display = key == Keys.None ? _loc.T("none") : KeyNames.Display(stored);
        return $"{_loc.T(labelKey)} — [{display}]";
    }

    private string FormatCoordLabel(string labelKey, ScreenPoint? p)
    {
        return p == null
            ? $"{_loc.T(labelKey)} — {_loc.T("none")}"
            : $"{_loc.T(labelKey)} — ({p.X}, {p.Y})";
    }

    private bool TryAssignHotkey(Keys pressed, Action<string?> assign, string? exceptId = null)
    {
        if (pressed == Keys.Escape)
        {
            assign(null);
            Persist(immediate: true);
            return true;
        }

        if (IsHotkeyConflict(pressed, exceptId))
        {
            MessageBox.Show(_loc.T("conflict"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        assign(KeyNames.Format(pressed));
        Persist(immediate: true);
        return true;
    }

    private bool IsHotkeyConflict(Keys key, string? exceptId)
    {
        if (key == Keys.None) return false;
        var s = _settings.Current;
        bool Same(string? stored, string id) =>
            id != exceptId && KeyNames.Parse(stored) == key;

        if (Same(s.Inventory.SellAllHotkey, "sell.all")) return true;
        if (!SmeltMuted && Same(s.Inventory.SmeltHotkey, "smelt")) return true;
        if (Same(s.Inventory.InventoryHotkey, "util.inv")) return true;
        if (Same(s.Inventory.SkillsWindowHotkey, "util.skills")) return true;
        if (Same(s.FastMount.MountHotkey, "util.mount")) return true;
        if (Same(s.FastMount.FastMountHotkey, "fastmount")) return true;
        if (Same(s.FastMount.JumpMountHotkey, "jumpmount")) return true;
        if (Same(s.FastMount.MainPanelHotkey, "util.main")) return true;
        if (Same(s.FastMount.JumpTargetPanelHotkey, "util.jumpTarget")) return true;
        if (Same(s.FastMount.JumpSkillHotkey, "util.jumpSkill")) return true;
        foreach (var p in s.ExtraSkill.Profiles)
        {
            if (Same(p.Hotkey, "es:" + p.Id)) return true;
            if (Same(p.TargetPanelHotkey, "es.t:" + p.Id)) return true;
            if (Same(p.SkillSlotHotkey, "es.s:" + p.Id)) return true;
        }
        if (!MutedNavIds.Contains("gearswap"))
        {
            foreach (var p in s.GearSwap.Profiles)
            {
                if (Same(p.Hotkey, "gs:" + p.Id)) return true;
                if (Same(p.TargetPanelHotkey, "gs.t:" + p.Id)) return true;
                if (Same(p.SkillSlotHotkey, "gs.s:" + p.Id)) return true;
            }
        }
        foreach (var p in s.FastTeleport.Locations)
            if (Same(p.Hotkey, "tp:" + p.Id)) return true;
        if (!MutedNavIds.Contains("autocast"))
        {
            for (var i = 0; i < s.AutoCast.Rows.Count; i++)
            {
                if (Same(s.AutoCast.Rows[i].ToggleHotkey, "ac:" + i)) return true;
                if (Same(s.AutoCast.Rows[i].SkillHotkey, "ac.skill:" + i)) return true;
            }
        }
        if (Same(s.WindowHotkeys.HideShow, "win.hide")) return true;
        if (Same(s.WindowHotkeys.Exit, "win.exit")) return true;
        if (Same(s.WindowHotkeys.EmergencyStop, "win.stop")) return true;
        if (Same(s.AutoClicker.LeftHotkey, "aclick.left")) return true;
        if (Same(s.AutoClicker.RightHotkey, "aclick.right")) return true;
        if (Same(s.SpeedBoost.ToggleHotkey, "speedboost.toggle")) return true;
        return false;
    }

    private void BindLaunchButton(Button button, Func<string?> getter, Action<string?> setter, string id, string labelKey)
    {
        void Refresh() => button.Text = FormatHotkeyLabel(labelKey, getter());
        Refresh();
        _toolTips.SetToolTip(button, _loc.T("tt.hotkey"));
        button.Click += (_, _) =>
        {
            button.Text = _loc.T("pressAny");
            _hotkeys.BeginCapture(key =>
            {
                TryAssignHotkey(key, setter, id);
                Refresh();
            });
        };
        _refreshAllLabels += () =>
        {
            Refresh();
            _toolTips.SetToolTip(button, _loc.T("tt.hotkey"));
        };
    }

    private void BindHotkeyRow(SettingRow row, Func<string?> getter, Action<string?> setter, string id, string labelKey)
    {
        void Refresh()
        {
            row.LabelText = _loc.T(labelKey);
            var k = KeyNames.Parse(getter());
            row.ValueDanger = false;
            row.ValueText = k == Keys.None ? _loc.T("not.set") : KeyNames.Display(getter());
        }
        Refresh();
        row.Activated += (_, _) =>
        {
            row.ValueText = _loc.T("pressAny");
            row.ValueDanger = false;
            _hotkeys.BeginCapture(key =>
            {
                TryAssignHotkey(key, setter, id);
                Refresh();
                _refreshBindingMap?.Invoke();
            });
        };
        _refreshAllLabels += Refresh;
    }

    private void BindCoordRow(SettingRow row, Func<ScreenPoint?> getter, Action<ScreenPoint?> setter, string labelKey, bool useOrigin = false, Func<int>? sizeProvider = null)
    {
        void Refresh()
        {
            row.LabelText = _loc.T(labelKey);
            var p = getter();
            row.ValueDanger = false;
            row.ValueText = p == null ? _loc.T("not.set") : $"({p.X}, {p.Y})";
        }
        Refresh();
        row.Activated += (_, _) => PickCoord(
            setter,
            Refresh,
            sizeProvider?.Invoke() ?? Math.Max(16, _settings.Current.Inventory.Grid.CrosshairSize),
            useOrigin);
        _refreshAllLabels += Refresh;
    }

    private void BindCoordRow(SettingRow row, Func<ScreenPoint?> getter, Action<ScreenPoint?> setter, string labelKey, Func<int> sizeProvider, bool useOrigin = true)
        => BindCoordRow(row, getter, setter, labelKey, useOrigin, sizeProvider);

    private void BindUtilityKeyButton(Button button, Func<string?> getter, Action<string?> setter, string labelKey, string? conflictId = null)
    {
        var id = conflictId ?? "util:" + labelKey;
        void Refresh() => button.Text = FormatHotkeyLabel(labelKey, getter());
        Refresh();
        _toolTips.SetToolTip(button, _loc.T("tt.hotkey"));
        button.Click += (_, _) =>
        {
            button.Text = _loc.T("pressAny");
            _hotkeys.BeginCapture(key =>
            {
                TryAssignHotkey(key, setter, id);
                Refresh();
            });
        };
        _refreshAllLabels += () =>
        {
            Refresh();
            _toolTips.SetToolTip(button, _loc.T("tt.hotkey"));
        };
    }

    private void BindCoordButton(Button button, Func<ScreenPoint?> getter, Action<ScreenPoint?> setter, string labelKey, int? size = null)
    {
        void Refresh() => button.Text = FormatCoordLabel(labelKey, getter());
        Refresh();
        _toolTips.SetToolTip(button, _loc.T("tt.coord"));
        button.Click += (_, _) => PickCoord(setter, Refresh, size ?? Math.Max(16, _settings.Current.Inventory.Grid.CrosshairSize), useOrigin: false);
        _refreshAllLabels += () =>
        {
            Refresh();
            _toolTips.SetToolTip(button, _loc.T("tt.coord"));
        };
    }

    private void BindCoordButton(Button button, Func<ScreenPoint?> getter, Action<ScreenPoint?> setter, string labelKey, Func<int> sizeProvider)
    {
        void Refresh() => button.Text = FormatCoordLabel(labelKey, getter());
        Refresh();
        _toolTips.SetToolTip(button, _loc.T("tt.coord"));
        button.Click += (_, _) => PickCoord(setter, Refresh, sizeProvider(), useOrigin: true);
        _refreshAllLabels += () =>
        {
            Refresh();
            _toolTips.SetToolTip(button, _loc.T("tt.coord"));
        };
    }

    private void PickCoord(Action<ScreenPoint?> setter, Action refresh, int pickSize, bool useOrigin)
    {
        var gridWasVisible = _gridOverlay is { Visible: true };
        if (_gridOverlay != null) _gridOverlay.Visible = false;

        using var overlay = new OverlayForm(pickSize, rectangle: true, hintText: _loc.T("overlay.hint"));
        Hide();
        overlay.ShowDialog();
        Show();

        if (overlay.Result == null)
            setter(null);
        else
        {
            var p = useOrigin && overlay.Origin != null ? overlay.Origin.Value : overlay.Result.Value;
            setter(new ScreenPoint(p.X, p.Y));
        }

        Persist(immediate: true, hotkeys: false, grid: true);
        UpdateGridOverlay();
        if (gridWasVisible && _gridOverlay != null)
            _gridOverlay.Visible = true;
        refresh();
        _refreshChecklist?.Invoke();
    }

    private void BindCellButton(Button button, Func<CellRef?> getter, Action<CellRef?> setter, string labelKey)
    {
        void Refresh()
        {
            var c = getter();
            button.Text = c == null
                ? $"{_loc.T(labelKey)} — {_loc.T("none")}"
                : $"{_loc.T(labelKey)} — {c}";
        }
        Refresh();
        _toolTips.SetToolTip(button, _loc.T("tt.coord"));
        button.Click += (_, _) =>
        {
            var size = _settings.Current.Inventory.Grid.CellSize;
            using var overlay = new OverlayForm(size, rectangle: true, hintText: _loc.T("overlay.hint"));
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
            Persist(immediate: true, grid: true);
            Refresh();
        };
        _refreshAllLabels += () =>
        {
            Refresh();
            _toolTips.SetToolTip(button, _loc.T("tt.coord"));
        };
    }

    private CellRef GuessCellFromPoint(Point screen)
    {
        var inv = _settings.Current.Inventory;
        if (inv.FirstCell == null)
            return new CellRef { Row = 1, Col = 1 };
        var bestCol = 0;
        var bestRow = 0;
        var best = int.MaxValue;
        for (var row = 0; row < InventoryLayout.Rows; row++)
        for (var col = 0; col < InventoryLayout.Columns; col++)
        {
            var origin = InventoryLayout.CellOrigin(inv, row, col);
            if (origin == null) continue;
            var dx = screen.X - (origin.Value.X + inv.Grid.CellSize / 2);
            var dy = screen.Y - (origin.Value.Y + inv.Grid.CellSize / 2);
            var d = dx * dx + dy * dy;
            if (d >= best) continue;
            best = d;
            bestCol = col;
            bestRow = row;
        }
        return new CellRef
        {
            Row = bestRow + 1,
            Col = bestCol + 1
        };
    }

    private void AddColorPoint(List<RelativeColorPoint> points)
    {
        if (_settings.Current.Inventory.FirstCell == null)
        {
            MessageBox.Show(_loc.T("need.firstCell"), _loc.T("notify"));
            return;
        }

        var size = _settings.Current.Inventory.Grid.CrosshairSize;
        using var overlay = new OverlayForm(size, rectangle: false, hintText: _loc.T("overlay.hint"));
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

    private bool ConfirmAction(string messageKey) =>
        MessageBox.Show(_loc.T(messageKey), _loc.T("notify"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    private void SetStatusMessage(string message, int ms = 3500)
    {
        _statusMessage = message;
        _statusMessageUntil = DateTime.UtcNow.AddMilliseconds(ms);
        RefreshStatusBar();
    }

    private void RefreshStatusBar()
    {
        if (!IsHandleCreated) return;
        var parts = new List<string>();
        if (_macros.IsRunning) parts.Add(_loc.T("status.macro"));
        if (_autoClicker.IsAnyActive) parts.Add(_loc.T("status.clicker"));
        if (_speedBoost.IsEnabled || _speedBoost.IsLaunching) parts.Add(_loc.T("status.speedboost"));

        var left = parts.Count == 0 ? _loc.T("status.idle") : string.Join(" · ", parts);
        if (!string.IsNullOrEmpty(_statusMessage) && DateTime.UtcNow < _statusMessageUntil)
            left = _statusMessage;
        else
            _statusMessage = "";

        _statusLeft.Text = left;
        var stopKey = KeyNames.Display(_settings.Current.WindowHotkeys.EmergencyStop);
        if (KeyNames.Parse(_settings.Current.WindowHotkeys.EmergencyStop) == Keys.None)
            stopKey = _loc.T("none");
        _statusRight.Text = _loc.Tf("status.stopHint", stopKey);
    }

    private void UpdateGridOverlay()
    {
        var show = _settings.Current.Inventory.Grid.ShowGrid;
        if (!show)
        {
            if (_gridOverlay != null)
            {
                _gridOverlay.Close();
                _gridOverlay.Dispose();
                _gridOverlay = null;
            }
            return;
        }

        var (cells, tabs, samples) = InventoryLayout.BuildOverlay(_settings.Current.Inventory);
        if (_gridOverlay == null || _gridOverlay.IsDisposed)
        {
            _gridOverlay = new GridOverlayForm(cells, tabs, samples);
            _gridOverlay.Show();
        }
        else
        {
            _gridOverlay.UpdateGeometry(cells, tabs, samples);
        }
    }

    private void RegisterAllHotkeys()
    {
        _hotkeys.UnregisterAll();
        var s = _settings.Current;
        var failed = new List<string>();

        void Reg(string id, string? keyName, Action action)
        {
            var key = KeyNames.Parse(keyName);
            if (key == Keys.None) return;
            if (!_hotkeys.Register(id, key, action))
                failed.Add($"{id} ({KeyNames.Display(keyName)})");
        }

        Reg("win.hide", s.WindowHotkeys.HideShow, ToggleVisible);
        Reg("win.exit", s.WindowHotkeys.Exit, () => BeginInvoke(Close));
        Reg("win.stop", s.WindowHotkeys.EmergencyStop, () =>
        {
            _macros.EmergencyStop();
            _autoClicker.StopAll();
            _autoCast.StopAll();
            _speedBoost.Disable();
            Persist(immediate: true);
            _refreshAutoCast?.Invoke();
            _refreshSpeedBoost?.Invoke();
            SetStatusMessage(_loc.T("status.stopped"));
        });

        Reg("sell.all", s.Inventory.SellAllHotkey, () => _macros.SellAll());
        if (!SmeltMuted)
            Reg("smelt", s.Inventory.SmeltHotkey, () => _macros.Smelt());
        Reg("fastmount", s.FastMount.FastMountHotkey, () => _macros.FastMount());
        Reg("jumpmount", s.FastMount.JumpMountHotkey, () => _macros.JumpMount());

        foreach (var p in s.ExtraSkill.Profiles)
        {
            var profile = p;
            Reg("es:" + profile.Id, profile.Hotkey, () => _macros.ExtraSkill(profile));
        }
        if (!MutedNavIds.Contains("gearswap"))
        {
            foreach (var p in s.GearSwap.Profiles)
            {
                var profile = p;
                Reg("gs:" + profile.Id, profile.Hotkey, () => _macros.GearSwap(profile));
            }
        }
        foreach (var p in s.FastTeleport.Locations)
        {
            var loc = p;
            Reg("tp:" + loc.Id, loc.Hotkey, () => _macros.Teleport(loc));
        }
        if (!MutedNavIds.Contains("autocast"))
        {
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

        Reg("speedboost.toggle", s.SpeedBoost.ToggleHotkey, () =>
        {
            BeginInvoke(() =>
            {
                SpeedBoostLog.Host("Hotkey speedboost.toggle", includeStack: true);
                _speedBoost.Toggle();
                _refreshSpeedBoost?.Invoke();
            });
        });

        if (failed.Count > 0)
            SetStatusMessage(_loc.Tf("hotkey.registerFail", string.Join(", ", failed)), 6000);
    }

    private void RelocalizeNav()
    {
        foreach (var (id, key) in NavItems)
        {
            if (!_navButtons.TryGetValue(id, out var btn)) continue;
            btn.Text = _loc.T(key);
            if (MutedNavIds.Contains(id))
                _toolTips.SetToolTip(btn, _loc.T("wip.tooltip"));
        }

        foreach (Control c in _navRail.Controls)
        {
            if (c is not FlowLayoutPanel flp) continue;
            foreach (Control child in flp.Controls)
            {
                if (child is NavSectionHeader { Tag: string groupKey } header)
                    header.Title = _loc.T(groupKey);
            }
        }

        RelocalizeTray();
    }

    private void RebuildUi(string? stayOn = null)
    {
        var current = stayOn ?? _activePage;
        if (MutedNavIds.Contains(current))
            current = "play";
        _refreshAllLabels = null;
        _refreshBindingMap = null;
        _refreshInventory = null;
        _refreshGearSwap = null;
        _refreshExtraSkill = null;
        _refreshTeleport = null;
        _refreshAutoCast = null;
        _refreshSpeedBoost = null;
        _refreshChecklist = null;
        BuildPages();
        RelocalizeNav();
        ShowPage(current);
        RefreshStatusBar();
    }

    private void MaybeShowFirstRunWizard()
    {
        var s = _settings.Current;
        if (s.FirstRunWizardDone) return;
        if (s.Inventory.FirstCell != null)
        {
            s.FirstRunWizardDone = true;
            _settings.Save();
            return;
        }

        using var wizard = new FirstRunWizardForm(_loc, _settings, PickCoordForWizard, () =>
        {
            UpdateGridOverlay();
            Persist(immediate: true);
        });
        wizard.ShowDialog(this);
        s.FirstRunWizardDone = true;
        _settings.Save();
        RebuildUi("inventory");
    }

    private ScreenPoint? PickCoordForWizard(int size, bool useOrigin)
    {
        using var overlay = new OverlayForm(size, rectangle: true, hintText: _loc.T("overlay.hint"));
        Hide();
        overlay.ShowDialog();
        Show();
        if (overlay.Result == null) return null;
        var p = useOrigin && overlay.Origin != null ? overlay.Origin.Value : overlay.Result.Value;
        return new ScreenPoint(p.X, p.Y);
    }
}
