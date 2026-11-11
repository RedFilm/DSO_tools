using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    private Panel BuildInventoryPage()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var subNav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Theme.Panel,
            Padding = new Padding(12, 8, 12, 6),
            WrapContents = false
        };

        var sellBtn = UiFactory.Chip(_loc.T("inv.sell"), true);
        var smeltBtn = UiFactory.Chip(_loc.T("inv.smelt"), false);
        var sellPage = BuildSellSubPage();
        Panel? smeltPage = null;

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        sellPage.Dock = DockStyle.Fill;
        host.Controls.Add(sellPage);

        void ShowSub(string id)
        {
            _inventorySub = id;
            sellBtn.BackColor = id == "sell" ? Color.FromArgb(28, 64, 52) : Theme.Button;
            sellBtn.FlatAppearance.BorderColor = id == "sell" ? Theme.Accent : Theme.ButtonBorder;
            smeltBtn.BackColor = id == "smelt" ? Color.FromArgb(28, 64, 52) : Theme.Button;
            smeltBtn.FlatAppearance.BorderColor = id == "smelt" ? Theme.Accent : Theme.ButtonBorder;

            if (id == "smelt")
            {
                if (smeltPage == null)
                {
                    smeltPage = BuildSmeltSubPage();
                    smeltPage.Dock = DockStyle.Fill;
                    host.Controls.Add(smeltPage);
                }
                sellPage.Visible = false;
                smeltPage.Visible = true;
                UiFactory.Relayout(smeltPage);
            }
            else
            {
                if (smeltPage != null) smeltPage.Visible = false;
                sellPage.Visible = true;
                UiFactory.Relayout(sellPage);
            }
        }

        sellBtn.Click += (_, _) => ShowSub("sell");
        smeltBtn.Click += (_, _) => ShowSub("smelt");
        subNav.Controls.Add(sellBtn);
        subNav.Controls.Add(smeltBtn);

        root.Controls.Add(host);
        root.Controls.Add(subNav);

        _refreshAllLabels += () =>
        {
            sellBtn.Text = _loc.T("inv.sell");
            smeltBtn.Text = _loc.T("inv.smelt");
        };
        return root;
    }

    private Panel BuildSellSubPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var inv = _settings.Current.Inventory;

        var sellAll = UiFactory.Btn("", BtnKind.Primary);
        BindLaunchButton(sellAll, () => inv.SellAllHotkey, v => inv.SellAllHotkey = v, "sell.all", "sell.all");
        var sellCur = UiFactory.Btn("", BtnKind.Primary);
        BindLaunchButton(sellCur, () => inv.SellCurrentHotkey, v => inv.SellCurrentHotkey = v, "sell.current", "sell.current");
        UiFactory.Add(stack, UiFactory.EqualRow(sellAll, sellCur));

        var setupInner = UiFactory.VStack();
        var invKey = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(invKey, () => inv.InventoryHotkey, v =>
        {
            inv.InventoryHotkey = v;
            _settings.Current.GearSwap.InventoryHotkey = v;
        }, "btn.inventory");
        var closeInv = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(closeInv, () => inv.CloseInventory, v => inv.CloseInventory = v, "btn.closeInv");
        var firstTab = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(firstTab, () => inv.FirstTab, v => inv.FirstTab = v, "btn.firstTab");
        var firstCell = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(firstCell, () => inv.FirstCell, v => inv.FirstCell = v, "btn.firstCell", () => inv.Grid.CellSize);
        setupInner.Controls.Add(UiFactory.EqualRow(invKey, closeInv));
        setupInner.Controls.Add(UiFactory.EqualRow(firstTab, firstCell));
        var setup = UiFactory.Section(_loc.T("section.setup"), setupInner, out var setupTitle);
        UiFactory.Add(stack, setup);
        _refreshAllLabels += () => setupTitle.Text = _loc.T("section.setup");

        var shopInner = UiFactory.VStack();
        var skills = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(skills, () => inv.SkillsWindowHotkey, v => inv.SkillsWindowHotkey = v, "btn.skills");
        var wisdom = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(wisdom, () => inv.WisdomTab, v => inv.WisdomTab = v, "btn.wisdom");
        var shop = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(shop, () => inv.ShopButton, v => inv.ShopButton = v, "btn.shop");
        shopInner.Controls.Add(UiFactory.EqualRow(skills, wisdom, shop));
        var shopSec = UiFactory.Section(_loc.T("section.shop"), shopInner, out var shopTitle);
        UiFactory.Add(stack, shopSec);
        _refreshAllLabels += () => shopTitle.Text = _loc.T("section.shop");

        UiFactory.Add(stack, BuildGridSettingsSection());
        UiFactory.Add(stack, BuildTabsSection());
        UiFactory.Add(stack, BuildRaritiesSection());
        UiFactory.Add(stack, BuildOtherItemsSection());
        return scroll;
    }

    private Control BuildGridSettingsSection()
    {
        var g = _settings.Current.Inventory.Grid;
        var body = UiFactory.VStack();
        body.Visible = false;

        var show = UiFactory.Check(_loc.T("grid.show"), g.ShowGrid);
        show.CheckedChanged += (_, _) => { g.ShowGrid = show.Checked; Persist(immediate: true, hotkeys: false, grid: true); };
        var fast = UiFactory.Check(_loc.T("grid.fast"), g.FastFirstScan);
        fast.CheckedChanged += (_, _) => { g.FastFirstScan = fast.Checked; Persist(hotkeys: false, grid: true); };
        var checks = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Height = 36,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 6)
        };
        checks.Controls.Add(show);
        checks.Controls.Add(fast);
        body.Controls.Add(checks);

        void NumRow(string key, Func<int> get, Action<int> set, int min, int max)
        {
            var label = UiFactory.Lbl(_loc.T(key));
            var num = UiFactory.Num(min, max, get());
            num.ValueChanged += (_, _) => { set((int)num.Value); Persist(hotkeys: false, grid: true); };
            body.Controls.Add(UiFactory.FieldRow(label, num, 220));
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

        var wrap = UiFactory.VStack();
        var header = UiFactory.Btn("▸ " + _loc.T("section.grid"), BtnKind.Neutral, 30);
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.Width = 400;
        header.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾ " : "▸ ") + _loc.T("section.grid");
            UiFactory.Relayout(wrap);
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
        var inner = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Color.Transparent
        };
        var tabs = _settings.Current.Inventory.SelectedTabs;
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            var cb = UiFactory.Check((i + 1).ToString(), tabs[i]);
            cb.CheckedChanged += (_, _) => { tabs[index] = cb.Checked; Persist(hotkeys: false); };
            inner.Controls.Add(cb);
        }
        var section = UiFactory.Section(_loc.T("section.tabs"), inner, out var title);
        _refreshAllLabels += () => title.Text = _loc.T("section.tabs");
        return section;
    }

    private Control BuildRaritiesSection()
    {
        var host = UiFactory.VStack();
        void Rebuild()
        {
            host.SuspendLayout();
            host.Controls.Clear();
            foreach (var rarity in _settings.Current.Inventory.Rarities)
            {
                var r = rarity;
                var colorBox = new Panel { Width = 22, Height = 22, BackColor = Color.FromArgb(r.DisplayColorArgb) };
                var enabled = UiFactory.Check(r.Name, r.Enabled);
                enabled.Width = 110;
                enabled.CheckedChanged += (_, _) => { r.Enabled = enabled.Checked; Persist(grid: true); };
                var pointsLabel = UiFactory.Lbl($"{r.Points.Count} {_loc.T("points")}", true);
                pointsLabel.Width = 72;

                var add = UiFactory.Btn(_loc.T("btn.addPoint"), BtnKind.Warn);
                add.Click += (_, _) => { AddColorPoint(r.Points); Rebuild(); UiFactory.Relayout(host); };
                var del = UiFactory.Btn(_loc.T("btn.delLast"), BtnKind.Danger);
                del.Click += (_, _) =>
                {
                    if (r.Points.Count > 0) r.Points.RemoveAt(r.Points.Count - 1);
                    Persist(grid: true);
                    Rebuild();
                    UiFactory.Relayout(host);
                };
                var clear = UiFactory.Btn(_loc.T("btn.clear"), BtnKind.Warn);
                clear.Click += (_, _) =>
                {
                    r.Points.Clear();
                    Persist(grid: true);
                    Rebuild();
                    UiFactory.Relayout(host);
                };

                host.Controls.Add(UiFactory.ActionRow(
                    (colorBox, 24), (enabled, 120), (add, 0), (del, 0), (clear, 0), (pointsLabel, 72)));
            }
            host.ResumeLayout(false);
            UiFactory.Relayout(host);
        }
        Rebuild();
        _refreshInventory = Rebuild;
        var section = UiFactory.Section(_loc.T("section.rarities"), host, out var title);
        _refreshAllLabels += () => title.Text = _loc.T("section.rarities");
        return section;
    }

    private Control BuildOtherItemsSection()
    {
        var list = UiFactory.VStack();
        void Rebuild()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            foreach (var item in _settings.Current.Inventory.OtherItems.ToList())
            {
                var it = item;
                var enabled = UiFactory.Check(it.Name, it.Enabled);
                enabled.Width = 100;
                enabled.CheckedChanged += (_, _) => { it.Enabled = enabled.Checked; Persist(grid: true); };
                var name = UiFactory.Tb(it.Name);
                name.Width = 140;
                name.TextChanged += (_, _) => { it.Name = name.Text; enabled.Text = name.Text; };
                name.Leave += (_, _) => Persist();
                var pointsLabel = UiFactory.Lbl($"{it.Points.Count} {_loc.T("points")}", true);
                pointsLabel.Width = 72;
                var add = UiFactory.Btn(_loc.T("btn.addPoint"), BtnKind.Warn);
                add.Click += (_, _) => { AddColorPoint(it.Points); Rebuild(); UiFactory.Relayout(list); };
                var del = UiFactory.Btn(_loc.T("btn.delLast"), BtnKind.Danger);
                del.Click += (_, _) =>
                {
                    if (it.Points.Count > 0) it.Points.RemoveAt(it.Points.Count - 1);
                    Persist(grid: true);
                    Rebuild();
                    UiFactory.Relayout(list);
                };
                var clear = UiFactory.Btn(_loc.T("btn.clear"), BtnKind.Warn);
                clear.Click += (_, _) =>
                {
                    it.Points.Clear();
                    Persist(grid: true);
                    Rebuild();
                    UiFactory.Relayout(list);
                };

                list.Controls.Add(UiFactory.ActionRow(
                    (enabled, 110), (name, 140), (add, 0), (del, 0), (clear, 0), (pointsLabel, 72)));
            }
            list.ResumeLayout(false);
            UiFactory.Relayout(list);
        }

        var addItem = UiFactory.Btn(_loc.T("btn.addItem"), BtnKind.Neutral);
        addItem.Click += (_, _) =>
        {
            _settings.Current.Inventory.OtherItems.Add(new CustomItemProfile { Name = "Item" });
            Persist();
            Rebuild();
        };
        var delLast = UiFactory.Btn(_loc.T("delete.last"), BtnKind.Danger);
        delLast.Click += (_, _) =>
        {
            var items = _settings.Current.Inventory.OtherItems;
            if (items.Count > 0) items.RemoveAt(items.Count - 1);
            Persist();
            Rebuild();
        };

        var wrap = UiFactory.VStack();
        wrap.Controls.Add(UiFactory.EqualRow(addItem, delLast));
        wrap.Controls.Add(list);
        Rebuild();
        var section = UiFactory.Section(_loc.T("section.other"), wrap, out var title);
        _refreshAllLabels += () =>
        {
            title.Text = _loc.T("section.other");
            addItem.Text = _loc.T("btn.addItem");
            delLast.Text = _loc.T("delete.last");
        };
        return section;
    }

    private Panel BuildSmeltSubPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var inv = _settings.Current.Inventory;
        var smelter = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(smelter, () => inv.SmelterItem, v => inv.SmelterItem = v, "smelt.select", () => inv.Grid.CellSize);
        var button = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(button, () => inv.SmeltButton, v => inv.SmeltButton = v, "smelt.button");
        var start = UiFactory.Btn("", BtnKind.Primary);
        BindLaunchButton(start, () => inv.SmeltHotkey, v => inv.SmeltHotkey = v, "smelt", "smelt.start");
        UiFactory.Add(stack, UiFactory.EqualRow(smelter, button, start));
        var hint = UiFactory.Lbl("Плавка использует сетку, вкладки, редкости и уровень сканирования из раздела «Продажа».", true);
        UiFactory.Add(stack, hint);
        return scroll;
    }

    private Panel BuildAutoClickerPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var s = _settings.Current.AutoClicker;

        Control BuildSide(string titleKey, Func<string> getMode, Action<string> setMode,
            Func<string?> getKey, Action<string?> setKey, string keyId,
            Func<int> getDelay, Action<int> setDelay, string keyLabel)
        {
            var inner = UiFactory.VStack();
            var mode = UiFactory.Combo(_loc.T("mode.hold"), _loc.T("mode.toggle"));
            mode.SelectedIndex = getMode() == "toggle" ? 1 : 0;
            mode.SelectedIndexChanged += (_, _) =>
            {
                setMode(mode.SelectedIndex == 1 ? "toggle" : "hold");
                Persist(immediate: true);
            };
            var keyBtn = UiFactory.Btn("", BtnKind.Primary);
            BindLaunchButton(keyBtn, getKey, setKey, keyId, keyLabel);
            inner.Controls.Add(UiFactory.EqualRow(mode, keyBtn));

            var waitLabel = UiFactory.Lbl(_loc.T("wait"));
            var track = UiFactory.Slider(1, 200, getDelay());
            var value = UiFactory.Lbl(getDelay().ToString());
            var speed = UiFactory.Lbl(_loc.T("speed"), true);
            track.ValueChanged += (_, _) =>
            {
                setDelay(track.Value);
                value.Text = track.Value.ToString();
            };
            track.DragEnded += (_, _) => Persist();
            var delayRow = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 1,
                Height = 36,
                BackColor = Color.Transparent
            };
            delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            waitLabel.Dock = DockStyle.Fill;
            track.Dock = DockStyle.Fill;
            value.Dock = DockStyle.Fill;
            speed.Dock = DockStyle.Fill;
            delayRow.Controls.Add(waitLabel, 0, 0);
            delayRow.Controls.Add(track, 1, 0);
            delayRow.Controls.Add(value, 2, 0);
            delayRow.Controls.Add(speed, 3, 0);
            inner.Controls.Add(delayRow);

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
            var section = UiFactory.Section(_loc.T(titleKey), inner, out var title);
            _refreshAllLabels += () => title.Text = _loc.T(titleKey);
            return section;
        }

        UiFactory.Add(stack, BuildSide("click.left", () => s.LeftMode, v => s.LeftMode = v,
            () => s.LeftHotkey, v => s.LeftHotkey = v, "aclick.left",
            () => s.LeftDelayMs, v => s.LeftDelayMs = v, "click.left"));
        UiFactory.Add(stack, BuildSide("click.right", () => s.RightMode, v => s.RightMode = v,
            () => s.RightHotkey, v => s.RightHotkey = v, "aclick.right",
            () => s.RightDelayMs, v => s.RightDelayMs = v, "click.right"));
        return scroll;
    }

    private Panel BuildFastMountPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var fm = _settings.Current.FastMount;

        var control = UiFactory.VStack();
        var cape = UiFactory.Btn("", BtnKind.Warn);
        BindCellButton(cape, () => fm.CapeCell, v => fm.CapeCell = v, "cape");
        var mount = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(mount, () => fm.MountHotkey, v => fm.MountHotkey = v, "mount");
        var fast = UiFactory.Btn("", BtnKind.Primary);
        BindLaunchButton(fast, () => fm.FastMountHotkey, v => fm.FastMountHotkey = v, "fastmount", "fastmount");
        control.Controls.Add(UiFactory.EqualRow(cape, mount, fast));
        var controlSec = UiFactory.Section(_loc.T("section.control"), control, out var controlTitle);
        UiFactory.Add(stack, controlSec);
        _refreshAllLabels += () => controlTitle.Text = _loc.T("section.control");

        var settings = UiFactory.VStack();
        var track = UiFactory.Slider(1, 200, fm.ClickDelayMs);
        var val = UiFactory.Lbl(fm.ClickDelayMs.ToString());
        var delayLabel = UiFactory.Lbl(_loc.T("delay"), true);
        track.ValueChanged += (_, _) =>
        {
            fm.ClickDelayMs = track.Value;
            val.Text = track.Value.ToString();
        };
        track.DragEnded += (_, _) => Persist();
        var sRow = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = 36,
            BackColor = Color.Transparent
        };
        sRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        sRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        track.Dock = DockStyle.Fill;
        val.Dock = DockStyle.Fill;
        delayLabel.Dock = DockStyle.Fill;
        sRow.Controls.Add(track, 0, 0);
        sRow.Controls.Add(val, 1, 0);
        sRow.Controls.Add(delayLabel, 2, 0);
        settings.Controls.Add(sRow);
        var settingsSec = UiFactory.Section(_loc.T("section.settings"), settings, out var settingsTitle);
        UiFactory.Add(stack, settingsSec);
        _refreshAllLabels += () =>
        {
            settingsTitle.Text = _loc.T("section.settings");
            delayLabel.Text = _loc.T("delay");
        };

        var jump = UiFactory.VStack();
        var main = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(main, () => fm.MainPanelHotkey, SetMainPanelHotkey, "mainPanel");
        var target = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(target, () => fm.JumpTargetPanelHotkey, v => fm.JumpTargetPanelHotkey = v, "targetPanel");
        var skill = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(skill, () => fm.JumpSkillHotkey, v => fm.JumpSkillHotkey = v, "skillSlot");
        var jumpKey = UiFactory.Btn("", BtnKind.Primary);
        BindLaunchButton(jumpKey, () => fm.JumpMountHotkey, v => fm.JumpMountHotkey = v, "jumpmount", "jumpmount");
        jump.Controls.Add(UiFactory.EqualRow(main, target, skill));
        jump.Controls.Add(UiFactory.EqualRow(jumpKey));
        var jumpSec = UiFactory.Section(_loc.T("section.jump"), jump, out var jumpTitle);
        UiFactory.Add(stack, jumpSec);
        _refreshAllLabels += () => jumpTitle.Text = _loc.T("section.jump");
        return scroll;
    }

    private Panel BuildExtraSkillPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var es = _settings.Current.ExtraSkill;

        var mainWrap = UiFactory.VStack();
        var main = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(main, () => es.MainPanelHotkey, SetMainPanelHotkey, "mainPanel");
        mainWrap.Controls.Add(UiFactory.EqualRow(main));
        var mainSec = UiFactory.Section(_loc.T("mainPanel"), mainWrap, out var mainTitle);
        UiFactory.Add(stack, mainSec);
        _refreshAllLabels += () => mainTitle.Text = _loc.T("mainPanel");

        var listHost = UiFactory.VStack();
        void Rebuild()
        {
            listHost.Controls.Clear();
            foreach (var profile in es.Profiles.ToList())
                listHost.Controls.Add(BuildExtraSkillBlock(profile));
        }

        var add = UiFactory.Btn(_loc.T("add.extraskill"), BtnKind.Neutral);
        add.Click += (_, _) =>
        {
            es.Profiles.Add(new ExtraSkillProfile { Name = $"Extra Skill {es.Profiles.Count + 1}" });
            Persist(immediate: true);
            Rebuild();
        };
        var del = UiFactory.Btn(_loc.T("delete.last"), BtnKind.Danger);
        del.Click += (_, _) =>
        {
            if (es.Profiles.Count > 0) es.Profiles.RemoveAt(es.Profiles.Count - 1);
            Persist(immediate: true);
            Rebuild();
        };

        var skillsInner = UiFactory.VStack();
        skillsInner.Controls.Add(UiFactory.EqualRow(add, del));
        skillsInner.Controls.Add(listHost);
        var skillsSec = UiFactory.Section(_loc.T("skills"), skillsInner, out var skillsTitle);
        UiFactory.Add(stack, skillsSec);
        _refreshAllLabels += () =>
        {
            skillsTitle.Text = _loc.T("skills");
            add.Text = _loc.T("add.extraskill");
            del.Text = _loc.T("delete.last");
        };
        Rebuild();
        _refreshExtraSkill = Rebuild;
        return scroll;
    }

    private Control BuildExtraSkillBlock(ExtraSkillProfile profile)
    {
        var wrap = UiFactory.VStack();
        var body = UiFactory.VStack();
        var nameLabel = UiFactory.Lbl(_loc.T("skillName"));
        var name = UiFactory.Tb(profile.Name);
        name.TextChanged += (_, _) => profile.Name = name.Text;
        name.Leave += (_, _) => Persist();
        var target = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(target, () => profile.TargetPanelHotkey, v => profile.TargetPanelHotkey = v, "targetPanel");
        var skill = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(skill, () => profile.SkillSlotHotkey, v => profile.SkillSlotHotkey = v, "skillSlot");
        body.Controls.Add(UiFactory.FieldRow(nameLabel, name, 140));
        body.Controls.Add(UiFactory.EqualRow(target, skill));

        var headerBtn = UiFactory.Btn("▾ " + profile.Name, BtnKind.Neutral, 30);
        headerBtn.TextAlign = ContentAlignment.MiddleLeft;
        headerBtn.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            headerBtn.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;
        };
        name.TextChanged += (_, _) => headerBtn.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;

        var hotkey = UiFactory.Btn("", BtnKind.Primary, 30);
        BindLaunchButton(hotkey, () => profile.Hotkey, v => profile.Hotkey = v, "es:" + profile.Id, "hotkey");
        wrap.Controls.Add(UiFactory.EqualRow(headerBtn, hotkey));
        wrap.Controls.Add(body);
        return wrap;
    }

    private Panel BuildGearSwapPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var gs = _settings.Current.GearSwap;

        var invKey = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(invKey, () => gs.InventoryHotkey, v =>
        {
            gs.InventoryHotkey = v;
            _settings.Current.Inventory.InventoryHotkey = v;
        }, "btn.inventory");
        var main = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(main, () => gs.MainPanelHotkey, SetMainPanelHotkey, "mainPanel");
        var add = UiFactory.Btn(_loc.T("add.gearswap"), BtnKind.Neutral);
        var del = UiFactory.Btn(_loc.T("delete.last"), BtnKind.Danger);
        UiFactory.Add(stack, UiFactory.EqualRow(invKey, main, add, del));

        var track = UiFactory.Slider(1, 200, gs.ClickDelayMs);
        var val = UiFactory.Lbl(gs.ClickDelayMs.ToString());
        var delayLabel = UiFactory.Lbl(_loc.T("delay"), true);
        track.ValueChanged += (_, _) =>
        {
            gs.ClickDelayMs = track.Value;
            val.Text = track.Value.ToString();
        };
        track.DragEnded += (_, _) => Persist();
        var dRow = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = 36,
            BackColor = Color.Transparent
        };
        dRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        dRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        track.Dock = DockStyle.Fill;
        val.Dock = DockStyle.Fill;
        delayLabel.Dock = DockStyle.Fill;
        dRow.Controls.Add(track, 0, 0);
        dRow.Controls.Add(val, 1, 0);
        dRow.Controls.Add(delayLabel, 2, 0);
        UiFactory.Add(stack, dRow);
        _refreshAllLabels += () =>
        {
            delayLabel.Text = _loc.T("delay");
            add.Text = _loc.T("add.gearswap");
            del.Text = _loc.T("delete.last");
        };

        var list = UiFactory.VStack();
        void Rebuild()
        {
            list.Controls.Clear();
            foreach (var p in gs.Profiles.ToList())
                list.Controls.Add(BuildGearSwapBlock(p));
        }
        add.Click += (_, _) =>
        {
            gs.Profiles.Add(new GearSwapProfile { Name = $"Gear Swap {gs.Profiles.Count + 1}" });
            Persist(immediate: true);
            Rebuild();
        };
        del.Click += (_, _) =>
        {
            if (gs.Profiles.Count > 0) gs.Profiles.RemoveAt(gs.Profiles.Count - 1);
            Persist(immediate: true);
            Rebuild();
        };
        Rebuild();
        _refreshGearSwap = Rebuild;
        UiFactory.Add(stack, list);
        return scroll;
    }

    private Control BuildGearSwapBlock(GearSwapProfile profile)
    {
        var wrap = UiFactory.VStack();
        var body = UiFactory.VStack();
        var nameLabel = UiFactory.Lbl(_loc.T("name"));
        var name = UiFactory.Tb(profile.Name);
        name.TextChanged += (_, _) => profile.Name = name.Text;
        name.Leave += (_, _) => Persist();

        var target = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(target, () => profile.TargetPanelHotkey, v => profile.TargetPanelHotkey = v, "targetPanel");
        var skill = UiFactory.Btn("", BtnKind.Accent);
        BindUtilityKeyButton(skill, () => profile.SkillSlotHotkey, v => profile.SkillSlotHotkey = v, "skillSlot");
        var tab = UiFactory.Combo(Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray());
        tab.SelectedIndex = Math.Clamp(profile.InventoryTab - 1, 0, 8);
        tab.SelectedIndexChanged += (_, _) =>
        {
            profile.InventoryTab = tab.SelectedIndex + 1;
            Persist();
        };
        var tabLabel = UiFactory.Lbl(_loc.T("tab"));

        var itemsHost = UiFactory.VStack();
        void RebuildItems()
        {
            itemsHost.Controls.Clear();
            for (var i = 0; i < profile.Items.Count; i++)
            {
                var index = i;
                var btn = UiFactory.Btn($"{_loc.T("item")} {index + 1}: {profile.Items[index]}", BtnKind.Warn);
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
                    Persist(immediate: true);
                    RebuildItems();
                };
                itemsHost.Controls.Add(UiFactory.EqualRow(btn));
            }
        }

        var addItem = UiFactory.Btn(_loc.T("add.item"), BtnKind.Neutral);
        addItem.Click += (_, _) =>
        {
            profile.Items.Add(new CellRef { Row = 1, Col = 1 });
            Persist();
            RebuildItems();
        };
        var delItem = UiFactory.Btn(_loc.T("delete.last"), BtnKind.Danger);
        delItem.Click += (_, _) =>
        {
            if (profile.Items.Count > 0) profile.Items.RemoveAt(profile.Items.Count - 1);
            Persist();
            RebuildItems();
        };

        body.Controls.Add(UiFactory.FieldRow(nameLabel, name, 90));
        var tabRow = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Height = 38,
            BackColor = Color.Transparent
        };
        tabRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        tabRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        tabRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        tabRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        target.Dock = DockStyle.Fill;
        skill.Dock = DockStyle.Fill;
        tabLabel.Dock = DockStyle.Fill;
        tab.Dock = DockStyle.Fill;
        tabRow.Controls.Add(target, 0, 0);
        tabRow.Controls.Add(skill, 1, 0);
        tabRow.Controls.Add(tabLabel, 2, 0);
        tabRow.Controls.Add(tab, 3, 0);
        body.Controls.Add(tabRow);
        body.Controls.Add(UiFactory.EqualRow(addItem, delItem));
        body.Controls.Add(itemsHost);
        RebuildItems();

        var header = UiFactory.Btn("▾ " + profile.Name, BtnKind.Neutral, 30);
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;
        };
        name.TextChanged += (_, _) => header.Text = (body.Visible ? "▾ " : "▸ ") + profile.Name;

        var hotkey = UiFactory.Btn("", BtnKind.Primary, 30);
        BindLaunchButton(hotkey, () => profile.Hotkey, v => profile.Hotkey = v, "gs:" + profile.Id, "hotkey");
        wrap.Controls.Add(UiFactory.EqualRow(header, hotkey));
        wrap.Controls.Add(body);
        return wrap;
    }

    private Panel BuildAutoCastPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var hdr = new TableLayoutPanel
        {
            ColumnCount = 4,
            RowCount = 1,
            Height = 28,
            BackColor = Color.Transparent
        };
        hdr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        hdr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        hdr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        hdr.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        var hSkill = UiFactory.Lbl(_loc.T("skill"), true);
        var hInterval = UiFactory.Lbl(_loc.T("interval"), true);
        var hStatus = UiFactory.Lbl(_loc.T("status"), true);
        var hHotkey = UiFactory.Lbl(_loc.T("hotkey"), true);
        hdr.Controls.Add(hSkill, 0, 0);
        hdr.Controls.Add(hInterval, 1, 0);
        hdr.Controls.Add(hStatus, 2, 0);
        hdr.Controls.Add(hHotkey, 3, 0);
        UiFactory.Add(stack, hdr);
        _refreshAllLabels += () =>
        {
            hSkill.Text = _loc.T("skill");
            hInterval.Text = _loc.T("interval");
            hStatus.Text = _loc.T("status");
            hHotkey.Text = _loc.T("hotkey");
        };

        var rowControls = new List<(Button Skill, TextBox Interval, Button Status, Button Hotkey)>();
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var row = _settings.Current.AutoCast.Rows[index];
            var skill = UiFactory.Btn("", BtnKind.Accent);
            void RefreshSkill() => skill.Text = $"{_loc.T("skill")} {index + 1}: {KeyNames.Display(row.SkillHotkey)}";
            RefreshSkill();
            skill.Click += (_, _) =>
            {
                skill.Text = _loc.T("pressAny");
                _hotkeys.BeginCapture(key =>
                {
                    row.SkillHotkey = key == Keys.Escape ? null : KeyNames.Format(key);
                    Persist(immediate: true);
                    RefreshSkill();
                });
            };
            _refreshAllLabels += RefreshSkill;

            var interval = UiFactory.Tb(row.IntervalMs.ToString());
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
                row.Enabled ? BtnKind.Primary : BtnKind.Danger);
            status.Click += (_, _) =>
            {
                _autoCast.Toggle(index);
                status.Text = row.Enabled ? _loc.T("on") : _loc.T("off");
                UiFactory.ApplyKind(status, row.Enabled ? BtnKind.Primary : BtnKind.Danger);
            };

            var hotkey = UiFactory.Btn("", BtnKind.Primary);
            BindLaunchButton(hotkey, () => row.ToggleHotkey, v => row.ToggleHotkey = v, "ac:" + index, "hotkey");

            rowControls.Add((skill, interval, status, hotkey));
            var line = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 1,
                Height = 38,
                BackColor = Color.Transparent
            };
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            skill.Dock = DockStyle.Fill;
            interval.Dock = DockStyle.Fill;
            status.Dock = DockStyle.Fill;
            hotkey.Dock = DockStyle.Fill;
            line.Controls.Add(skill, 0, 0);
            line.Controls.Add(interval, 1, 0);
            line.Controls.Add(status, 2, 0);
            line.Controls.Add(hotkey, 3, 0);
            UiFactory.Add(stack, line);
        }

        _refreshAutoCast = () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var row = _settings.Current.AutoCast.Rows[i];
                var c = rowControls[i];
                c.Status.Text = row.Enabled ? _loc.T("on") : _loc.T("off");
                UiFactory.ApplyKind(c.Status, row.Enabled ? BtnKind.Primary : BtnKind.Danger);
            }
        };
        _autoCast.SetUiRefresh(() => _refreshAutoCast?.Invoke());
        return scroll;
    }

    private Panel BuildTeleportPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var tp = _settings.Current.FastTeleport;

        var global = UiFactory.VStack();
        var map = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(map, () => tp.MapItem, v => tp.MapItem = v, "map", () => _settings.Current.Inventory.Grid.CellSize);
        var back = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(back, () => tp.BackButton, v => tp.BackButton = v, "back");
        var enter = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(enter, () => tp.EnterButton, v => tp.EnterButton = v, "enter");
        global.Controls.Add(UiFactory.EqualRow(map, back, enter));

        var track = UiFactory.Slider(1, 300, tp.ClickDelayMs);
        var val = UiFactory.Lbl(tp.ClickDelayMs.ToString());
        var delayLabel = UiFactory.Lbl(_loc.T("delay"), true);
        track.ValueChanged += (_, _) =>
        {
            tp.ClickDelayMs = track.Value;
            val.Text = track.Value.ToString();
        };
        track.DragEnded += (_, _) => Persist();
        var dRow = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = 36,
            BackColor = Color.Transparent
        };
        dRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        dRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        track.Dock = DockStyle.Fill;
        val.Dock = DockStyle.Fill;
        delayLabel.Dock = DockStyle.Fill;
        dRow.Controls.Add(track, 0, 0);
        dRow.Controls.Add(val, 1, 0);
        dRow.Controls.Add(delayLabel, 2, 0);
        global.Controls.Add(dRow);
        var globalSec = UiFactory.Section(_loc.T("global"), global, out var globalTitle);
        UiFactory.Add(stack, globalSec);
        _refreshAllLabels += () =>
        {
            globalTitle.Text = _loc.T("global");
            delayLabel.Text = _loc.T("delay");
        };

        var list = UiFactory.VStack();
        void Rebuild()
        {
            list.Controls.Clear();
            foreach (var loc in tp.Locations.ToList())
                list.Controls.Add(BuildTeleportBlock(loc));
        }

        var add = UiFactory.Btn(_loc.T("add.location"), BtnKind.Neutral);
        add.Click += (_, _) =>
        {
            tp.Locations.Add(new TeleportLocation { Name = $"Location {tp.Locations.Count + 1}" });
            Persist(immediate: true);
            Rebuild();
        };
        var del = UiFactory.Btn(_loc.T("delete.last"), BtnKind.Danger);
        del.Click += (_, _) =>
        {
            if (tp.Locations.Count > 0) tp.Locations.RemoveAt(tp.Locations.Count - 1);
            Persist(immediate: true);
            Rebuild();
        };
        UiFactory.Add(stack, UiFactory.EqualRow(add, del));
        UiFactory.Add(stack, list);
        Rebuild();
        _refreshTeleport = Rebuild;
        _refreshAllLabels += () =>
        {
            add.Text = _loc.T("add.location");
            del.Text = _loc.T("delete.last");
        };
        return scroll;
    }

    private Control BuildTeleportBlock(TeleportLocation loc)
    {
        var wrap = UiFactory.VStack();
        var body = UiFactory.VStack();
        var nameLabel = UiFactory.Lbl(_loc.T("locName"));
        var name = UiFactory.Tb(loc.Name);
        name.TextChanged += (_, _) => loc.Name = name.Text;
        name.Leave += (_, _) => Persist();

        var region = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(region, () => loc.Region, v => loc.Region = v, "region");
        var location = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(location, () => loc.Location, v => loc.Location = v, "location");
        var difficulty = UiFactory.Btn("", BtnKind.Warn);
        BindCoordButton(difficulty, () => loc.Difficulty, v => loc.Difficulty = v, "difficulty");

        body.Controls.Add(UiFactory.FieldRow(nameLabel, name, 150));
        body.Controls.Add(UiFactory.EqualRow(region, location, difficulty));

        var header = UiFactory.Btn("▾ " + loc.Name, BtnKind.Neutral, 30);
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.Click += (_, _) =>
        {
            body.Visible = !body.Visible;
            header.Text = (body.Visible ? "▾ " : "▸ ") + loc.Name;
        };
        name.TextChanged += (_, _) => header.Text = (body.Visible ? "▾ " : "▸ ") + loc.Name;

        var hotkey = UiFactory.Btn("", BtnKind.Primary, 30);
        BindLaunchButton(hotkey, () => loc.Hotkey, v => loc.Hotkey = v, "tp:" + loc.Id, "hotkey");
        wrap.Controls.Add(UiFactory.EqualRow(header, hotkey));
        wrap.Controls.Add(body);
        return wrap;
    }

    private Panel BuildSettingsPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var s = _settings.Current;

        var langInner = UiFactory.VStack();
        var lang = UiFactory.Combo("Русский", "English");
        lang.SelectedIndex = s.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        lang.SelectedIndexChanged += (_, _) =>
        {
            s.Language = lang.SelectedIndex == 1 ? "en" : "ru";
            _loc.Language = s.Language;
            Persist(immediate: true);
            RelocalizeNav();
            _refreshAllLabels?.Invoke();
        };
        langInner.Controls.Add(lang);
        var langSec = UiFactory.Section(_loc.T("language"), langInner, out var langTitle);
        UiFactory.Add(stack, langSec);

        var resInner = UiFactory.VStack();
        var res = UiFactory.Combo("Auto / Custom", "1366x768", "1920x1080", "2560x1440", "3840x2160");
        var idx = res.Items.IndexOf(s.ResolutionPreset);
        res.SelectedIndex = idx >= 0 ? idx : 0;
        res.SelectedIndexChanged += (_, _) =>
        {
            s.ResolutionPreset = res.SelectedItem?.ToString() ?? "Auto / Custom";
            Persist();
        };
        resInner.Controls.Add(res);
        var resSec = UiFactory.Section(_loc.T("resolution"), resInner, out var resTitle);
        UiFactory.Add(stack, resSec);

        var profileInner = UiFactory.VStack();
        var exportBtn = UiFactory.Btn(_loc.T("export"), BtnKind.Warn);
        var importBtn = UiFactory.Btn(_loc.T("import"), BtnKind.Warn);
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
            RebuildUi(_activePage);
            RegisterAllHotkeys();
            _autoClicker.ApplySettings();
            _autoCast.ApplySettings();
            UpdateGridOverlay();
        };
        profileInner.Controls.Add(UiFactory.EqualRow(exportBtn, importBtn));
        var profileSec = UiFactory.Section(_loc.T("profile"), profileInner, out var profileTitle);
        UiFactory.Add(stack, profileSec);

        var hotInner = UiFactory.VStack();
        var hide = UiFactory.Btn("", BtnKind.Warn);
        BindLaunchButton(hide, () => s.WindowHotkeys.HideShow, v => s.WindowHotkeys.HideShow = v, "win.hide", "hide");
        var exit = UiFactory.Btn("", BtnKind.Danger);
        BindLaunchButton(exit, () => s.WindowHotkeys.Exit, v => s.WindowHotkeys.Exit = v, "win.exit", "exit");
        var stop = UiFactory.Btn("", BtnKind.Warn);
        BindLaunchButton(stop, () => s.WindowHotkeys.EmergencyStop, v => s.WindowHotkeys.EmergencyStop = v, "win.stop", "stop");
        hotInner.Controls.Add(UiFactory.EqualRow(hide, exit, stop));
        var hotSec = UiFactory.Section(_loc.T("winHotkeys"), hotInner, out var hotTitle);
        UiFactory.Add(stack, hotSec);

        _refreshAllLabels += () =>
        {
            langTitle.Text = _loc.T("language");
            resTitle.Text = _loc.T("resolution");
            profileTitle.Text = _loc.T("profile");
            hotTitle.Text = _loc.T("winHotkeys");
            exportBtn.Text = _loc.T("export");
            importBtn.Text = _loc.T("import");
        };
        return scroll;
    }
}
