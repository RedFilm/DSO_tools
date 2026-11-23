using DrakenHub.Core;
using DrakenHub.Models;
using DrakenHub.Services;

namespace DrakenHub.UI;

internal sealed partial class MainForm
{
    private Panel BuildTeleportPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var tp = _settings.Current.FastTeleport;

        var calib = UiFactory.VStack();
        var map = new SettingRow(SettingRowKind.Coord, _loc.T("map"), "");
        BindCoordRow(map, () => tp.MapItem, v => tp.MapItem = v, "map", () => _settings.Current.Inventory.Grid.CellSize);
        var back = new SettingRow(SettingRowKind.Coord, _loc.T("back"), "");
        BindCoordRow(back, () => tp.BackButton, v => tp.BackButton = v, "back");
        var enter = new SettingRow(SettingRowKind.Coord, _loc.T("enter"), "");
        BindCoordRow(enter, () => tp.EnterButton, v => tp.EnterButton = v, "enter");
        calib.Controls.Add(map);
        calib.Controls.Add(back);
        calib.Controls.Add(enter);
        var delayRow = UiFactory.DelaySliderRow(_loc.T("delay"), 1, 300, tp.ClickDelayMs,
            v => tp.ClickDelayMs = v, () => Persist(), labelWidth: 180);
        calib.Controls.Add(delayRow);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("section.calib"), calib, out var calibTitle));
        _refreshAllLabels += () =>
        {
            calibTitle.Text = _loc.T("section.calib");
            if (delayRow.Controls[0] is Label l) l.Text = _loc.T("delay");
        };

        var list = UiFactory.VStack();
        void Rebuild()
        {
            list.Controls.Clear();
            foreach (var loc in tp.Locations.ToList())
                list.Controls.Add(BuildTeleportBlock(loc, scroll));
            UiFactory.Relayout(scroll);
        }

        var add = UiFactory.Btn(_loc.T("add.location"), BtnKind.Neutral);
        add.Click += (_, _) =>
        {
            tp.Locations.Add(new TeleportLocation { Name = $"Location {tp.Locations.Count + 1}" });
            Persist(immediate: true);
            Rebuild();
            _refreshBindingMap?.Invoke();
        };
        var del = UiFactory.Btn(_loc.T("delete.last"), BtnKind.Danger);
        del.Click += (_, _) =>
        {
            if (tp.Locations.Count == 0) return;
            if (!ConfirmAction("confirm.delete")) return;
            tp.Locations.RemoveAt(tp.Locations.Count - 1);
            Persist(immediate: true);
            Rebuild();
            _refreshBindingMap?.Invoke();
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

    private Control BuildTeleportBlock(TeleportLocation loc, Control layoutHost)
    {
        var body = UiFactory.VStack();
        body.Visible = false;
        var nameLabel = UiFactory.Lbl(_loc.T("locName"));
        var name = UiFactory.Tb(loc.Name);
        name.Leave += (_, _) => Persist();

        var region = new SettingRow(SettingRowKind.Coord, _loc.T("region"), "");
        BindCoordRow(region, () => loc.Region, v => loc.Region = v, "region");
        var location = new SettingRow(SettingRowKind.Coord, _loc.T("location"), "");
        BindCoordRow(location, () => loc.Location, v => loc.Location = v, "location");
        var difficulty = new SettingRow(SettingRowKind.Coord, _loc.T("difficulty"), "");
        BindCoordRow(difficulty, () => loc.Difficulty, v => loc.Difficulty = v, "difficulty");

        body.Controls.Add(UiFactory.FieldRow(nameLabel, name, 150));
        body.Controls.Add(region);
        body.Controls.Add(location);
        body.Controls.Add(difficulty);

        var hotkey = UiFactory.Btn("", BtnKind.Primary, 30);
        BindLaunchButton(hotkey, () => loc.Hotkey, v => loc.Hotkey = v, "tp:" + loc.Id, "hotkey");
        var wrap = BuildCollapsibleBlock(loc.Name, body, layoutHost, hotkey);
        name.TextChanged += (_, _) =>
        {
            loc.Name = name.Text;
            SetCollapsibleTitle(wrap, loc.Name);
        };
        _refreshAllLabels += () => nameLabel.Text = _loc.T("locName");
        return wrap;
    }

    private Panel BuildSpeedBoostPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var sb = _settings.Current.SpeedBoost;
        var profileIds = SpeedBoostService.Profiles.Select(p => p.Id).ToArray();

        var toggle = UiFactory.Btn(_loc.T(_speedBoost.IsEnabled ? "sb.disable" : "sb.connect"), BtnKind.Primary, 40);
        var status = UiFactory.Lbl($"{_loc.T("sb.status")}: {_loc.T("sb.status.disabled")}", true);
        var profileLabel = UiFactory.Lbl(_loc.T("sb.profile"));
        var profileCombo = UiFactory.Combo(profileIds.Select(id => _loc.T("sb.profile." + id)).ToArray());
        var profileIndex = Array.IndexOf(profileIds, sb.ProfileId);
        profileCombo.SelectedIndex = profileIndex >= 0 ? profileIndex : 0;
        var boostLabel = UiFactory.Lbl(_loc.T("sb.boost"));
        var boostValue = UiFactory.Lbl(sb.BoostDisplay.ToString());
        var boostSlider = UiFactory.Slider(
            SpeedBoostService.BoostDisplayMin,
            SpeedBoostService.BoostDisplayMax,
            Math.Clamp(sb.BoostDisplay, SpeedBoostService.BoostDisplayMin, SpeedBoostService.BoostDisplayMax));
        var hotkey = new SettingRow(SettingRowKind.Hotkey, _loc.T("sb.hotkey"), "");
        var hint = UiFactory.Lbl(_loc.T("sb.hint"), true);

        void SyncSliderEnabled()
        {
            var profile = SpeedBoostService.ResolveProfile(sb.ProfileId);
            boostSlider.Enabled = profile.SliderEnabled;
            boostLabel.Enabled = profile.SliderEnabled;
            boostValue.Enabled = profile.SliderEnabled;
            boostValue.Text = profile.SliderEnabled ? sb.BoostDisplay.ToString() : "—";
        }

        void RefreshSpeedBoostUi()
        {
            var launching = _speedBoost.IsLaunching;
            var on = _speedBoost.IsEnabled || launching;
            toggle.Text = _loc.T(on ? "sb.disable" : "sb.connect");
            UiFactory.ApplyKind(toggle, on ? BtnKind.Danger : BtnKind.Primary);
            if (!_speedBoost.IsEnabled && !launching && _speedBoostStatusKey is "connected" or "not_connected" or "starting")
                _speedBoostStatusKey = "disabled";
            if (launching && _speedBoostStatusKey is "disabled" or "")
                _speedBoostStatusKey = "starting";
            var statusKey = _speedBoostStatusKey switch
            {
                "connected" => "connected",
                "no_process" => "no_process",
                "access_denied" => "access_denied",
                "pointer_failed" => "pointer_failed",
                "not_connected" => "not_connected",
                "starting" => "starting",
                "engine_missing" => "engine_missing",
                "engine_assets_missing" => "engine_assets_missing",
                "engine_start_failed" => "engine_start_failed",
                "engine_exited" => "engine_exited",
                _ => "disabled"
            };
            var localized = _loc.T("sb.status." + statusKey);
            if (localized == "sb.status." + statusKey)
                localized = statusKey;
            status.Text = $"{_loc.T("sb.status")}: {localized}";
            status.ForeColor = statusKey is "connected" ? Theme.Success : Theme.TextMuted;
            SyncSliderEnabled();
            RefreshStatusBar();
        }

        toggle.Click += (_, _) =>
        {
            SpeedBoostLog.Host("UI toggle.Click", includeStack: true);
            _speedBoost.Toggle();
            _speedBoostStatusKey = _speedBoost.LastDetail;
            Persist(immediate: true, hotkeys: false);
            RefreshSpeedBoostUi();
        };

        var control = UiFactory.VStack();
        control.Controls.Add(toggle);
        control.Controls.Add(status);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("section.launch"), control, out var controlTitle));

        profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressUiEvents) return;
            if (profileCombo.SelectedIndex < 0 || profileCombo.SelectedIndex >= profileIds.Length) return;
            var previousId = sb.ProfileId;
            sb.ProfileId = profileIds[profileCombo.SelectedIndex];
            SyncSliderEnabled();
            if (_speedBoost.IsEnabled)
                _speedBoost.SwitchActiveProfile(previousId);
            Persist(hotkeys: false);
            RefreshSpeedBoostUi();
        };

        boostSlider.ValueChanged += (_, _) =>
        {
            sb.BoostDisplay = boostSlider.Value;
            boostValue.Text = boostSlider.Value.ToString();
            if (_speedBoost.IsEnabled)
                _speedBoost.ReapplyIfEnabled();
        };
        boostSlider.DragEnded += (_, _) => Persist(hotkeys: false);

        var boostRow = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = 36,
            BackColor = Color.Transparent
        };
        boostRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        boostRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        boostRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        boostLabel.Dock = DockStyle.Fill;
        boostSlider.Dock = DockStyle.Fill;
        boostValue.Dock = DockStyle.Fill;
        boostRow.Controls.Add(boostLabel, 0, 0);
        boostRow.Controls.Add(boostSlider, 1, 0);
        boostRow.Controls.Add(boostValue, 2, 0);

        BindHotkeyRow(hotkey, () => sb.ToggleHotkey, v => sb.ToggleHotkey = v, "speedboost.toggle", "sb.hotkey");

        var settings = UiFactory.VStack();
        settings.Controls.Add(UiFactory.FieldRow(profileLabel, profileCombo, 160));
        settings.Controls.Add(boostRow);
        settings.Controls.Add(hotkey);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("section.params"), settings, out var settingsTitle));
        UiFactory.Add(stack, hint);

        _refreshSpeedBoost = RefreshSpeedBoostUi;
        _refreshAllLabels += () =>
        {
            controlTitle.Text = _loc.T("section.launch");
            settingsTitle.Text = _loc.T("section.params");
            profileLabel.Text = _loc.T("sb.profile");
            boostLabel.Text = _loc.T("sb.boost");
            hint.Text = _loc.T("sb.hint");
            var sel = profileCombo.SelectedIndex;
            profileCombo.Items.Clear();
            foreach (var id in profileIds)
                profileCombo.Items.Add(_loc.T("sb.profile." + id));
            profileCombo.SelectedIndex = sel >= 0 && sel < profileIds.Length ? sel : 0;
            RefreshSpeedBoostUi();
        };

        SyncSliderEnabled();
        RefreshSpeedBoostUi();
        return scroll;
    }

    private Panel BuildSettingsPage()
    {
        var scroll = UiFactory.ScrollPage(out var stack);
        var s = _settings.Current;

        var langInner = UiFactory.VStack();
        var lang = UiFactory.Combo(_loc.T("lang.ru"), _loc.T("lang.en"));
        lang.SelectedIndex = s.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        lang.SelectedIndexChanged += (_, _) =>
        {
            // Setting SelectedIndex while refreshing combo items re-enters this handler → hang.
            if (_suppressUiEvents || lang.SelectedIndex < 0) return;

            s.Language = lang.SelectedIndex == 1 ? "en" : "ru";
            _loc.Language = s.Language;
            Persist(immediate: true);

            _suppressUiEvents = true;
            try
            {
                RelocalizeNav();
                _refreshAllLabels?.Invoke();
                // Play page already rebuilds map inside _refreshAllLabels — skip duplicate.
                RefreshStatusBar();

                var sel = lang.SelectedIndex;
                lang.BeginUpdate();
                try
                {
                    lang.Items.Clear();
                    lang.Items.Add(_loc.T("lang.ru"));
                    lang.Items.Add(_loc.T("lang.en"));
                    if (sel >= 0 && sel < lang.Items.Count)
                        lang.SelectedIndex = sel;
                }
                finally
                {
                    lang.EndUpdate();
                }
            }
            finally
            {
                _suppressUiEvents = false;
            }
        };
        langInner.Controls.Add(lang);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("language"), langInner, out var langTitle));

        var gameInner = UiFactory.VStack();
        var titleLabel = UiFactory.Lbl(_loc.T("gameWindowTitle"));
        var titleBox = UiFactory.Tb(s.GameWindowTitleContains);
        titleBox.Leave += (_, _) =>
        {
            s.GameWindowTitleContains = titleBox.Text.Trim();
            Persist(hotkeys: false);
        };
        var hint = UiFactory.Lbl(_loc.T("gameWindowHint"), true);
        gameInner.Controls.Add(UiFactory.FieldRow(titleLabel, titleBox, 180));
        gameInner.Controls.Add(hint);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("gameWindow"), gameInner, out var gameTitle));

        var profileInner = UiFactory.VStack();
        var exportBtn = UiFactory.Btn(_loc.T("export"), BtnKind.Neutral);
        var importBtn = UiFactory.Btn(_loc.T("import"), BtnKind.Neutral);
        exportBtn.Click += (_, _) =>
        {
            using var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "drakenhub-settings.json" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                _settings.Export(dlg.FileName);
                SetStatusMessage(_loc.T("export.ok"));
            }
            catch
            {
                MessageBox.Show(_loc.T("export.fail"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        importBtn.Click += (_, _) =>
        {
            if (!ConfirmAction("import.confirm")) return;
            using var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                _settings.Import(dlg.FileName);
                _loc.Language = _settings.Current.Language;
                RebuildUi(_activePage);
                RegisterAllHotkeys();
                _autoClicker.ApplySettings();
                _autoCast.ApplySettings();
                UpdateGridOverlay();
                SetStatusMessage(_loc.T("import.ok"));
            }
            catch
            {
                MessageBox.Show(_loc.T("import.fail"), _loc.T("notify"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        profileInner.Controls.Add(UiFactory.EqualRow(exportBtn, importBtn));
        UiFactory.Add(stack, UiFactory.Section(_loc.T("profile"), profileInner, out var profileTitle));

        var hotInner = UiFactory.VStack();
        var hide = new SettingRow(SettingRowKind.Hotkey, _loc.T("hide"), "");
        BindHotkeyRow(hide, () => s.WindowHotkeys.HideShow, v => s.WindowHotkeys.HideShow = v, "win.hide", "hide");
        var exit = new SettingRow(SettingRowKind.Hotkey, _loc.T("exit"), "");
        BindHotkeyRow(exit, () => s.WindowHotkeys.Exit, v => s.WindowHotkeys.Exit = v, "win.exit", "exit");
        var stop = new SettingRow(SettingRowKind.Hotkey, _loc.T("stop"), "");
        BindHotkeyRow(stop, () => s.WindowHotkeys.EmergencyStop, v => s.WindowHotkeys.EmergencyStop = v, "win.stop", "stop");
        hotInner.Controls.Add(hide);
        hotInner.Controls.Add(exit);
        hotInner.Controls.Add(stop);
        UiFactory.Add(stack, UiFactory.Section(_loc.T("winHotkeys"), hotInner, out var hotTitle));

        _refreshAllLabels += () =>
        {
            langTitle.Text = _loc.T("language");
            gameTitle.Text = _loc.T("gameWindow");
            titleLabel.Text = _loc.T("gameWindowTitle");
            hint.Text = _loc.T("gameWindowHint");
            profileTitle.Text = _loc.T("profile");
            hotTitle.Text = _loc.T("winHotkeys");
            exportBtn.Text = _loc.T("export");
            importBtn.Text = _loc.T("import");
        };
        return scroll;
    }
}
