using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    private Panel BuildInventoryPage()
    {
        // Smelt muted: no sub-nav noise — just the sell setup surface.
        if (SmeltMuted)
            return BuildSellSubPage();

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
            UiFactory.ApplyKind(sellBtn, id == "sell" ? BtnKind.Primary : BtnKind.Neutral);
            UiFactory.ApplyKind(smeltBtn, id == "smelt" ? BtnKind.Primary : BtnKind.Neutral);
            if (id == "smelt")
            {
                smeltPage ??= BuildSmeltSubPage();
                smeltPage.Dock = DockStyle.Fill;
                if (!host.Controls.Contains(smeltPage)) host.Controls.Add(smeltPage);
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

    /// <summary>
    /// Daily flow first: Launch → What to sell → one-time Calibration → Advanced tuning.
    /// </summary>
    private Panel BuildSellSubPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var inv = _settings.Current.Inventory;

        UiFactory.Add(stack, BuildChecklistSection());
        UiFactory.Add(stack, BuildSellLaunchSection(inv));
        UiFactory.Add(stack, BuildSellTargetsSection());
        UiFactory.Add(stack, BuildSellCalibrationSection(inv));
        UiFactory.Add(stack, BuildAdvancedInventorySection());
        return scroll;
    }

    private Control BuildSellLaunchSection(InventorySettings inv)
    {
        var launch = UiFactory.VStack();
        var sellAll = new SettingRow(SettingRowKind.Hotkey, _loc.T("sell.all"), "");
        BindHotkeyRow(sellAll, () => inv.SellAllHotkey, v => inv.SellAllHotkey = v, "sell.all", "sell.all");
        launch.Controls.Add(sellAll);

        var section = UiFactory.Section(_loc.T("section.launch"), launch, out var title);
        _refreshAllLabels += () =>
        {
            title.Text = _loc.T("section.launch");
            sellAll.LabelText = _loc.T("sell.all");
        };
        return section;
    }

    private Control BuildSellTargetsSection()
    {
        var body = UiFactory.VStack();
        body.Controls.Add(BuildTabsSectionInner());
        body.Controls.Add(BuildRaritiesSectionInner());
        body.Controls.Add(BuildOtherItemsSection());

        var section = UiFactory.Section(_loc.T("section.sellTargets"), body, out var title);
        _refreshAllLabels += () => title.Text = _loc.T("section.sellTargets");
        return section;
    }

    private static Label SubHead(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 22,
        ForeColor = Theme.TextMuted,
        Font = Theme.UiFontSmall,
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Theme.Panel,
        Margin = new Padding(0, 4, 0, 4)
    };

    /// <summary>
    /// Bring the game forward, open the inventory with its hotkey, then match the grid.
    /// The key toggles the bag, so a miss is followed by one more press: that reopens it if the first press closed it.
    /// </summary>
    private bool TryPrepareInventory(InventorySettings inv, out InventoryCalibrator.Hit hit)
    {
        hit = default;
        var title = _settings.Current.GameWindowTitleContains;
        if (!string.IsNullOrWhiteSpace(title) && !GameWindowFocus.TryActivate(title, out _))
        {
            MessageBox.Show(_loc.T("calib.noGame"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var key = KeyNames.Parse(inv.InventoryHotkey);
        if (key == Keys.None)
        {
            MessageBox.Show(_loc.T("calib.noKey"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        Thread.Sleep(120);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _input.KeyPress(key);
            Thread.Sleep(420);
            if (InventoryCalibrator.TryMatch(out hit))
                return true;
        }

        MessageBox.Show(_loc.T("calib.fail"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private Control BuildSellCalibrationSection(InventorySettings inv)
    {
        var calib = UiFactory.VStack();
        var firstCell = new SettingRow(SettingRowKind.Coord, _loc.T("btn.firstCell"), "");
        BindCoordRow(firstCell, () => inv.FirstCell, v => inv.FirstCell = v, "btn.firstCell", () => inv.Grid.CellSize);
        var firstTab = new SettingRow(SettingRowKind.Coord, _loc.T("btn.firstTab"), "");
        BindCoordRow(firstTab, () => inv.FirstTab, v => inv.FirstTab = v, "btn.firstTab");
        var closeInv = new SettingRow(SettingRowKind.Coord, _loc.T("btn.closeInv"), "");
        BindCoordRow(closeInv, () => inv.CloseInventory, v => inv.CloseInventory = v, "btn.closeInv");
        var shop = new SettingRow(SettingRowKind.Coord, _loc.T("btn.shop"), "");
        BindCoordRow(shop, () => inv.ShopButton, v => inv.ShopButton = v, "btn.shop");
        var invKey = new SettingRow(SettingRowKind.Hotkey, _loc.T("btn.inventory"), "");
        BindHotkeyRow(invKey, () => inv.InventoryHotkey, v =>
        {
            inv.InventoryHotkey = v;
            _settings.Current.GearSwap.InventoryHotkey = v;
        }, "util.inv", "btn.inventory");
        var wisdom = new SettingRow(SettingRowKind.Coord, _loc.T("btn.wisdom"), "");
        BindCoordRow(wisdom, () => inv.WisdomTab, v => inv.WisdomTab = v, "btn.wisdom");
        var skills = new SettingRow(SettingRowKind.Hotkey, _loc.T("btn.skills"), "");
        BindHotkeyRow(skills, () => inv.SkillsWindowHotkey, v => inv.SkillsWindowHotkey = v, "util.skills", "btn.skills");

        var auto = UiFactory.Btn(_loc.T("calib.auto"), BtnKind.Primary);
        auto.Click += (_, _) =>
        {
            auto.Enabled = false;
            Cursor.Current = Cursors.WaitCursor;
            var gridWasVisible = _gridOverlay is { Visible: true };
            if (_gridOverlay != null)
                _gridOverlay.Visible = false;
            try
            {
                if (!TryPrepareInventory(inv, out var hit))
                    return;

                InventoryCalibrator.Apply(inv, hit);
                _suppressUiEvents = true;
                try { _refreshAllLabels?.Invoke(); }
                finally { _suppressUiEvents = false; }
                Persist(immediate: true, hotkeys: false, grid: true);
                SetStatusMessage(_loc.T("calib.ok"));
                gridWasVisible = false;
            }
            finally
            {
                if (gridWasVisible && _gridOverlay != null && inv.Grid.ShowGrid)
                    _gridOverlay.Visible = true;
                Cursor.Current = Cursors.Default;
                auto.Enabled = true;
            }
        };
        calib.Controls.Add(auto);
        calib.Controls.Add(firstCell);
        calib.Controls.Add(firstTab);
        calib.Controls.Add(closeInv);
        calib.Controls.Add(shop);
        calib.Controls.Add(invKey);
        calib.Controls.Add(wisdom);
        calib.Controls.Add(skills);

        var section = UiFactory.SectionCard(_loc.T("section.calib"), _loc.T("calib.hint"), calib, out var title, out var subtitle);
        _refreshAllLabels += () =>
        {
            title.Text = _loc.T("section.calib");
            if (subtitle != null) subtitle.Text = _loc.T("calib.hint");
            auto.Text = _loc.T("calib.auto");
            firstCell.LabelText = _loc.T("btn.firstCell");
            firstTab.LabelText = _loc.T("btn.firstTab");
            closeInv.LabelText = _loc.T("btn.closeInv");
            shop.LabelText = _loc.T("btn.shop");
            invKey.LabelText = _loc.T("btn.inventory");
            wisdom.LabelText = _loc.T("btn.wisdom");
            skills.LabelText = _loc.T("btn.skills");
        };
        return section;
    }

    private string LocalizedRarityName(RarityProfile r)
    {
        var key = $"rarity.{r.Id}";
        var name = _loc.T(key);
        if (string.Equals(name, key, StringComparison.Ordinal))
            name = r.Name;
        return QuestionMarkTemplates.HasTemplate(r.Id)
            ? name
            : $"{name} ({_loc.T("rarity.noTemplate")})";
    }

    private Control BuildAdvancedInventorySection()
    {
        var body = UiFactory.VStack();
        body.Visible = false;
        body.Controls.Add(BuildSellExtraSkillSection());
        body.Controls.Add(BuildGridSettingsSection());

        var wrap = BuildCollapsibleBlock(_loc.T("section.advanced"), body, body);
        _refreshAllLabels += () => SetCollapsibleTitle(wrap, _loc.T("section.advanced"));
        return wrap;
    }

    private Control BuildChecklistSection()
    {
        var inner = UiFactory.VStack();
        var summary = UiFactory.Lbl("", true);
        var itemsHost = UiFactory.VStack();
        inner.Controls.Add(summary);
        inner.Controls.Add(itemsHost);

        void Refresh()
        {
            var inv = _settings.Current.Inventory;
            var missing = new List<(string Key, Action Fix)>();
            if (inv.FirstCell == null)
                missing.Add(("checklist.firstCell", () =>
                {
                    PickCoord(v => inv.FirstCell = v, () => { }, Math.Max(16, inv.Grid.CellSize), true);
                    Refresh();
                }));
            if (inv.CloseInventory == null)
                missing.Add(("checklist.closeInv", () =>
                {
                    PickCoord(v => inv.CloseInventory = v, () => { }, Math.Max(16, inv.Grid.CrosshairSize), false);
                    Refresh();
                }));
            if (inv.FirstTab == null)
                missing.Add(("checklist.firstTab", () =>
                {
                    PickCoord(v => inv.FirstTab = v, () => { }, Math.Max(16, inv.Grid.CrosshairSize), false);
                    Refresh();
                }));
            if (inv.ShopButton == null)
                missing.Add(("checklist.shop", () =>
                {
                    PickCoord(v => inv.ShopButton = v, () => { }, Math.Max(16, inv.Grid.CrosshairSize), false);
                    Refresh();
                }));
            if (string.IsNullOrWhiteSpace(inv.SellAllHotkey))
                missing.Add(("checklist.sellHotkey", () => { }));
            if (!inv.SelectedTabs.Any(t => t))
                missing.Add(("checklist.tabs", () => { }));

            itemsHost.Controls.Clear();
            if (missing.Count == 0)
            {
                summary.Text = _loc.T("checklist.ready");
                summary.ForeColor = Theme.Success;
            }
            else
            {
                summary.Text = _loc.Tf("checklist.incomplete", missing.Count);
                summary.ForeColor = Theme.Accent;
                foreach (var (key, fix) in missing)
                {
                    var btn = UiFactory.Btn("• " + _loc.T(key), BtnKind.Neutral, 28);
                    btn.TextAlign = ContentAlignment.MiddleLeft;
                    var action = fix;
                    btn.Click += (_, _) => action();
                    itemsHost.Controls.Add(btn);
                }
            }
            UiFactory.Relayout(inner);
        }

        Refresh();
        _refreshChecklist = Refresh;
        _refreshAllLabels += Refresh;
        var section = UiFactory.Section(_loc.T("section.checklist"), inner, out var title);
        _refreshAllLabels += () => title.Text = _loc.T("section.checklist");
        return section;
    }

    private Control BuildSellExtraSkillSection()
    {
        var inv = _settings.Current.Inventory;
        var inner = UiFactory.VStack();

        ComboBox MakeCombo(Func<string?> get, Action<string?> set, string labelKey)
        {
            var label = UiFactory.Lbl(_loc.T(labelKey));
            var combo = UiFactory.Combo();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Width = 280;

            void Fill(string? selected)
            {
                combo.Items.Clear();
                combo.Items.Add(new ExtraSkillPick(_loc.T("none"), null));
                foreach (var p in _settings.Current.ExtraSkill.Profiles)
                    combo.Items.Add(new ExtraSkillPick(p.Name, p.Id));
                var idx = 0;
                for (var i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.Items[i] is ExtraSkillPick pick &&
                        string.Equals(pick.Id, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
                combo.SelectedIndex = idx;
            }

            Fill(get());
            combo.DropDown += (_, _) => Fill(combo.SelectedItem is ExtraSkillPick p ? p.Id : get());
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressUiEvents) return;
                if (combo.SelectedItem is not ExtraSkillPick pick) return;
                set(pick.Id);
                Persist();
            };

            inner.Controls.Add(UiFactory.FieldRow(label, combo, 160));
            _refreshAllLabels += () =>
            {
                label.Text = _loc.T(labelKey);
                if (combo.Items.Count > 0 && combo.Items[0] is ExtraSkillPick none)
                    none.Name = _loc.T("none");
            };
            return combo;
        }

        MakeCombo(() => inv.SellExtraSkillBeforeId, v => inv.SellExtraSkillBeforeId = v, "sell.extraBefore");
        MakeCombo(() => inv.SellExtraSkillAfterId, v => inv.SellExtraSkillAfterId = v, "sell.extraAfter");

        var section = UiFactory.Section(_loc.T("section.sellExtra"), inner, out var title);
        _refreshAllLabels += () => title.Text = _loc.T("section.sellExtra");
        return section;
    }

    private sealed class ExtraSkillPick(string name, string? id)
    {
        public string Name { get; set; } = name;
        public string? Id { get; } = id;
        public override string ToString() => Name;
    }

    private Control BuildGridSettingsSection()
    {
        var g = _settings.Current.Inventory.Grid;
        var body = UiFactory.VStack();

        var show = UiFactory.Check(_loc.T("grid.show"), g.ShowGrid);
        show.CheckedChanged += (_, _) =>
        {
            if (_suppressUiEvents) return;
            g.ShowGrid = show.Checked;
            Persist(immediate: true, hotkeys: false, grid: true);
        };
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
            num.ValueChanged += (_, _) =>
            {
                if (_suppressUiEvents) return;
                set((int)num.Value);
                Persist(hotkeys: false, grid: true);
            };
            body.Controls.Add(UiFactory.FieldRow(label, num, 220));
            _refreshAllLabels += () =>
            {
                label.Text = _loc.T(key);
                var v = Math.Clamp(get(), min, max);
                if ((int)num.Value == v) return;
                var was = _suppressUiEvents;
                _suppressUiEvents = true;
                try { num.Value = v; }
                finally { _suppressUiEvents = was; }
            };
        }

        NumRow("grid.cell", () => g.CellSize, v => g.CellSize = v, 16, 220);
        NumRow("grid.tab", () => g.TabMarkerSize, v => g.TabMarkerSize = v, 8, 96);
        NumRow("grid.gapx", () => g.GapX, v => g.GapX = v, 0, 40);
        NumRow("grid.gapy", () => g.GapY, v => g.GapY = v, 0, 40);
        NumRow("grid.tabx", () => g.TabOffsetX, v => g.TabOffsetX = v, 10, 200);
        NumRow("grid.cross", () => g.CrosshairSize, v => g.CrosshairSize = v, 8, 80);
        NumRow("grid.scan", () => g.ScanLevel, v => g.ScanLevel = v, 1, 2);
        NumRow("grid.sellDelay", () => g.SellClickDelayMs, v => g.SellClickDelayMs = v, 8, 500);
        NumRow("grid.tol", () => g.ColorTolerance, v => g.ColorTolerance = v, 0, 80);

        var section = UiFactory.Section(_loc.T("section.grid"), body, out var title);
        _refreshAllLabels += () =>
        {
            show.Text = _loc.T("grid.show");
            fast.Text = _loc.T("grid.fast");
            title.Text = _loc.T("section.grid");
            if (show.Checked == g.ShowGrid) return;
            var was = _suppressUiEvents;
            _suppressUiEvents = true;
            try { show.Checked = g.ShowGrid; }
            finally { _suppressUiEvents = was; }
        };
        return section;
    }

    private Control BuildTabsSectionInner()
    {
        var label = SubHead(_loc.T("section.tabs"));
        // LeftToRight FlowLayout — must not be a plain Panel (layout would stretch digits full-width).
        var strip = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Height = 36,
            BackColor = Theme.Panel,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty
        };
        var tabs = _settings.Current.Inventory.SelectedTabs;
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            var toggle = new TabDigitToggle(i + 1)
            {
                IsOn = tabs[i],
                Margin = new Padding(0, 2, 6, 0),
                Size = new Size(30, 30)
            };
            toggle.IsOnChanged += (_, _) =>
            {
                tabs[index] = toggle.IsOn;
                Persist(hotkeys: false);
                _refreshChecklist?.Invoke();
            };
            strip.Controls.Add(toggle);
        }

        var wrap = UiFactory.VStack();
        wrap.Controls.Add(label);
        wrap.Controls.Add(strip);
        _refreshAllLabels += () => label.Text = _loc.T("section.tabs");
        return wrap;
    }

    private Control BuildRaritiesSectionInner()
    {
        var host = UiFactory.VStack();
        var title = SubHead(_loc.T("section.rarities"));
        host.Controls.Add(title);

        var list = UiFactory.VStack();
        host.Controls.Add(list);
        var rows = new List<RarityPickRow>();

        void Rebuild()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            rows.Clear();
            foreach (var rarity in _settings.Current.Inventory.Rarities)
            {
                var r = rarity;
                var template = QuestionMarkTemplates.Find(r.Id);
                if (template == null && !r.Enabled)
                    r.Enabled = false;
                var row = new RarityPickRow(template, () => LocalizedRarityName(r), r.Enabled);
                row.IsOnChanged += (_, _) =>
                {
                    if (template == null) return;
                    r.Enabled = row.IsOn;
                    Persist(grid: true);
                };
                rows.Add(row);
                list.Controls.Add(row);
            }
            list.ResumeLayout(false);
            UiFactory.Relayout(host);
        }

        Rebuild();
        _refreshInventory = Rebuild;
        _refreshAllLabels += () =>
        {
            title.Text = _loc.T("section.rarities");
            foreach (var row in rows)
                row.RefreshTitle();
        };
        return host;
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
                var block = UiFactory.VStack();
                var enabled = UiFactory.Check(it.Name, it.Enabled);
                enabled.CheckedChanged += (_, _) => { it.Enabled = enabled.Checked; Persist(grid: true); };
                var name = UiFactory.Tb(it.Name);
                name.TextChanged += (_, _) => { it.Name = name.Text; enabled.Text = name.Text; };
                name.Leave += (_, _) => Persist();
                var add = UiFactory.Btn(_loc.T("btn.addPoint"), BtnKind.Neutral);
                add.Click += (_, _) => { AddColorPoint(it.Points); Rebuild(); UiFactory.Relayout(list); };
                var clear = UiFactory.Btn(_loc.T("btn.clear"), BtnKind.Neutral);
                clear.Click += (_, _) =>
                {
                    if (it.Points.Count == 0) return;
                    if (!ConfirmAction("confirm.clear")) return;
                    it.Points.Clear();
                    Persist(grid: true);
                    Rebuild();
                    UiFactory.Relayout(list);
                };
                var removeItem = MakeDeleteButton(() =>
                {
                    _settings.Current.Inventory.OtherItems.Remove(it);
                    Persist(grid: true);
                    Rebuild();
                    UiFactory.Relayout(list);
                });
                block.Controls.Add(UiFactory.ActionRow(
                    (enabled, 120), (name, 160), (add, 0), (clear, 0), (removeItem, removeItem.Width)));

                for (var i = 0; i < it.Points.Count; i++)
                {
                    var index = i;
                    var point = it.Points[index];
                    var caption = UiFactory.Lbl(PointCaption(index, point));
                    caption.AutoSize = false;
                    caption.TextAlign = ContentAlignment.MiddleLeft;
                    var removePoint = MakeDeleteButton(() =>
                    {
                        if (index < 0 || index >= it.Points.Count) return;
                        it.Points.RemoveAt(index);
                        Persist(grid: true);
                        Rebuild();
                        UiFactory.Relayout(list);
                    });
                    _refreshAllLabels += () =>
                    {
                        if (!caption.IsDisposed)
                            caption.Text = PointCaption(index, point);
                    };
                    block.Controls.Add(UiFactory.ActionRow((caption, 0), (removePoint, removePoint.Width)));
                }

                _refreshAllLabels += () =>
                {
                    if (add.IsDisposed) return;
                    add.Text = _loc.T("btn.addPoint");
                    clear.Text = _loc.T("btn.clear");
                };
                list.Controls.Add(block);
            }
            list.ResumeLayout(false);
            UiFactory.Relayout(list);
        }

        var addItem = UiFactory.Btn(_loc.T("btn.addItem"), BtnKind.Neutral, 28);
        addItem.Click += (_, _) =>
        {
            _settings.Current.Inventory.OtherItems.Add(new CustomItemProfile { Name = "Item" });
            Persist();
            Rebuild();
        };

        var wrap = UiFactory.VStack();
        wrap.Controls.Add(addItem);
        wrap.Controls.Add(list);
        Rebuild();
        _refreshAllLabels += () => addItem.Text = _loc.T("btn.addItem");
        return wrap;
    }

    private string PointCaption(int index, RelativeColorPoint point) =>
        $"{_loc.Tf("point.n", index + 1)}  ({point.OffsetX}, {point.OffsetY})";

    private Panel BuildSmeltSubPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var inv = _settings.Current.Inventory;
        var smelter = new SettingRow(SettingRowKind.Coord, _loc.T("smelt.select"), "");
        BindCoordRow(smelter, () => inv.SmelterItem, v => inv.SmelterItem = v, "smelt.select", () => inv.Grid.CellSize);
        var button = new SettingRow(SettingRowKind.Coord, _loc.T("smelt.button"), "");
        BindCoordRow(button, () => inv.SmeltButton, v => inv.SmeltButton = v, "smelt.button");
        var start = new SettingRow(SettingRowKind.Hotkey, _loc.T("smelt.start"), "");
        BindHotkeyRow(start, () => inv.SmeltHotkey, v => inv.SmeltHotkey = v, "smelt", "smelt.start");
        UiFactory.Add(stack, smelter);
        UiFactory.Add(stack, button);
        UiFactory.Add(stack, start);
        var hint = UiFactory.Lbl(_loc.T("smelt.hint"), true);
        UiFactory.Add(stack, hint);
        _refreshAllLabels += () => hint.Text = _loc.T("smelt.hint");
        return scroll;
    }
}
