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

    /// <summary>Readiness first, then what to sell. Launch lives inside general settings.</summary>
    private Panel BuildSellSubPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var inv = _settings.Current.Inventory;

        UiFactory.Add(stack, BuildChecklistSection(inv));
        UiFactory.Add(stack, BuildSellTargetsSection());
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
        body.Controls.Add(BuildOtherItemsSection());

        var section = UiFactory.Section(_loc.T("section.sellTargets"), body, out var title);
        _refreshAllLabels += () => title.Text = _loc.T("section.sellTargets");
        return section;
    }

    private static Label SubHead(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = Theme.UiFont.Height + 8,
        ForeColor = Theme.Text,
        Font = Theme.UiFontValue,
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Theme.Panel,
        Margin = new Padding(0, 10, 0, 4)
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

    /// <summary>
    /// Inventory is open after a grid match. Close it, open the skills window, click «Знания», then match the bag.
    /// The bag is not on the other tabs. The skills key toggles, so one miss is retried.
    /// </summary>
    private bool TryPrepareSkills(InventorySettings inv, out InventoryCalibrator.Hit wisdom, out InventoryCalibrator.Hit shop)
    {
        wisdom = default;
        shop = default;
        var key = KeyNames.Parse(inv.SkillsWindowHotkey);
        if (key == Keys.None)
        {
            MessageBox.Show(_loc.T("calib.noSkillsKey"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var invKey = KeyNames.Parse(inv.InventoryHotkey);
        if (invKey != Keys.None)
        {
            _input.KeyPress(invKey);
            Thread.Sleep(280);
        }

        var opened = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _input.KeyPress(key);
            Thread.Sleep(520);
            if (InventoryCalibrator.TryMatchWisdom(out wisdom))
            {
                opened = true;
                break;
            }
        }

        if (!opened)
        {
            MessageBox.Show(_loc.T("calib.skillsFail"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        var tab = InventoryCalibrator.WisdomClick(wisdom);
        _input.LeftClick(tab.X, tab.Y, settleMs: 2);
        Thread.Sleep(450);
        if (InventoryCalibrator.TryMatchSkills(out shop))
            return true;

        _input.LeftClick(tab.X, tab.Y, settleMs: 2);
        Thread.Sleep(450);
        if (InventoryCalibrator.TryMatchSkills(out shop))
            return true;

        MessageBox.Show(_loc.T("calib.skillsFail"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        var wisdom = new SettingRow(SettingRowKind.Coord, _loc.T("btn.wisdom"), "");
        BindCoordRow(wisdom, () => inv.WisdomTab, v => inv.WisdomTab = v, "btn.wisdom");

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
                var skillsOk = TryPrepareSkills(inv, out var wisdomHit, out var shopHit);
                if (skillsOk)
                {
                    InventoryCalibrator.ApplyWisdom(inv, wisdomHit);
                    InventoryCalibrator.ApplyShop(inv, shopHit);
                }
                _suppressUiEvents = true;
                try { _refreshAllLabels?.Invoke(); }
                finally { _suppressUiEvents = false; }
                Persist(immediate: true, hotkeys: false, grid: true);
                SetStatusMessage(_loc.T(skillsOk ? "calib.ok" : "calib.okGrid"));
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
        var autoBody = UiFactory.VStack();
        autoBody.Controls.Add(auto);
        var autoSection = UiFactory.SectionCard(_loc.T("section.calibAuto"), _loc.T("calib.autoLead"), autoBody, out var autoTitle, out var autoLead);

        var manualBody = UiFactory.VStack();
        manualBody.Controls.Add(firstCell);
        manualBody.Controls.Add(firstTab);
        manualBody.Controls.Add(closeInv);
        manualBody.Controls.Add(shop);
        manualBody.Controls.Add(wisdom);
        var manualSection = UiFactory.SectionCard(_loc.T("section.calibManual"), _loc.T("calib.manualLead"), manualBody, out var manualTitle, out var manualLead);

        calib.Controls.Add(autoSection);
        calib.Controls.Add(manualSection);
        calib.Controls.Add(BuildGridSettingsSection());

        calib.Visible = false;
        var fold = BuildCollapsibleBlock(_loc.T("section.calib"), calib, calib);
        _refreshAllLabels += () =>
        {
            SetCollapsibleTitle(fold, _loc.T("section.calib"));
            autoTitle.Text = _loc.T("section.calibAuto");
            if (autoLead != null) autoLead.Text = _loc.T("calib.autoLead");
            manualTitle.Text = _loc.T("section.calibManual");
            if (manualLead != null) manualLead.Text = _loc.T("calib.manualLead");
            auto.Text = _loc.T("calib.auto");
            firstCell.LabelText = _loc.T("btn.firstCell");
            firstTab.LabelText = _loc.T("btn.firstTab");
            closeInv.LabelText = _loc.T("btn.closeInv");
            shop.LabelText = _loc.T("btn.shop");
            wisdom.LabelText = _loc.T("btn.wisdom");
        };
        return fold;
    }

    /// <summary>Window keys and what runs around a sell. Starts collapsed.</summary>
    private Control BuildGeneralSettingsFold(InventorySettings inv)
    {
        var body = UiFactory.VStack();
        var invKey = new SettingRow(SettingRowKind.Hotkey, _loc.T("btn.inventory"), "");
        BindHotkeyRow(invKey, () => inv.InventoryHotkey, v =>
        {
            inv.InventoryHotkey = v;
            _settings.Current.GearSwap.InventoryHotkey = v;
        }, "util.inv", "btn.inventory");
        var skills = new SettingRow(SettingRowKind.Hotkey, _loc.T("btn.skills"), "");
        BindHotkeyRow(skills, () => inv.SkillsWindowHotkey, v => inv.SkillsWindowHotkey = v, "util.skills", "btn.skills");
        var windows = UiFactory.VStack();
        windows.Controls.Add(invKey);
        windows.Controls.Add(skills);
        var windowsSection = UiFactory.Section(_loc.T("section.windows"), windows, out var windowsTitle);
        body.Controls.Add(BuildSellLaunchSection(inv));
        body.Controls.Add(windowsSection);
        body.Controls.Add(BuildSellExtraSkillSection());
        body.Visible = false;

        var fold = BuildCollapsibleBlock(_loc.T("section.general"), body, body);
        _refreshAllLabels += () =>
        {
            SetCollapsibleTitle(fold, _loc.T("section.general"));
            windowsTitle.Text = _loc.T("section.windows");
        };
        return fold;
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

    private static bool UnsetHotkey(string? stored) => KeyNames.Parse(stored) == Keys.None;

    private Control BuildChecklistSection(InventorySettings inv)
    {
        var inner = UiFactory.VStack();
        var summary = new Label
        {
            AutoSize = false,
            Height = Theme.UiFont.Height + 4,
            Font = Theme.UiFont,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 2)
        };
        var itemRows = new List<Control>();
        inner.Controls.Add(summary);
        inner.Controls.Add(BuildGeneralSettingsFold(inv));
        inner.Controls.Add(BuildSellCalibrationSection(inv));
        var section = UiFactory.Section(_loc.T("section.checklist"), inner, out var title);
        _refreshAllLabels += () => title.Text = _loc.T("section.checklist");

        void Refresh()
        {
            var current = _settings.Current.Inventory;
            var missing = new List<(string Key, Action? Fix)>();
            if (current.FirstCell == null)
                missing.Add(("checklist.firstCell", () =>
                {
                    PickCoord(v => current.FirstCell = v, () => { }, Math.Max(16, current.Grid.CellSize), true);
                    _refreshAllLabels?.Invoke();
                }));
            if (current.CloseInventory == null)
                missing.Add(("checklist.closeInv", () =>
                {
                    PickCoord(v => current.CloseInventory = v, () => { }, GridSettings.PickerSize, false);
                    _refreshAllLabels?.Invoke();
                }));
            if (current.FirstTab == null)
                missing.Add(("checklist.firstTab", () =>
                {
                    PickCoord(v => current.FirstTab = v, () => { }, GridSettings.PickerSize, false);
                    _refreshAllLabels?.Invoke();
                }));
            if (current.ShopButton == null)
                missing.Add(("checklist.shop", () =>
                {
                    PickCoord(v => current.ShopButton = v, () => { }, GridSettings.PickerSize, false);
                    _refreshAllLabels?.Invoke();
                }));
            if (current.WisdomTab == null)
                missing.Add(("btn.wisdom", () =>
                {
                    PickCoord(v => current.WisdomTab = v, () => { }, GridSettings.PickerSize, false);
                    _refreshAllLabels?.Invoke();
                }));
            if (UnsetHotkey(current.SellAllHotkey))
                missing.Add(("checklist.sellHotkey", null));
            if (UnsetHotkey(current.InventoryHotkey))
                missing.Add(("btn.inventory", null));
            if (UnsetHotkey(current.SkillsWindowHotkey))
                missing.Add(("btn.skills", null));
            if (!current.SelectedTabs.Any(t => t))
                missing.Add(("checklist.tabs", null));

            foreach (var row in itemRows)
            {
                inner.Controls.Remove(row);
                row.Dispose();
            }
            itemRows.Clear();

            var ready = missing.Count == 0;
            title.ForeColor = ready ? Theme.Text : Theme.Signal;
            if (ready)
            {
                summary.Text = _loc.T("checklist.ready");
                summary.ForeColor = Theme.Success;
            }
            else
            {
                summary.Text = _loc.T("checklist.incomplete");
                summary.ForeColor = Theme.Signal;
                var at = 1;
                foreach (var (key, fix) in missing)
                {
                    var canFix = fix != null;
                    var line = new Label
                    {
                        AutoSize = false,
                        Height = Theme.UiFont.Height + 4,
                        Font = Theme.UiFont,
                        ForeColor = Theme.Signal,
                        BackColor = Color.Transparent,
                        Text = "•  " + _loc.T(key),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Margin = new Padding(0, 0, 0, 2),
                        Cursor = canFix ? Cursors.Hand : Cursors.Default
                    };
                    if (canFix)
                    {
                        var action = fix!;
                        line.Click += (_, _) => action();
                    }
                    inner.Controls.Add(line);
                    inner.Controls.SetChildIndex(line, at++);
                    itemRows.Add(line);
                }
            }
            UiFactory.Relayout(inner);
        }

        Refresh();
        _refreshChecklist = Refresh;
        _refreshAllLabels += Refresh;
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
        body.Controls.Add(show);

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
            body.Controls.Add(UiFactory.FieldRow(label, num, 280));
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
        NumRow("grid.gapx", () => g.GapX, v => g.GapX = v, 0, 40);
        NumRow("grid.gapy", () => g.GapY, v => g.GapY = v, 0, 40);
        NumRow("grid.tabx", () => g.TabOffsetX, v => g.TabOffsetX = v, 10, 200);
        NumRow("grid.scan", () => g.ScanLevel, v => g.ScanLevel = v, 1, 2);
        var scanHint = new WrapLabel
        {
            Text = _loc.T("grid.scanHint"),
            ForeColor = Theme.TextMuted,
            Font = Theme.UiFontSmall,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(2, 0, 8, 0)
        };
        body.Controls.Add(scanHint);
        NumRow("grid.sellDelay", () => g.SellClickDelayMs, v => g.SellClickDelayMs = v, 8, 500);

        var section = UiFactory.Section(_loc.T("section.grid"), body, out var title);
        _refreshAllLabels += () =>
        {
            show.Text = _loc.T("grid.show");
            scanHint.Text = _loc.T("grid.scanHint");
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

    private Control BuildOtherItemsSection()
    {
        var title = SubHead(_loc.T("section.rarities"));
        var addItem = UiFactory.Btn(_loc.T("btn.addItem"), BtnKind.Neutral, 28);
        var selectAll = UiFactory.Btn(_loc.T("btn.itemsAll"), BtnKind.Neutral, 28);
        var groupStrip = new UiFactory.WrapGrid { Padding = new Padding(0, 2, 0, 2) };
        var grid = new UiFactory.WrapGrid();
        var chips = new List<GroupChip>();
        var tiles = new List<ItemTile>();
        var systemTiles = new List<SystemTile>();
        Control? addGroup = null;

        string GroupTitle(ItemGroup group) =>
            string.IsNullOrWhiteSpace(group.Name) ? _loc.T("group.default") : group.Name;

        InventorySettings Inventory() => _settings.Current.Inventory;

        bool IsSystem(string? id) => id == ItemGroup.SystemId;

        string ActiveGroupId()
        {
            var inv = Inventory();
            if (inv.ItemGroups.Count == 0)
                inv.ItemGroups.Add(new ItemGroup());
            if (IsSystem(inv.ActiveItemGroupId))
                return ItemGroup.SystemId;
            if (string.IsNullOrEmpty(inv.ActiveItemGroupId) || inv.ItemGroups.All(g => g.Id != inv.ActiveItemGroupId))
                inv.ActiveItemGroupId = inv.ItemGroups[0].Id;
            return inv.ActiveItemGroupId!;
        }

        List<CustomItemProfile> VisibleItems()
        {
            var id = ActiveGroupId();
            return Inventory().OtherItems.Where(i => i.GroupId == id).ToList();
        }

        List<RarityProfile> VisibleRarities() =>
            Inventory().Rarities.Where(r => QuestionMarkTemplates.HasTemplate(r.Id)).ToList();

        void SyncSelect()
        {
            var system = IsSystem(ActiveGroupId());
            addItem.Enabled = !system;
            _toolTips.SetToolTip(addItem, system ? _loc.T("group.noAdd") : "");
            if (system)
            {
                var list = VisibleRarities();
                var allOn = list.Count > 0 && list.All(r => r.Enabled);
                selectAll.Text = _loc.T(allOn ? "btn.itemsNone" : "btn.itemsAll");
                selectAll.Enabled = list.Count > 0;
                return;
            }

            var items = VisibleItems();
            var itemsOn = items.Count > 0 && items.All(i => i.Enabled);
            selectAll.Text = _loc.T(itemsOn ? "btn.itemsNone" : "btn.itemsAll");
            selectAll.Enabled = items.Count > 0;
        }

        void RebuildTiles()
        {
            grid.SuspendLayout();
            for (var i = grid.Controls.Count - 1; i >= 0; i--)
            {
                var old = grid.Controls[i];
                grid.Controls.RemoveAt(i);
                old.Dispose();
            }

            tiles.Clear();
            systemTiles.Clear();
            if (IsSystem(ActiveGroupId()))
            {
                foreach (var rarity in VisibleRarities())
                {
                    var r = rarity;
                    var tile = new SystemTile(r, () => LocalizedRarityName(r), () =>
                    {
                        if (!QuestionMarkTemplates.HasTemplate(r.Id)) return;
                        r.Enabled = !r.Enabled;
                        Persist(grid: true);
                        foreach (var live in systemTiles)
                        {
                            if (!live.IsDisposed) live.ApplyState();
                        }
                        SyncSelect();
                    });
                    _toolTips.SetToolTip(tile, _loc.T("item.systemHint"));
                    systemTiles.Add(tile);
                    grid.Controls.Add(tile);
                }
            }
            else
            {
            foreach (var item in VisibleItems())
            {
                var it = item;
                var tile = new ItemTile(
                    it,
                    _loc,
                    () =>
                    {
                        it.Enabled = !it.Enabled;
                        Persist(grid: true);
                        foreach (var live in tiles)
                        {
                            if (!live.IsDisposed) live.ApplyState();
                        }
                        SyncSelect();
                    },
                    () =>
                    {
                        if (!CaptureItemIcon(it)) return;
                        RefreshTiles();
                    },
                    () =>
                    {
                        if (!ConfirmAction("confirm.delete")) return;
                        Inventory().OtherItems.Remove(it);
                        Persist(grid: true);
                        RefreshTiles();
                    },
                    text => it.Name = text,
                    () => Persist());
                tile.ApplyLanguage(_loc, _toolTips);
                tiles.Add(tile);
                grid.Controls.Add(tile);
            }
            }

            grid.ResumeLayout(false);
            SyncSelect();
            UiFactory.Relayout(grid);
        }

        void RebuildGroups()
        {
            groupStrip.SuspendLayout();
            for (var i = groupStrip.Controls.Count - 1; i >= 0; i--)
            {
                var old = groupStrip.Controls[i];
                groupStrip.Controls.RemoveAt(i);
                old.Dispose();
            }

            chips.Clear();
            var inv = Inventory();
            var active = ActiveGroupId();
            var canDelete = inv.ItemGroups.Count > 1;

            var system = new GroupChip
            {
                GroupId = ItemGroup.SystemId,
                AllowDelete = false,
                AllowRename = false,
                AllowItemDrop = false,
                IsSelected = IsSystem(active)
            };
            system.SetTitle(_loc.T("group.system"));
            system.Chosen += (_, _) => Choose(ItemGroup.SystemId);
            _toolTips.SetToolTip(system, _loc.T("group.systemHint"));
            chips.Add(system);
            groupStrip.Controls.Add(system);

            foreach (var group in inv.ItemGroups)
            {
                var g = group;
                var chip = new GroupChip
                {
                    GroupId = g.Id,
                    AllowDelete = canDelete,
                    AllowRename = true,
                    AllowItemDrop = true,
                    IsSelected = g.Id == active
                };
                chip.SetTitle(GroupTitle(g));
                chip.Chosen += (_, _) => Choose(g.Id);
                chip.DeleteRequested += (_, _) => AskDeleteGroup(g);
                chip.ItemDropped += (_, itemId) => MoveItem(itemId, g);
                chip.Renamed += (_, name) =>
                {
                    g.Name = name;
                    Persist();
                    RefreshAll();
                };
                _toolTips.SetToolTip(chip, _loc.T("group.renameHint"));
                chips.Add(chip);
                groupStrip.Controls.Add(chip);
            }

            var side = Math.Max(28, Theme.UiFont.Height + 12);
            var plus = new MiniButton("+", BtnKind.Neutral) { Margin = new Padding(0, 0, 0, 6) };
            plus.SetSize(side, side);
            addGroup = plus;
            addGroup.Click += (_, _) => AddGroup();
            _toolTips.SetToolTip(addGroup, _loc.T("group.add"));
            groupStrip.Controls.Add(addGroup);
            groupStrip.ResumeLayout(false);
        }

        void Choose(string groupId)
        {
            if (Inventory().ActiveItemGroupId == groupId) return;
            Inventory().ActiveItemGroupId = groupId;
            Persist();
            foreach (var chip in chips)
            {
                if (!chip.IsDisposed) chip.IsSelected = chip.GroupId == groupId;
            }
            RebuildTiles();
        }

        void MoveItem(string itemId, ItemGroup target)
        {
            if (IsSystem(target.Id)) return;
            var item = Inventory().OtherItems.FirstOrDefault(i => i.Id == itemId);
            if (item == null || item.GroupId == target.Id) return;
            item.GroupId = target.Id;
            Inventory().ActiveItemGroupId = target.Id;
            Persist(grid: true);
            RefreshAll();
        }

        void AddGroup()
        {
            var inv = Inventory();
            var group = new ItemGroup { Name = _loc.Tf("group.new", inv.ItemGroups.Count + 1) };
            inv.ItemGroups.Add(group);
            inv.ActiveItemGroupId = group.Id;
            Persist();
            RefreshAll();
        }

        void AskDeleteGroup(ItemGroup group)
        {
            var inv = Inventory();
            if (inv.ItemGroups.Count < 2) return;
            var other = inv.ItemGroups.First(g => g.Id != group.Id);
            var message = _loc.Tf("group.delete", GroupTitle(group), GroupTitle(other));
            if (MessageBox.Show(message, _loc.T("notify"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            foreach (var item in inv.OtherItems)
            {
                if (item.GroupId == group.Id) item.GroupId = other.Id;
            }
            inv.ItemGroups.Remove(group);
            inv.ActiveItemGroupId = other.Id;
            Persist(grid: true);
            RefreshAll();
        }

        void RefreshTiles()
        {
            if (!grid.IsHandleCreated)
            {
                RebuildTiles();
                return;
            }

            grid.BeginInvoke(new Action(RebuildTiles));
        }

        void RefreshAll()
        {
            if (!groupStrip.IsHandleCreated)
            {
                RebuildGroups();
                RebuildTiles();
                return;
            }

            groupStrip.BeginInvoke(new Action(() =>
            {
                RebuildGroups();
                RebuildTiles();
            }));
        }

        addItem.Click += (_, _) =>
        {
            if (IsSystem(ActiveGroupId())) return;
            Inventory().OtherItems.Add(new CustomItemProfile { Name = "Item", GroupId = ActiveGroupId() });
            Persist();
            RebuildTiles();
        };
        selectAll.Click += (_, _) =>
        {
            if (IsSystem(ActiveGroupId()))
            {
                var rarities = VisibleRarities();
                if (rarities.Count == 0) return;
                var turnOn = rarities.Any(r => !r.Enabled);
                foreach (var rarity in rarities)
                    rarity.Enabled = turnOn;
                Persist(grid: true);
                foreach (var tile in systemTiles)
                {
                    if (!tile.IsDisposed) tile.ApplyState();
                }
                SyncSelect();
                return;
            }

            var list = VisibleItems();
            if (list.Count == 0) return;
            var itemsOn = list.Any(i => !i.Enabled);
            foreach (var it in list)
                it.Enabled = itemsOn;
            Persist(grid: true);
            foreach (var tile in tiles)
            {
                if (!tile.IsDisposed) tile.ApplyState();
            }
            SyncSelect();
        };

        var wrap = UiFactory.VStack();
        wrap.Controls.Add(title);
        wrap.Controls.Add(groupStrip);
        wrap.Controls.Add(UiFactory.ActionRow((addItem, 0), (selectAll, 0)));
        wrap.Controls.Add(grid);
        RebuildGroups();
        RebuildTiles();
        _refreshInventory = RebuildTiles;
        _refreshAllLabels += () =>
        {
            title.Text = _loc.T("section.rarities");
            if (!addItem.IsDisposed) addItem.Text = _loc.T("btn.addItem");
            if (addGroup is { IsDisposed: false })
                _toolTips.SetToolTip(addGroup, _loc.T("group.add"));
            foreach (var chip in chips)
            {
                if (chip.IsDisposed) continue;
                if (chip.GroupId == ItemGroup.SystemId)
                {
                    chip.SetTitle(_loc.T("group.system"));
                    _toolTips.SetToolTip(chip, _loc.T("group.systemHint"));
                    continue;
                }
                var group = Inventory().ItemGroups.FirstOrDefault(g => g.Id == chip.GroupId);
                if (group != null) chip.SetTitle(GroupTitle(group));
                _toolTips.SetToolTip(chip, _loc.T("group.renameHint"));
            }
            SyncSelect();
            foreach (var tile in tiles)
            {
                if (!tile.IsDisposed) tile.ApplyLanguage(_loc, _toolTips);
            }
            foreach (var tile in systemTiles)
            {
                if (tile.IsDisposed) continue;
                tile.ApplyLanguage();
                _toolTips.SetToolTip(tile, _loc.T("item.systemHint"));
            }
            UiFactory.Relayout(groupStrip);
        };
        return wrap;
    }

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
