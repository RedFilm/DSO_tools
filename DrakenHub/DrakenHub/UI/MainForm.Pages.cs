using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    private const int CW = 980;

    private Panel BuildInventoryPage()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var subNav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Theme.Bg,
            Padding = new Padding(8, 6, 8, 4),
            WrapContents = false,
            AutoScroll = false
        };

        var sellBtn = TabChip(_loc.T("inv.sell"), true);
        var smeltBtn = TabChip(_loc.T("inv.smelt"), false);
        var sellPage = BuildSellSubPage();
        var smeltPage = BuildSmeltSubPage();
        var host = UiFactory.ScrollHost(sellPage, CW);

        void ShowSub(string id)
        {
            _inventorySub = id;
            sellBtn.BackColor = id == "sell" ? Theme.TabActive : Theme.TabIdle;
            smeltBtn.BackColor = id == "smelt" ? Theme.TabActive : Theme.TabIdle;
            host.Controls.Clear();
            var page = id == "sell" ? sellPage : smeltPage;
            page.Location = new Point(12, 12);
            host.Controls.Add(page);
        }

        sellBtn.Click += (_, _) => ShowSub("sell");
        smeltBtn.Click += (_, _) => ShowSub("smelt");
        subNav.Controls.Add(sellBtn);
        subNav.Controls.Add(smeltBtn);

        // Fill first, Top second — correct WinForms dock z-order.
        root.Controls.Add(host);
        root.Controls.Add(subNav);

        _refreshAllLabels += () =>
        {
            sellBtn.Text = _loc.T("inv.sell");
            smeltBtn.Text = _loc.T("inv.smelt");
        };
        return root;
    }

    private static Button TabChip(string text, bool active)
    {
        var b = UiFactory.Btn(text, active ? Theme.TabActive : Theme.TabIdle, 28);
        b.ForeColor = Theme.Text;
        b.AutoSize = true;
        b.MaximumSize = new Size(0, 28);
        b.Padding = new Padding(14, 0, 14, 0);
        return b;
    }

    private Control BuildSellSubPage()
    {
        var col = UiFactory.Col(CW);
        var inv = _settings.Current.Inventory;

        var sellAll = UiFactory.Btn("", Theme.Green);
        BindLaunchButton(sellAll, () => inv.SellAllHotkey, v => inv.SellAllHotkey = v, "sell.all", "sell.all");
        var sellCur = UiFactory.Btn("", Theme.Green);
        BindLaunchButton(sellCur, () => inv.SellCurrentHotkey, v => inv.SellCurrentHotkey = v, "sell.current", "sell.current");
        var top = UiFactory.Row();
        UiFactory.AddEqual(top, CW, sellAll, sellCur);
        col.Controls.Add(top);

        var setupInner = UiFactory.Col(CW - 20);
        var invKey = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(invKey, () => inv.InventoryHotkey, v =>
        {
            inv.InventoryHotkey = v;
            _settings.Current.GearSwap.InventoryHotkey = v;
        }, "btn.inventory");
        var closeInv = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(closeInv, () => inv.CloseInventory, v => inv.CloseInventory = v, "btn.closeInv");
        var firstTab = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(firstTab, () => inv.FirstTab, v => inv.FirstTab = v, "btn.firstTab");
        var firstCell = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(firstCell, () => inv.FirstCell, v => inv.FirstCell = v, "btn.firstCell", () => inv.Grid.CellSize);
        var r1 = UiFactory.Row(); UiFactory.AddEqual(r1, CW - 20, invKey, closeInv); setupInner.Controls.Add(r1);
        var r2 = UiFactory.Row(); UiFactory.AddEqual(r2, CW - 20, firstTab, firstCell); setupInner.Controls.Add(r2);
        col.Controls.Add(UiFactory.Section(_loc.T("section.setup"), setupInner, CW));

        var shopInner = UiFactory.Col(CW - 20);
        var skills = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(skills, () => inv.SkillsWindowHotkey, v => inv.SkillsWindowHotkey = v, "btn.skills");
        var wisdom = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(wisdom, () => inv.WisdomTab, v => inv.WisdomTab = v, "btn.wisdom");
        var shop = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(shop, () => inv.ShopButton, v => inv.ShopButton = v, "btn.shop");
        var shopRow = UiFactory.Row();
        UiFactory.AddEqual(shopRow, CW - 20, skills, wisdom, shop);
        shopInner.Controls.Add(shopRow);
        col.Controls.Add(UiFactory.Section(_loc.T("section.shop"), shopInner, CW));

        col.Controls.Add(BuildGridSettingsSection());
        col.Controls.Add(BuildTabsSection());
        col.Controls.Add(BuildRaritiesSection());
        col.Controls.Add(BuildOtherItemsSection());
        return col;
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
            Persist();
            Refresh();
        };
        _refreshAllLabels += Refresh;
    }

    private Control BuildGridSettingsSection()
    {
        var g = _settings.Current.Inventory.Grid;
        var body = UiFactory.Col(CW);
        body.Visible = false;

        var show = UiFactory.Check(_loc.T("grid.show"), g.ShowGrid);
        show.CheckedChanged += (_, _) => { g.ShowGrid = show.Checked; Persist(); };
        var fast = UiFactory.Check(_loc.T("grid.fast"), g.FastFirstScan);
        fast.CheckedChanged += (_, _) => { g.FastFirstScan = fast.Checked; Persist(); };
        var checks = UiFactory.Row(36);
        checks.Width = CW;
        checks.Controls.Add(show);
        checks.Controls.Add(fast);
        body.Controls.Add(checks);

        void NumRow(string key, Func<int> get, Action<int> set, int min, int max)
        {
            var label = UiFactory.Lbl(_loc.T(key));
            label.MinimumSize = new Size(220, 0);
            var num = UiFactory.Num(min, max, get());
            num.ValueChanged += (_, _) => { set((int)num.Value); Persist(); };
            var row = UiFactory.Row(36);
            UiFactory.AddSized(row, CW, (label, 220), (num, 90));
            body.Controls.Add(row);
            _refreshAllLabels += () => label.Text = _loc.T(key);
        }

        NumRow("grid.cell", () => g.CellSize, v => g.CellSize = v, 16, 128);
        NumRow("grid.tab", () => g.TabMarkerSize, v => g.TabMarkerSize = v, 8, 64);
        NumRow("grid.gapx", () => g.GapX, v => g.GapX = v, 0, 40);
        NumRow("grid.gapy", () => g.GapY, v => g.GapY = v, 0, 40);
        NumRow("grid.tabx", () => g.TabOffsetX, v => g.TabOffsetX = v, 10, 120);
        NumRow("grid.cross", () => g.CrosshairSize, v => g.CrosshairSize = v, 8, 80);
        NumRow("grid.scan", () => g.ScanLevel, v => g.ScanLevel = v, 1, 2);
        NumRow("grid.cols", () => g.Columns, v => g.Columns = v, 1, 20);
        NumRow("grid.rows", () => g.Rows, v => g.Rows = v, 1, 20);
        NumRow("grid.tol", () => g.ColorTolerance, v => g.ColorTolerance = v, 0, 80);

        var header = UiFactory.Btn("▸ " + _loc.T("section.grid"), Theme.TabIdle, 30);
        header.Width = CW;
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.ForeColor = Theme.Text;
        header.MaximumSize = new Size(0, 30);

        var wrap = UiFactory.Col(CW);
        header.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾ " : "▸ ") + _loc.T("section.grid");
        };
        wrap.Controls.Add(header);
        wrap.Controls.Add(body);
        _refreshAllLabels += () =>
        {
            show.Text = _loc.T("grid.show");
            fast.Text = _loc.T("grid.fast");
            header.Text = (body.Visible ? "▾ " : "▸ ") + _loc.T("section.grid");
        };
        return wrap;
    }

    private Control BuildTabsSection()
    {
        var inner = UiFactory.Row(36);
        inner.Width = CW - 20;
        var tabs = _settings.Current.Inventory.SelectedTabs;
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            var cb = UiFactory.Check((i + 1).ToString(), tabs[i]);
            cb.CheckedChanged += (_, _) => { tabs[index] = cb.Checked; Persist(); };
            inner.Controls.Add(cb);
        }
        return UiFactory.Section(_loc.T("section.tabs"), inner, CW);
    }

    private Control BuildRaritiesSection()
    {
        var host = UiFactory.Col(CW - 20);
        void Rebuild()
        {
            host.Controls.Clear();
            foreach (var rarity in _settings.Current.Inventory.Rarities)
            {
                var r = rarity;
                var colorBox = new Panel
                {
                    Width = 18,
                    Height = 18,
                    BackColor = Color.FromArgb(r.DisplayColorArgb),
                    Margin = new Padding(6, 10, 6, 4)
                };
                var enabled = UiFactory.Check(r.Name, r.Enabled);
                enabled.MinimumSize = new Size(100, 0);
                enabled.CheckedChanged += (_, _) => { r.Enabled = enabled.Checked; Persist(); };
                var pointsLabel = UiFactory.Lbl($"{r.Points.Count} {_loc.T("points")}", true);

                var add = UiFactory.Btn(_loc.T("btn.addPoint"), Theme.Orange);
                add.Click += (_, _) => { AddColorPoint(r.Points); Rebuild(); };
                var del = UiFactory.Btn(_loc.T("btn.delLast"), Theme.Red);
                del.Click += (_, _) =>
                {
                    if (r.Points.Count > 0) r.Points.RemoveAt(r.Points.Count - 1);
                    Persist();
                    Rebuild();
                };
                var clear = UiFactory.Btn(_loc.T("btn.clear"), Theme.Orange);
                clear.Click += (_, _) => { r.Points.Clear(); Persist(); Rebuild(); };

                var row = UiFactory.Row();
                UiFactory.AddSized(row, CW - 20,
                    (colorBox, 24), (enabled, 110), (add, 150), (del, 150), (clear, 120), (pointsLabel, 80));
                host.Controls.Add(row);
            }
        }
        Rebuild();
        _refreshInventory = Rebuild;
        var section = UiFactory.Section(_loc.T("section.rarities"), host, CW);
        _refreshAllLabels += () => UiFactory.SetSectionTitle(section, _loc.T("section.rarities"));
        return section;
    }

    private Control BuildOtherItemsSection()
    {
        var list = UiFactory.Col(CW - 20);
        void Rebuild()
        {
            list.Controls.Clear();
            foreach (var item in _settings.Current.Inventory.OtherItems.ToList())
            {
                var it = item;
                var enabled = UiFactory.Check(it.Name, it.Enabled);
                enabled.CheckedChanged += (_, _) => { it.Enabled = enabled.Checked; Persist(); };
                var name = UiFactory.Tb(it.Name); name.Width = 150;
                name.TextChanged += (_, _) => { it.Name = name.Text; enabled.Text = name.Text; };
                name.Leave += (_, _) => Persist();
                var pointsLabel = UiFactory.Lbl($"{it.Points.Count} {_loc.T("points")}", true);
                var add = UiFactory.Btn(_loc.T("btn.addPoint"), Theme.Orange);
                add.Click += (_, _) => { AddColorPoint(it.Points); Rebuild(); };
                var del = UiFactory.Btn(_loc.T("btn.delLast"), Theme.Red);
                del.Click += (_, _) =>
                {
                    if (it.Points.Count > 0) it.Points.RemoveAt(it.Points.Count - 1);
                    Persist();
                    Rebuild();
                };
                var clear = UiFactory.Btn(_loc.T("btn.clear"), Theme.Orange);
                clear.Click += (_, _) => { it.Points.Clear(); Persist(); Rebuild(); };
                var row = UiFactory.Row();
                UiFactory.AddSized(row, CW - 20,
                    (enabled, 110), (name, 150), (add, 150), (del, 150), (clear, 120), (pointsLabel, 80));
                list.Controls.Add(row);
            }
        }

        var addItem = UiFactory.Btn(_loc.T("btn.addItem"), Theme.Tan);
        addItem.Click += (_, _) =>
        {
            _settings.Current.Inventory.OtherItems.Add(new CustomItemProfile { Name = "Item" });
            Persist();
            Rebuild();
        };
        var delLast = UiFactory.Btn(_loc.T("delete.last"), Theme.Red);
        delLast.Click += (_, _) =>
        {
            var items = _settings.Current.Inventory.OtherItems;
            if (items.Count > 0) items.RemoveAt(items.Count - 1);
            Persist();
            Rebuild();
        };

        var wrap = UiFactory.Col(CW - 20);
        var actions = UiFactory.Row();
        UiFactory.AddEqual(actions, CW - 20, addItem, delLast);
        wrap.Controls.Add(actions);
        wrap.Controls.Add(list);
        Rebuild();
        return UiFactory.Section(_loc.T("section.other"), wrap, CW);
    }

    private Control BuildSmeltSubPage()
    {
        var col = UiFactory.Col(CW);
        var inv = _settings.Current.Inventory;
        var smelter = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(smelter, () => inv.SmelterItem, v => inv.SmelterItem = v, "smelt.select", () => inv.Grid.CellSize);
        var button = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(button, () => inv.SmeltButton, v => inv.SmeltButton = v, "smelt.button");
        var start = UiFactory.Btn("", Theme.Green);
        BindLaunchButton(start, () => inv.SmeltHotkey, v => inv.SmeltHotkey = v, "smelt", "smelt.start");
        var row = UiFactory.Row();
        UiFactory.AddEqual(row, CW, smelter, button, start);
        col.Controls.Add(row);
        col.Controls.Add(UiFactory.Lbl("Плавка использует сетку, вкладки, редкости и уровень сканирования из раздела «Продажа».", true));
        return col;
    }

    private Panel BuildAutoClickerPage()
    {
        var col = UiFactory.Col(CW);
        var s = _settings.Current.AutoClicker;

        Control BuildSide(string titleKey, Func<string> getMode, Action<string> setMode,
            Func<string?> getKey, Action<string?> setKey, string keyId,
            Func<int> getDelay, Action<int> setDelay, string keyLabel)
        {
            var inner = UiFactory.Col(CW - 20);
            var mode = UiFactory.Combo(_loc.T("mode.hold"), _loc.T("mode.toggle"));
            mode.Width = 200;
            mode.SelectedIndex = getMode() == "toggle" ? 1 : 0;
            mode.SelectedIndexChanged += (_, _) =>
            {
                setMode(mode.SelectedIndex == 1 ? "toggle" : "hold");
                Persist();
            };
            var keyBtn = UiFactory.Btn("", Theme.Green);
            keyBtn.Tag = "flex";
            BindLaunchButton(keyBtn, getKey, setKey, keyId, keyLabel);

            var waitLabel = UiFactory.Lbl(_loc.T("wait"));
            waitLabel.MinimumSize = new Size(100, 0);
            var track = UiFactory.Slider(1, 200, getDelay(), 420);
            var value = UiFactory.Lbl(getDelay().ToString());
            value.MinimumSize = new Size(40, 0);
            var speed = UiFactory.Lbl(_loc.T("speed"), true);
            track.ValueChanged += (_, _) =>
            {
                setDelay(track.Value);
                value.Text = track.Value.ToString();
                Persist();
            };

            var row1 = UiFactory.Row();
            UiFactory.AddSized(row1, CW - 20, (mode, 200), (keyBtn, 700));
            var row2 = UiFactory.Row(36);
            UiFactory.AddSized(row2, CW - 20, (waitLabel, 110), (track, 420), (value, 48), (speed, 140));
            inner.Controls.Add(row1);
            inner.Controls.Add(row2);

            _refreshAllLabels += () =>
            {
                var sel = mode.SelectedIndex;
                mode.Items.Clear();
                mode.Items.Add(_loc.T("mode.hold"));
                mode.Items.Add(_loc.T("mode.toggle"));
                mode.SelectedIndex = sel >= 0 ? sel : 0;
                waitLabel.Text = _loc.T("wait");
                speed.Text = _loc.T("speed");
            };
            return UiFactory.Section(_loc.T(titleKey), inner, CW);
        }

        col.Controls.Add(BuildSide("click.left", () => s.LeftMode, v => s.LeftMode = v,
            () => s.LeftHotkey, v => s.LeftHotkey = v, "aclick.left",
            () => s.LeftDelayMs, v => s.LeftDelayMs = v, "click.left"));
        col.Controls.Add(BuildSide("click.right", () => s.RightMode, v => s.RightMode = v,
            () => s.RightHotkey, v => s.RightHotkey = v, "aclick.right",
            () => s.RightDelayMs, v => s.RightDelayMs = v, "click.right"));
        return UiFactory.ScrollHost(col, CW);
    }

    private Panel BuildFastMountPage()
    {
        var col = UiFactory.Col(CW);
        var fm = _settings.Current.FastMount;

        var control = UiFactory.Col(CW - 20);
        var cape = UiFactory.Btn("", Theme.Orange);
        BindCellButton(cape, () => fm.CapeCell, v => fm.CapeCell = v, "cape");
        var mount = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(mount, () => fm.MountHotkey, v => fm.MountHotkey = v, "mount");
        var fast = UiFactory.Btn("", Theme.Green);
        BindLaunchButton(fast, () => fm.FastMountHotkey, v => fm.FastMountHotkey = v, "fastmount", "fastmount");
        var cRow = UiFactory.Row();
        UiFactory.AddEqual(cRow, CW - 20, cape, mount, fast);
        control.Controls.Add(cRow);
        col.Controls.Add(UiFactory.Section(_loc.T("section.control"), control, CW));

        var settings = UiFactory.Col(CW - 20);
        var track = UiFactory.Slider(1, 200, fm.ClickDelayMs, 500);
        var val = UiFactory.Lbl(fm.ClickDelayMs.ToString());
        val.MinimumSize = new Size(40, 0);
        var delayLabel = UiFactory.Lbl(_loc.T("delay"), true);
        track.ValueChanged += (_, _) =>
        {
            fm.ClickDelayMs = track.Value;
            val.Text = track.Value.ToString();
            Persist();
        };
        var sRow = UiFactory.Row(36);
        UiFactory.AddSized(sRow, CW - 20, (track, 500), (val, 48), (delayLabel, 180));
        settings.Controls.Add(sRow);
        col.Controls.Add(UiFactory.Section(_loc.T("section.settings"), settings, CW));

        var jump = UiFactory.Col(CW - 20);
        var main = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(main, () => fm.MainPanelHotkey, SetMainPanelHotkey, "mainPanel");
        var target = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(target, () => fm.JumpTargetPanelHotkey, v => fm.JumpTargetPanelHotkey = v, "targetPanel");
        var skill = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(skill, () => fm.JumpSkillHotkey, v => fm.JumpSkillHotkey = v, "skillSlot");
        var jumpKey = UiFactory.Btn("", Theme.Green);
        BindLaunchButton(jumpKey, () => fm.JumpMountHotkey, v => fm.JumpMountHotkey = v, "jumpmount", "jumpmount");
        var j1 = UiFactory.Row(); UiFactory.AddEqual(j1, CW - 20, main, target, skill); jump.Controls.Add(j1);
        var j2 = UiFactory.Row(); UiFactory.AddEqual(j2, CW - 20, jumpKey); jump.Controls.Add(j2);
        col.Controls.Add(UiFactory.Section(_loc.T("section.jump"), jump, CW));
        return UiFactory.ScrollHost(col, CW);
    }

    private Panel BuildExtraSkillPage()
    {
        var col = UiFactory.Col(CW);
        var es = _settings.Current.ExtraSkill;

        var mainWrap = UiFactory.Col(CW - 20);
        var main = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(main, () => es.MainPanelHotkey, SetMainPanelHotkey, "mainPanel");
        var mRow = UiFactory.Row(); UiFactory.AddEqual(mRow, CW - 20, main); mainWrap.Controls.Add(mRow);
        col.Controls.Add(UiFactory.Section(_loc.T("mainPanel"), mainWrap, CW));

        var listHost = UiFactory.Col(CW - 20);
        void Rebuild()
        {
            listHost.Controls.Clear();
            foreach (var profile in es.Profiles.ToList())
                listHost.Controls.Add(BuildExtraSkillBlock(profile));
        }

        var add = UiFactory.Btn(_loc.T("add.extraskill"), Theme.Tan);
        add.Click += (_, _) =>
        {
            es.Profiles.Add(new ExtraSkillProfile { Name = $"Extra Skill {es.Profiles.Count + 1}" });
            Persist();
            Rebuild();
        };
        var del = UiFactory.Btn(_loc.T("delete.last"), Theme.Red);
        del.Click += (_, _) =>
        {
            if (es.Profiles.Count > 0) es.Profiles.RemoveAt(es.Profiles.Count - 1);
            Persist();
            Rebuild();
        };

        var skillsInner = UiFactory.Col(CW - 20);
        var actions = UiFactory.Row();
        UiFactory.AddEqual(actions, CW - 20, add, del);
        skillsInner.Controls.Add(actions);
        skillsInner.Controls.Add(listHost);
        col.Controls.Add(UiFactory.Section(_loc.T("skills"), skillsInner, CW));
        Rebuild();
        _refreshExtraSkill = Rebuild;
        return UiFactory.ScrollHost(col, CW);
    }

    private Control BuildExtraSkillBlock(ExtraSkillProfile profile)
    {
        var wrap = UiFactory.Col(CW - 20);
        var body = UiFactory.Col(CW - 20);
        var nameLabel = UiFactory.Lbl(_loc.T("skillName"));
        var name = UiFactory.Tb(profile.Name); name.Width = 360;
        name.TextChanged += (_, _) => profile.Name = name.Text;
        name.Leave += (_, _) => Persist();
        var target = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(target, () => profile.TargetPanelHotkey, v => profile.TargetPanelHotkey = v, "targetPanel");
        var skill = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(skill, () => profile.SkillSlotHotkey, v => profile.SkillSlotHotkey = v, "skillSlot");
        var nRow = UiFactory.Row(36); UiFactory.AddSized(nRow, CW - 20, (nameLabel, 140), (name, 360)); body.Controls.Add(nRow);
        var sRow = UiFactory.Row(); UiFactory.AddEqual(sRow, CW - 20, target, skill); body.Controls.Add(sRow);

        var headerBtn = UiFactory.Btn("▾ " + profile.Name, Theme.TabIdle, 30);
        headerBtn.TextAlign = ContentAlignment.MiddleLeft;
        headerBtn.ForeColor = Theme.Text;
        headerBtn.Tag = "flex";
        headerBtn.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            headerBtn.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;
        };
        name.TextChanged += (_, _) => headerBtn.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;

        var hotkey = UiFactory.Btn("", Theme.Green, 30);
        BindLaunchButton(hotkey, () => profile.Hotkey, v => profile.Hotkey = v, "es:" + profile.Id, "hotkey");
        var hRow = UiFactory.Row();
        UiFactory.AddSized(hRow, CW - 20, (headerBtn, 650), (hotkey, 260));
        wrap.Controls.Add(hRow);
        wrap.Controls.Add(body);
        return wrap;
    }

    private Panel BuildGearSwapPage()
    {
        var col = UiFactory.Col(CW);
        var gs = _settings.Current.GearSwap;

        var invKey = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(invKey, () => gs.InventoryHotkey, v =>
        {
            gs.InventoryHotkey = v;
            _settings.Current.Inventory.InventoryHotkey = v;
        }, "btn.inventory");
        var main = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(main, () => gs.MainPanelHotkey, SetMainPanelHotkey, "mainPanel");
        var add = UiFactory.Btn(_loc.T("add.gearswap"), Theme.Tan);
        var del = UiFactory.Btn(_loc.T("delete.last"), Theme.Red);
        var top = UiFactory.Row();
        UiFactory.AddEqual(top, CW, invKey, main, add, del);
        col.Controls.Add(top);

        var track = UiFactory.Slider(1, 200, gs.ClickDelayMs, 500);
        var val = UiFactory.Lbl(gs.ClickDelayMs.ToString());
        val.MinimumSize = new Size(40, 0);
        var delayLabel = UiFactory.Lbl(_loc.T("delay"), true);
        track.ValueChanged += (_, _) =>
        {
            gs.ClickDelayMs = track.Value;
            val.Text = track.Value.ToString();
            Persist();
        };
        var dRow = UiFactory.Row(36);
        UiFactory.AddSized(dRow, CW, (track, 500), (val, 48), (delayLabel, 180));
        col.Controls.Add(dRow);

        var list = UiFactory.Col(CW);
        void Rebuild()
        {
            list.Controls.Clear();
            foreach (var p in gs.Profiles.ToList())
                list.Controls.Add(BuildGearSwapBlock(p));
        }
        add.Click += (_, _) =>
        {
            gs.Profiles.Add(new GearSwapProfile { Name = $"Gear Swap {gs.Profiles.Count + 1}" });
            Persist();
            Rebuild();
        };
        del.Click += (_, _) =>
        {
            if (gs.Profiles.Count > 0) gs.Profiles.RemoveAt(gs.Profiles.Count - 1);
            Persist();
            Rebuild();
        };
        Rebuild();
        _refreshGearSwap = Rebuild;
        col.Controls.Add(list);
        return UiFactory.ScrollHost(col, CW);
    }

    private Control BuildGearSwapBlock(GearSwapProfile profile)
    {
        var wrap = UiFactory.Col(CW);
        var body = UiFactory.Col(CW);
        var nameLabel = UiFactory.Lbl(_loc.T("name"));
        var name = UiFactory.Tb(profile.Name); name.Width = 320;
        name.TextChanged += (_, _) => profile.Name = name.Text;
        name.Leave += (_, _) => Persist();

        var target = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(target, () => profile.TargetPanelHotkey, v => profile.TargetPanelHotkey = v, "targetPanel");
        var skill = UiFactory.Btn("", Theme.Cyan);
        BindUtilityKeyButton(skill, () => profile.SkillSlotHotkey, v => profile.SkillSlotHotkey = v, "skillSlot");
        var tab = UiFactory.Combo(Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray());
        tab.Width = 80;
        tab.SelectedIndex = Math.Clamp(profile.InventoryTab - 1, 0, 8);
        tab.SelectedIndexChanged += (_, _) =>
        {
            profile.InventoryTab = tab.SelectedIndex + 1;
            Persist();
        };
        var tabLabel = UiFactory.Lbl(_loc.T("tab"));

        var itemsHost = UiFactory.Col(CW);
        void RebuildItems()
        {
            itemsHost.Controls.Clear();
            for (var i = 0; i < profile.Items.Count; i++)
            {
                var index = i;
                var btn = UiFactory.Btn($"{_loc.T("item")} {index + 1}: {profile.Items[index]}", Theme.Orange);
                btn.Width = 280;
                btn.Click += (_, _) =>
                {
                    var size = _settings.Current.Inventory.Grid.CellSize;
                    using var overlay = new OverlayForm(size, true);
                    Hide();
                    overlay.ShowDialog();
                    Show();
                    if (overlay.Result == null)
                        profile.Items.RemoveAt(index);
                    else
                        profile.Items[index] = InventoryLayout.PointToCell(_settings.Current.Inventory, overlay.Result.Value)
                                              ?? GuessCellFromPoint(overlay.Result.Value);
                    Persist();
                    RebuildItems();
                };
                var ir = UiFactory.Row();
                ir.Width = CW;
                ir.Controls.Add(btn);
                itemsHost.Controls.Add(ir);
            }
        }

        var addItem = UiFactory.Btn(_loc.T("add.item"), Theme.Tan);
        addItem.Click += (_, _) =>
        {
            profile.Items.Add(new CellRef { Row = 1, Col = 1 });
            Persist();
            RebuildItems();
        };
        var delItem = UiFactory.Btn(_loc.T("delete.last"), Theme.Red);
        delItem.Click += (_, _) =>
        {
            if (profile.Items.Count > 0) profile.Items.RemoveAt(profile.Items.Count - 1);
            Persist();
            RebuildItems();
        };

        var nRow = UiFactory.Row(36); UiFactory.AddSized(nRow, CW, (nameLabel, 90), (name, 320)); body.Controls.Add(nRow);
        var tRow = UiFactory.Row(); UiFactory.AddSized(tRow, CW, (target, 280), (skill, 260), (tabLabel, 70), (tab, 80)); body.Controls.Add(tRow);
        var aRow = UiFactory.Row(); UiFactory.AddEqual(aRow, CW, addItem, delItem); body.Controls.Add(aRow);
        body.Controls.Add(itemsHost);
        RebuildItems();

        var header = UiFactory.Btn("▾ " + profile.Name, Theme.TabIdle, 30);
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.ForeColor = Theme.Text;
        header.Tag = "flex";
        header.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;
        };
        name.TextChanged += (_, _) => header.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;

        var hotkey = UiFactory.Btn("", Theme.Green, 30);
        BindLaunchButton(hotkey, () => profile.Hotkey, v => profile.Hotkey = v, "gs:" + profile.Id, "hotkey");
        var hRow = UiFactory.Row();
        UiFactory.AddSized(hRow, CW, (header, 680), (hotkey, 260));
        wrap.Controls.Add(hRow);
        wrap.Controls.Add(body);
        return wrap;
    }

    private Panel BuildAutoCastPage()
    {
        var col = UiFactory.Col(CW);
        var hdr = UiFactory.Row(28);
        UiFactory.AddSized(hdr, CW,
            (UiFactory.Lbl(_loc.T("skill")), 260),
            (UiFactory.Lbl(_loc.T("interval")), 120),
            (UiFactory.Lbl(_loc.T("status")), 120),
            (UiFactory.Lbl(_loc.T("hotkey")), 300));
        col.Controls.Add(hdr);

        var rowControls = new List<(Button Skill, TextBox Interval, Button Status, Button Hotkey)>();
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var row = _settings.Current.AutoCast.Rows[index];
            var skill = UiFactory.Btn("", Theme.Cyan);
            void RefreshSkill() => skill.Text = $"{_loc.T("skill")} {index + 1}: {KeyNames.Display(row.SkillHotkey)}";
            RefreshSkill();
            skill.Click += (_, _) =>
            {
                skill.Text = _loc.T("pressAny");
                _hotkeys.BeginCapture(key =>
                {
                    row.SkillHotkey = key == Keys.Escape ? null : KeyNames.Format(key);
                    Persist();
                    RefreshSkill();
                });
            };
            _refreshAllLabels += RefreshSkill;

            var interval = UiFactory.Tb(row.IntervalMs.ToString());
            interval.Width = 100;
            interval.Leave += (_, _) =>
            {
                if (int.TryParse(interval.Text, out var ms))
                {
                    row.IntervalMs = Math.Max(50, ms);
                    Persist();
                }
                interval.Text = row.IntervalMs.ToString();
            };

            var status = UiFactory.Btn(row.Enabled ? _loc.T("on") : _loc.T("off"),
                row.Enabled ? Theme.Green : Theme.Red);
            status.Click += (_, _) =>
            {
                _autoCast.Toggle(index);
                status.Text = row.Enabled ? _loc.T("on") : _loc.T("off");
                status.BackColor = row.Enabled ? Theme.Green : Theme.Red;
            };

            var hotkey = UiFactory.Btn("", Theme.Green);
            BindLaunchButton(hotkey, () => row.ToggleHotkey, v => row.ToggleHotkey = v, "ac:" + index, "hotkey");

            rowControls.Add((skill, interval, status, hotkey));
            var line = UiFactory.Row();
            UiFactory.AddSized(line, CW, (skill, 260), (interval, 120), (status, 120), (hotkey, 300));
            col.Controls.Add(line);
        }

        _refreshAutoCast = () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var row = _settings.Current.AutoCast.Rows[i];
                var c = rowControls[i];
                c.Status.Text = row.Enabled ? _loc.T("on") : _loc.T("off");
                c.Status.BackColor = row.Enabled ? Theme.Green : Theme.Red;
            }
        };
        _autoCast.SetUiRefresh(() => _refreshAutoCast?.Invoke());
        return UiFactory.ScrollHost(col, CW);
    }

    private Panel BuildTeleportPage()
    {
        var col = UiFactory.Col(CW);
        var tp = _settings.Current.FastTeleport;

        var global = UiFactory.Col(CW - 20);
        var map = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(map, () => tp.MapItem, v => tp.MapItem = v, "map", () => _settings.Current.Inventory.Grid.CellSize);
        var back = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(back, () => tp.BackButton, v => tp.BackButton = v, "back");
        var enter = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(enter, () => tp.EnterButton, v => tp.EnterButton = v, "enter");
        var gRow = UiFactory.Row();
        UiFactory.AddEqual(gRow, CW - 20, map, back, enter);
        global.Controls.Add(gRow);

        var track = UiFactory.Slider(1, 300, tp.ClickDelayMs, 500);
        var val = UiFactory.Lbl(tp.ClickDelayMs.ToString());
        val.MinimumSize = new Size(40, 0);
        var delayLabel = UiFactory.Lbl(_loc.T("delay"), true);
        track.ValueChanged += (_, _) =>
        {
            tp.ClickDelayMs = track.Value;
            val.Text = track.Value.ToString();
            Persist();
        };
        var dRow = UiFactory.Row(36);
        UiFactory.AddSized(dRow, CW - 20, (track, 500), (val, 48), (delayLabel, 180));
        global.Controls.Add(dRow);
        col.Controls.Add(UiFactory.Section(_loc.T("global"), global, CW));

        var list = UiFactory.Col(CW);
        void Rebuild()
        {
            list.Controls.Clear();
            foreach (var loc in tp.Locations.ToList())
                list.Controls.Add(BuildTeleportBlock(loc));
        }

        var add = UiFactory.Btn(_loc.T("add.location"), Theme.Tan);
        add.Click += (_, _) =>
        {
            tp.Locations.Add(new TeleportLocation { Name = $"Location {tp.Locations.Count + 1}" });
            Persist();
            Rebuild();
        };
        var del = UiFactory.Btn(_loc.T("delete.last"), Theme.Red);
        del.Click += (_, _) =>
        {
            if (tp.Locations.Count > 0) tp.Locations.RemoveAt(tp.Locations.Count - 1);
            Persist();
            Rebuild();
        };
        var aRow = UiFactory.Row();
        UiFactory.AddEqual(aRow, CW, add, del);
        col.Controls.Add(aRow);
        col.Controls.Add(list);
        Rebuild();
        _refreshTeleport = Rebuild;
        return UiFactory.ScrollHost(col, CW);
    }

    private Control BuildTeleportBlock(TeleportLocation loc)
    {
        var wrap = UiFactory.Col(CW);
        var body = UiFactory.Col(CW);
        var nameLabel = UiFactory.Lbl(_loc.T("locName"));
        var name = UiFactory.Tb(loc.Name); name.Width = 400;
        name.TextChanged += (_, _) => loc.Name = name.Text;
        name.Leave += (_, _) => Persist();

        var region = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(region, () => loc.Region, v => loc.Region = v, "region");
        var location = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(location, () => loc.Location, v => loc.Location = v, "location");
        var difficulty = UiFactory.Btn("", Theme.Orange);
        BindCoordButton(difficulty, () => loc.Difficulty, v => loc.Difficulty = v, "difficulty");

        var nRow = UiFactory.Row(36); UiFactory.AddSized(nRow, CW, (nameLabel, 150), (name, 400)); body.Controls.Add(nRow);
        var cRow = UiFactory.Row(); UiFactory.AddEqual(cRow, CW, region, location, difficulty); body.Controls.Add(cRow);

        var header = UiFactory.Btn("▾ " + loc.Name, Theme.TabIdle, 30);
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.ForeColor = Theme.Text;
        header.Tag = "flex";
        header.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾ " : "▸ ") + loc.Name;
        };
        name.TextChanged += (_, _) => header.Text = (body.Visible ? "▾ " : "▸ ") + loc.Name;

        var hotkey = UiFactory.Btn("", Theme.Green, 30);
        BindLaunchButton(hotkey, () => loc.Hotkey, v => loc.Hotkey = v, "tp:" + loc.Id, "hotkey");
        var hRow = UiFactory.Row();
        UiFactory.AddSized(hRow, CW, (header, 680), (hotkey, 260));
        wrap.Controls.Add(hRow);
        wrap.Controls.Add(body);
        return wrap;
    }

    private Panel BuildSettingsPage()
    {
        var col = UiFactory.Col(CW);
        var s = _settings.Current;

        var langInner = UiFactory.Col(CW - 20);
        var lang = UiFactory.Combo("Русский", "English");
        lang.SelectedIndex = s.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        lang.SelectedIndexChanged += (_, _) =>
        {
            s.Language = lang.SelectedIndex == 1 ? "en" : "ru";
            _loc.Language = s.Language;
            Persist();
            RelocalizeNav();
            var current = _activePage;
            _refreshAllLabels = null;
            _refreshInventory = null;
            _refreshGearSwap = null;
            _refreshExtraSkill = null;
            _refreshTeleport = null;
            _refreshAutoCast = null;
            _pages.Clear();
            BuildPages();
            ShowPage(current);
        };
        var lRow = UiFactory.Row(36); langInner.Controls.Add(lRow); lRow.Width = CW - 20; lRow.Controls.Add(lang);
        col.Controls.Add(UiFactory.Section(_loc.T("language"), langInner, CW));

        var resInner = UiFactory.Col(CW - 20);
        var res = UiFactory.Combo("Auto / Custom", "1366x768", "1920x1080", "2560x1440", "3840x2160");
        var idx = res.Items.IndexOf(s.ResolutionPreset);
        res.SelectedIndex = idx >= 0 ? idx : 0;
        res.SelectedIndexChanged += (_, _) =>
        {
            s.ResolutionPreset = res.SelectedItem?.ToString() ?? "Auto / Custom";
            Persist();
        };
        var rRow = UiFactory.Row(36); resInner.Controls.Add(rRow); rRow.Width = CW - 20; rRow.Controls.Add(res);
        col.Controls.Add(UiFactory.Section(_loc.T("resolution"), resInner, CW));

        var profileInner = UiFactory.Col(CW - 20);
        var exportBtn = UiFactory.Btn(_loc.T("export"), Theme.Orange);
        var importBtn = UiFactory.Btn(_loc.T("import"), Theme.Orange);
        exportBtn.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "drakenhub-settings.json" };
            if (dlg.ShowDialog() == DialogResult.OK)
                _settings.Export(dlg.FileName);
        };
        importBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _settings.Import(dlg.FileName);
            _loc.Language = _settings.Current.Language;
            RelocalizeNav();
            var current = _activePage;
            _refreshAllLabels = null;
            _refreshInventory = null;
            _refreshGearSwap = null;
            _refreshExtraSkill = null;
            _refreshTeleport = null;
            _refreshAutoCast = null;
            _pages.Clear();
            BuildPages();
            ShowPage(current);
            RegisterAllHotkeys();
            _autoClicker.ApplySettings();
            _autoCast.ApplySettings();
            UpdateGridOverlay();
        };
        var pRow = UiFactory.Row();
        UiFactory.AddEqual(pRow, CW - 20, exportBtn, importBtn);
        profileInner.Controls.Add(pRow);
        col.Controls.Add(UiFactory.Section(_loc.T("profile"), profileInner, CW));

        var hotInner = UiFactory.Col(CW - 20);
        var hide = UiFactory.Btn("", Theme.Orange);
        BindLaunchButton(hide, () => s.WindowHotkeys.HideShow, v => s.WindowHotkeys.HideShow = v, "win.hide", "hide");
        var exit = UiFactory.Btn("", Theme.Red);
        BindLaunchButton(exit, () => s.WindowHotkeys.Exit, v => s.WindowHotkeys.Exit = v, "win.exit", "exit");
        var stop = UiFactory.Btn("", Theme.Orange);
        BindLaunchButton(stop, () => s.WindowHotkeys.EmergencyStop, v => s.WindowHotkeys.EmergencyStop = v, "win.stop", "stop");
        var hRow = UiFactory.Row();
        UiFactory.AddEqual(hRow, CW - 20, hide, exit, stop);
        hotInner.Controls.Add(hRow);
        col.Controls.Add(UiFactory.Section(_loc.T("winHotkeys"), hotInner, CW));
        return UiFactory.ScrollHost(col, CW);
    }
}
