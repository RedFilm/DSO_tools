using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    private Panel BuildAutoClickerPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var s = _settings.Current.AutoClicker;
        var labelWidth = WidestClickerLabel("click.mode", "click.interval");
        var modeWidth = WidestClickerLabel("mode.hold", "mode.toggle") + 28;

        Control BuildSide(string titleKey, Func<string> getMode, Action<string> setMode,
            Func<string?> getKey, Action<string?> setKey, string keyId,
            Func<int> getDelay, Action<int> setDelay)
        {
            var body = UiFactory.VStack();

            var modeLabel = new Label
            {
                Text = _loc.T("click.mode"),
                ForeColor = Theme.Text,
                Font = Theme.UiFont,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
            var mode = UiFactory.Combo(_loc.T("mode.hold"), _loc.T("mode.toggle"));
            mode.SelectedIndex = getMode() == "toggle" ? 1 : 0;
            mode.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressUiEvents) return;
                setMode(mode.SelectedIndex == 1 ? "toggle" : "hold");
                Persist(immediate: true);
            };
            body.Controls.Add(ModeRow(modeLabel, mode, labelWidth, modeWidth));

            var keyRow = new SettingRow(SettingRowKind.Hotkey, _loc.T("click.key"), "");
            BindHotkeyRow(keyRow, getKey, setKey, keyId, "click.key");
            body.Controls.Add(keyRow);

            var delayRow = UiFactory.DelaySliderRow(
                _loc.T("click.interval"), 1, 200, getDelay(),
                v => setDelay(v),
                () => Persist(),
                labelWidth,
                editableValue: true);
            var intervalLabel = delayRow.Controls.Count > 0 ? delayRow.Controls[0] as Label : null;
            body.Controls.Add(delayRow);

            var section = UiFactory.Section(_loc.T(titleKey), body, out var title);
            _refreshAllLabels += () =>
            {
                var sel = mode.SelectedIndex;
                mode.Items.Clear();
                mode.Items.Add(_loc.T("mode.hold"));
                mode.Items.Add(_loc.T("mode.toggle"));
                mode.SelectedIndex = sel >= 0 ? sel : 0;
                modeLabel.Text = _loc.T("click.mode");
                if (intervalLabel != null) intervalLabel.Text = _loc.T("click.interval");
                title.Text = _loc.T(titleKey);
            };
            return section;
        }

        UiFactory.Add(stack, BuildSide("click.left", () => s.LeftMode, v => s.LeftMode = v,
            () => s.LeftHotkey, v => s.LeftHotkey = v, "aclick.left",
            () => s.LeftDelayMs, v => s.LeftDelayMs = v));
        UiFactory.Add(stack, BuildSide("click.right", () => s.RightMode, v => s.RightMode = v,
            () => s.RightHotkey, v => s.RightHotkey = v, "aclick.right",
            () => s.RightDelayMs, v => s.RightDelayMs = v));
        return scroll;
    }

    /// <summary>Label and a combo only as wide as its longest option; leftover space stays empty.</summary>
    private static TableLayoutPanel ModeRow(Label label, Control combo, int labelWidth, int comboWidth)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = UiFactory.RowH + 4,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = Color.Transparent,
            Padding = Padding.Empty
        };
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFactory.RowH + 4));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, comboWidth));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(2, 0, 8, 0);
        combo.Dock = DockStyle.Fill;
        combo.Margin = new Padding(2, 4, 2, 4);
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(combo, 1, 0);
        return row;
    }

    /// <summary>Column wide enough for the longer of the RU/EN strings at the current DPI.</summary>
    private int WidestClickerLabel(params string[] keys)
    {
        var saved = _loc.Language;
        try
        {
            var width = 0;
            foreach (var lang in new[] { "ru", "en" })
            {
                _loc.Language = lang;
                foreach (var key in keys)
                    width = Math.Max(width, TextRenderer.MeasureText(_loc.T(key), Theme.UiFont).Width);
            }

            // Delay row keeps an 8px gap between the caption and the track.
            return width + 16;
        }
        finally
        {
            _loc.Language = saved;
        }
    }

    private Panel BuildFastMountPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var fm = _settings.Current.FastMount;

        var launch = UiFactory.VStack();
        var fast = new SettingRow(SettingRowKind.Hotkey, _loc.T("fastmount"), "");
        BindHotkeyRow(fast, () => fm.FastMountHotkey, v => fm.FastMountHotkey = v, "fastmount", "fastmount");
        var jumpKey = new SettingRow(SettingRowKind.Hotkey, _loc.T("jumpmount"), "");
        BindHotkeyRow(jumpKey, () => fm.JumpMountHotkey, v => fm.JumpMountHotkey = v, "jumpmount", "jumpmount");
        launch.Controls.Add(fast);
        launch.Controls.Add(jumpKey);
        var launchSec = UiFactory.Section(_loc.T("section.launch"), launch, out var launchTitle);
        UiFactory.Add(stack, launchSec);

        var parms = UiFactory.VStack();
        var cape = new SettingRow(SettingRowKind.Cell, _loc.T("cape"), "");
        void RefreshCape() => cape.ValueText = fm.CapeCell == null ? _loc.T("not.set") : fm.CapeCell.ToString();
        RefreshCape();
        cape.Activated += (_, _) =>
        {
            var size = _settings.Current.Inventory.Grid.CellSize;
            using var overlay = new OverlayForm(size, rectangle: true, hintText: _loc.T("overlay.hint"));
            Hide();
            overlay.ShowDialog();
            Show();
            if (overlay.Result == null)
                fm.CapeCell = null;
            else
                fm.CapeCell = InventoryLayout.PointToCell(_settings.Current.Inventory, overlay.Result.Value)
                              ?? GuessCellFromPoint(overlay.Result.Value);
            Persist(immediate: true, grid: true);
            RefreshCape();
        };
        _refreshAllLabels += () => { cape.LabelText = _loc.T("cape"); RefreshCape(); };

        var mount = new SettingRow(SettingRowKind.Hotkey, _loc.T("mount"), "");
        BindHotkeyRow(mount, () => fm.MountHotkey, v => fm.MountHotkey = v, "util.mount", "mount");

        var capeTab = new DarkSelect(Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray())
        {
            Caption = _loc.T("tab")
        };
        capeTab.SelectedIndex = Math.Clamp(fm.CapeTab - 1, 0, 8);
        capeTab.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUiEvents) return;
            fm.CapeTab = capeTab.SelectedIndex + 1;
            Persist();
        };
        cape.SetTrailing(capeTab, capeTab.MeasurePreferredWidth());
        parms.Controls.Add(cape);
        parms.Controls.Add(mount);

        var delay1 = UiFactory.DelaySliderRow(_loc.T("delay"), 1, 200, fm.ClickDelayMs,
            v => fm.ClickDelayMs = v, () => Persist());
        var delay2 = UiFactory.DelaySliderRow(_loc.T("mountSwapDelay"), 10, 5000, fm.MountSwapDelayMs,
            v => fm.MountSwapDelayMs = v, () => Persist());
        parms.Controls.Add(delay1);
        parms.Controls.Add(delay2);

        var mountCell = new SettingRow(SettingRowKind.Cell, _loc.T("mountCell"), "");
        void RefreshMountCell() => mountCell.ValueText = fm.MountCell == null ? _loc.T("not.set") : fm.MountCell.ToString();
        RefreshMountCell();
        mountCell.Activated += (_, _) =>
        {
            var size = _settings.Current.Inventory.Grid.CellSize;
            using var overlay = new OverlayForm(size, rectangle: true, hintText: _loc.T("overlay.hint"));
            Hide();
            overlay.ShowDialog();
            Show();
            if (overlay.Result == null)
                fm.MountCell = null;
            else
                fm.MountCell = InventoryLayout.PointToCell(_settings.Current.Inventory, overlay.Result.Value)
                               ?? GuessCellFromPoint(overlay.Result.Value);
            Persist(immediate: true, grid: true);
            RefreshMountCell();
        };
        var target = new SettingRow(SettingRowKind.Hotkey, _loc.T("targetPanel"), "");
        BindHotkeyRow(target, () => fm.JumpTargetPanelHotkey, v => fm.JumpTargetPanelHotkey = v, "util.jumpTarget", "targetPanel");
        var skill = new SettingRow(SettingRowKind.Hotkey, _loc.T("skillSlot"), "");
        BindHotkeyRow(skill, () => fm.JumpSkillHotkey, v => fm.JumpSkillHotkey = v, "util.jumpSkill", "skillSlot");

        var mountTab = new DarkSelect(Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray())
        {
            Caption = _loc.T("tab")
        };
        mountTab.SelectedIndex = Math.Clamp(fm.MountTab - 1, 0, 8);
        mountTab.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUiEvents) return;
            fm.MountTab = mountTab.SelectedIndex + 1;
            Persist();
        };
        mountCell.SetTrailing(mountTab, mountTab.MeasurePreferredWidth());
        parms.Controls.Add(mountCell);
        parms.Controls.Add(target);
        parms.Controls.Add(skill);

        var parmsSec = UiFactory.Section(_loc.T("section.params"), parms, out var parmsTitle);
        UiFactory.Add(stack, parmsSec);
        _refreshAllLabels += () =>
        {
            launchTitle.Text = _loc.T("section.launch");
            parmsTitle.Text = _loc.T("section.params");
            capeTab.Caption = _loc.T("tab");
            mountTab.Caption = _loc.T("tab");
            cape.SetTrailing(capeTab, capeTab.MeasurePreferredWidth());
            mountCell.SetTrailing(mountTab, mountTab.MeasurePreferredWidth());
            mountCell.LabelText = _loc.T("mountCell");
            RefreshMountCell();
            if (delay1.Controls[0] is Label l1) l1.Text = _loc.T("delay");
            if (delay2.Controls[0] is Label l2) l2.Text = _loc.T("mountSwapDelay");
        };
        return scroll;
    }

    private Panel BuildExtraSkillPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var es = _settings.Current.ExtraSkill;

        var launch = UiFactory.VStack();
        var main = new SettingRow(SettingRowKind.Hotkey, _loc.T("mainPanel"), "");
        BindHotkeyRow(main, () => es.MainPanelHotkey, SetMainPanelHotkey, "util.main", "mainPanel");
        launch.Controls.Add(main);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("section.settings"), launch, out var launchTitle));
        _refreshAllLabels += () => launchTitle.Text = _loc.T("section.settings");

        var listHost = new Panel { BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };

        void Rebuild()
        {
            listHost.Controls.Clear();
            foreach (var profile in es.Profiles.ToList())
                listHost.Controls.Add(BuildExtraSkillBlock(profile, scroll, () =>
            {
                es.Profiles.Remove(profile);
                Persist(immediate: true);
                Rebuild();
                _refreshBindingMap?.Invoke();
            }));
            UiFactory.Relayout(scroll);
        }

        var add = UiFactory.Btn(_loc.T("add.extraskill"), BtnKind.Neutral);
        add.Click += (_, _) =>
        {
            es.Profiles.Add(new ExtraSkillProfile { Name = $"Extra Skill {es.Profiles.Count + 1}" });
            Persist(immediate: true);
            Rebuild();
            _refreshBindingMap?.Invoke();
        };

        var skillsInner = UiFactory.VStack();
        skillsInner.Controls.Add(add);
        skillsInner.Controls.Add(listHost);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("skills"), skillsInner, out var skillsTitle));
        _refreshAllLabels += () =>
        {
            skillsTitle.Text = _loc.T("skills");
            add.Text = _loc.T("add.extraskill");
        };
        Rebuild();
        _refreshExtraSkill = Rebuild;
        return scroll;
    }

    private Control BuildExtraSkillBlock(ExtraSkillProfile profile, Control layoutHost, Action remove)
    {
        var nameLabel = UiFactory.Lbl(_loc.T("skillName"));
        nameLabel.AutoSize = false;
        nameLabel.TextAlign = ContentAlignment.MiddleLeft;
        // Setting-row captions start at x=42 (after the key icon). Keep this label on that column.
        nameLabel.Padding = new Padding(42, 0, 8, 0);
        var name = UiFactory.Tb(profile.Name);
        name.AutoSize = false;
        name.Leave += (_, _) => Persist();
        var target = new SettingRow(SettingRowKind.Hotkey, _loc.T("targetPanel"), "");
        BindHotkeyRow(target, () => profile.TargetPanelHotkey, v => profile.TargetPanelHotkey = v, "es.t:" + profile.Id, "targetPanel");
        var skill = new SettingRow(SettingRowKind.Hotkey, _loc.T("skillButton"), "");
        BindHotkeyRow(skill, () => profile.SkillSlotHotkey, v => profile.SkillSlotHotkey = v, "es.s:" + profile.Id, "skillButton");

        var body = new Panel
        {
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Visible = false
        };
        var nameCol = 42 + WidestClickerLabel("skillName");
        var nameRow = UiFactory.FieldRow(nameLabel, name, nameCol);
        nameRow.Height = 38;
        if (nameRow.RowStyles.Count > 0)
            nameRow.RowStyles[0].Height = 38;
        nameLabel.Margin = Padding.Empty;
        name.Margin = Padding.Empty;
        body.Controls.Add(nameRow);
        body.Controls.Add(target);
        body.Controls.Add(skill);

        var hotkey = UiFactory.Btn("", BtnKind.Primary, 30);
        BindLaunchButton(hotkey, () => profile.Hotkey, v => profile.Hotkey = v, "es:" + profile.Id, "hotkey");
        var wrap = BuildCollapsibleBlock(profile.Name, body, layoutHost, hotkey, MakeDeleteButton(remove));
        // start collapsed
        body.Visible = false;
        SetCollapsibleTitle(wrap, profile.Name);
        name.TextChanged += (_, _) =>
        {
            profile.Name = name.Text;
            SetCollapsibleTitle(wrap, profile.Name);
        };
        _refreshAllLabels += () => nameLabel.Text = _loc.T("skillName");
        return wrap;
    }

    private Panel BuildGearSwapPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var gs = _settings.Current.GearSwap;

        var invKey = new SettingRow(SettingRowKind.Hotkey, _loc.T("btn.inventory"), "");
        BindHotkeyRow(invKey, () => gs.InventoryHotkey, v =>
        {
            gs.InventoryHotkey = v;
            _settings.Current.Inventory.InventoryHotkey = v;
        }, "util.inv", "btn.inventory");
        var main = new SettingRow(SettingRowKind.Hotkey, _loc.T("mainPanel"), "");
        BindHotkeyRow(main, () => gs.MainPanelHotkey, SetMainPanelHotkey, "util.main", "mainPanel");
        var add = UiFactory.Btn(_loc.T("add.gearswap"), BtnKind.Neutral);
        var top = UiFactory.VStack();
        top.Controls.Add(invKey);
        top.Controls.Add(main);
        top.Controls.Add(add);
        UiFactory.Add(stack, top);

        var delayRow = UiFactory.DelaySliderRow(_loc.T("delay"), 1, 200, gs.ClickDelayMs,
            v => gs.ClickDelayMs = v, () => Persist(), labelWidth: 180);
        UiFactory.Add(stack, delayRow);

        var list = UiFactory.VStack();
        void Rebuild()
        {
            list.Controls.Clear();
            foreach (var p in gs.Profiles.ToList())
                list.Controls.Add(BuildGearSwapBlock(p, scroll, () =>
                {
                    gs.Profiles.Remove(p);
                    Persist(immediate: true);
                    Rebuild();
                    _refreshBindingMap?.Invoke();
                }));
            UiFactory.Relayout(scroll);
        }
        add.Click += (_, _) =>
        {
            gs.Profiles.Add(new GearSwapProfile { Name = $"Gear Swap {gs.Profiles.Count + 1}" });
            Persist(immediate: true);
            Rebuild();
        };
        Rebuild();
        _refreshGearSwap = Rebuild;
        UiFactory.Add(stack, list);
        _refreshAllLabels += () =>
        {
            if (delayRow.Controls[0] is Label l) l.Text = _loc.T("delay");
            add.Text = _loc.T("add.gearswap");
        };
        return scroll;
    }

    private Control BuildGearSwapBlock(GearSwapProfile profile, Control layoutHost, Action remove)
    {
        var body = UiFactory.VStack();
        body.Visible = false;
        var nameLabel = UiFactory.Lbl(_loc.T("name"));
        var name = UiFactory.Tb(profile.Name);
        name.Leave += (_, _) => Persist();
        var target = new SettingRow(SettingRowKind.Hotkey, _loc.T("targetPanel"), "");
        BindHotkeyRow(target, () => profile.TargetPanelHotkey, v => profile.TargetPanelHotkey = v, "gs.t:" + profile.Id, "targetPanel");
        var skill = new SettingRow(SettingRowKind.Hotkey, _loc.T("skillSlot"), "");
        BindHotkeyRow(skill, () => profile.SkillSlotHotkey, v => profile.SkillSlotHotkey = v, "gs.s:" + profile.Id, "skillSlot");
        var tab = UiFactory.Combo(Enumerable.Range(1, 9).Select(i => i.ToString()).ToArray());
        tab.SelectedIndex = Math.Clamp(profile.InventoryTab - 1, 0, 8);
        tab.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUiEvents) return;
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
                var cell = profile.Items[index];
                var btn = UiFactory.Btn($"{_loc.T("item")} {index + 1}: {cell}", BtnKind.Neutral);
                btn.Click += (_, _) =>
                {
                    var size = _settings.Current.Inventory.Grid.CellSize;
                    using var overlay = new OverlayForm(size, true, _loc.T("overlay.hint"));
                    Hide();
                    overlay.ShowDialog();
                    Show();
                    if (overlay.Result == null) return;
                    profile.Items[index] = InventoryLayout.PointToCell(_settings.Current.Inventory, overlay.Result.Value)
                                          ?? GuessCellFromPoint(overlay.Result.Value);
                    Persist(immediate: true);
                    RebuildItems();
                    RelayoutProfileHost(layoutHost, body);
                };
                var del = MakeDeleteButton(() =>
                {
                    if (index < 0 || index >= profile.Items.Count) return;
                    profile.Items.RemoveAt(index);
                    Persist(immediate: true);
                    RebuildItems();
                    RelayoutProfileHost(layoutHost, body);
                });
                itemsHost.Controls.Add(UiFactory.ActionRow((btn, 0), (del, del.Width)));
            }
            RelayoutProfileHost(layoutHost, body);
        }

        var addItem = UiFactory.Btn(_loc.T("add.item"), BtnKind.Neutral);
        addItem.Click += (_, _) =>
        {
            profile.Items.Add(new CellRef { Row = 1, Col = 1 });
            Persist();
            RebuildItems();
        };

        body.Controls.Add(UiFactory.FieldRow(nameLabel, name, 90));
        body.Controls.Add(target);
        body.Controls.Add(skill);
        body.Controls.Add(UiFactory.FieldRow(tabLabel, tab, 90));
        body.Controls.Add(addItem);
        body.Controls.Add(itemsHost);
        RebuildItems();

        var hotkey = UiFactory.Btn("", BtnKind.Primary, 30);
        BindLaunchButton(hotkey, () => profile.Hotkey, v => profile.Hotkey = v, "gs:" + profile.Id, "hotkey");
        var wrap = BuildCollapsibleBlock(profile.Name, body, layoutHost, hotkey, MakeDeleteButton(remove));
        name.TextChanged += (_, _) =>
        {
            profile.Name = name.Text;
            SetCollapsibleTitle(wrap, profile.Name);
        };
        _refreshAllLabels += () =>
        {
            nameLabel.Text = _loc.T("name");
            tabLabel.Text = _loc.T("tab");
            addItem.Text = _loc.T("add.item");
        };
        return wrap;
    }

    private static void RelayoutProfileHost(Control layoutHost, Control body)
    {
        LayoutPlainChildren(body);
        UiFactory.Relayout(layoutHost);
    }

    private Panel BuildAutoCastPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var rowControls = new List<(SettingRow Skill, NumericUpDown Interval, Button Status, SettingRow Hotkey)>();
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var row = _settings.Current.AutoCast.Rows[index];
            var block = UiFactory.VStack();
            var skill = new SettingRow(SettingRowKind.Hotkey, $"{_loc.T("skill")} {index + 1}", "");
            void RefreshSkill()
            {
                skill.LabelText = $"{_loc.T("skill")} {index + 1}";
                skill.ValueText = KeyNames.Parse(row.SkillHotkey) == Keys.None
                    ? _loc.T("not.set")
                    : KeyNames.Display(row.SkillHotkey);
            }
            RefreshSkill();
            skill.Activated += (_, _) =>
            {
                skill.ValueText = _loc.T("pressAny");
                _hotkeys.BeginCapture(key =>
                {
                    TryAssignHotkey(key, v => row.SkillHotkey = v, "ac.skill:" + index);
                    RefreshSkill();
                });
            };
            _refreshAllLabels += RefreshSkill;

            var interval = UiFactory.Num(50, 60000, Math.Max(50, row.IntervalMs));
            interval.ValueChanged += (_, _) => { row.IntervalMs = (int)interval.Value; Persist(); };
            var status = UiFactory.Btn(row.Enabled ? _loc.T("on") : _loc.T("off"),
                row.Enabled ? BtnKind.Primary : BtnKind.Danger);
            status.Click += (_, _) =>
            {
                _autoCast.Toggle(index);
                status.Text = row.Enabled ? _loc.T("on") : _loc.T("off");
                UiFactory.ApplyKind(status, row.Enabled ? BtnKind.Primary : BtnKind.Danger);
                RefreshStatusBar();
            };
            var hotkey = new SettingRow(SettingRowKind.Hotkey, _loc.T("hotkey"), "");
            BindHotkeyRow(hotkey, () => row.ToggleHotkey, v => row.ToggleHotkey = v, "ac:" + index, "hotkey");

            block.Controls.Add(skill);
            block.Controls.Add(UiFactory.FieldRow(UiFactory.Lbl(_loc.T("interval")), interval, 120));
            block.Controls.Add(status);
            block.Controls.Add(hotkey);
            rowControls.Add((skill, interval, status, hotkey));
            UiFactory.Add(stack, UiFactory.Section($"{_loc.T("skill")} {index + 1}", block));
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
            RefreshStatusBar();
        };
        _autoCast.SetUiRefresh(() => _refreshAutoCast?.Invoke());
        return scroll;
    }
}
