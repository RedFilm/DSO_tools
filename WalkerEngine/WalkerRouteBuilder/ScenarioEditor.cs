using System.Runtime.InteropServices;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal sealed partial class MainForm
{
    private readonly record struct BlockRow(ScenarioBlock Block, List<ScenarioBlock> Parent, int Index, int Depth);

    private int _clickDragIndex = -1;

    private Point _clickDragOrigin;

    private bool _clickDragMoved;

    private ScenarioBlock? CurrentBlock => _blockRow >= 0 && _blockRow < _rows.Count ? _rows[_blockRow].Block : null;

    private FollowBlock? EditedFollow => CurrentBlock as FollowBlock;

    private LockBlock? EditedLock => CurrentBlock as LockBlock;

    private SequenceBlock? EditedSequence => CurrentBlock as SequenceBlock;

    private RepeatBlock? EditedRepeat => CurrentBlock as RepeatBlock;

    private Button BlockButton(string text, Action action)
    {
        var button = new Button { Text = text };
        StyleButton(button, roomy: true);
        button.MinimumSize = new Size(0, 32);
        button.Padding = new Padding(10, 4, 10, 4);
        button.Margin = new Padding(0, 2, 6, 0);
        button.Click += (_, _) => action();
        return button;
    }

    private Button PointButton(string text, AnchorSource source)
    {
        var button = new Button { Text = text };
        StyleButton(button, roomy: true);
        button.Click += (_, _) => AddAnchor(source);
        return button;
    }

    private static Label InlineLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
            ForeColor = Color.FromArgb(210, 210, 210),
        };

    private void ConfigureClickEditor(Panel options)
    {
        _clickRow.BackColor = Color.FromArgb(32, 32, 32);
        _clickRow.Dock = DockStyle.Fill;
        _clickX.Minimum = 0;
        _clickX.Maximum = 4000;
        _clickX.Width = 64;
        _clickY.Minimum = 0;
        _clickY.Maximum = 4000;
        _clickY.Width = 64;
        _clickPause.Minimum = 0;
        _clickPause.Maximum = 120000;
        _clickPause.Increment = 100;
        _clickPause.Width = 72;
        _clickHold.Minimum = 0;
        _clickHold.Maximum = 2000;
        _clickHold.Value = 40;
        _clickHold.Width = 64;
        foreach (NumericUpDown field in new[] { _clickX, _clickY, _clickPause, _clickHold })
            field.ValueChanged += (_, _) => ApplySelectedClick();
        _clickName.TextChanged += (_, _) => ApplySelectedClick();

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = false,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        fields.Controls.Add(InlineLabel("X"));
        fields.Controls.Add(_clickX);
        fields.Controls.Add(InlineLabel("Y"));
        fields.Controls.Add(_clickY);
        fields.Controls.Add(InlineLabel("После"));
        fields.Controls.Add(_clickPause);
        fields.Controls.Add(InlineLabel("Удержание"));
        fields.Controls.Add(_clickHold);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = false,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        actions.Controls.Add(BlockButton("Добавить нажатие", AddClick));
        actions.Controls.Add(BlockButton("Удалить нажатие", RemoveClick));
        actions.Controls.Add(new Label
        {
            Text = "F9 — снять с курсора",
            AutoSize = true,
            Margin = new Padding(8, 8, 0, 0),
            ForeColor = Color.FromArgb(176, 176, 176),
        });

        var nameRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nameRow.Controls.Add(InlineLabel("Название"), 0, 0);
        _clickName.Dock = DockStyle.Fill;
        _clickName.Margin = new Padding(0, 4, 0, 0);
        nameRow.Controls.Add(_clickName, 1, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        grid.Controls.Add(nameRow, 0, 0);
        grid.Controls.Add(fields, 0, 1);
        grid.Controls.Add(actions, 0, 2);
        _clicks.MouseDown += (_, e) => BeginClickDrag(e);
        _clicks.MouseMove += (_, e) => ContinueClickDrag(e);
        _clicks.MouseUp += (_, _) => EndClickDrag();
        _clicks.MouseCaptureChanged += (_, _) =>
        {
            if (!_clicks.Capture)
                EndClickDrag();
        };
        _clickRow.Controls.Add(grid);
        options.Controls.Add(_clickRow);
    }

    private void RebuildBlocks()
    {
        if (ViewingChainOverview)
        {
            _rows.Clear();
            _blockRow = -1;
            ShowBlockSettings();
            RebuildChainView();
            return;
        }

        if (_scenarioRecord is null)
        {
            _rows.Clear();
            _blockRow = -1;
            _picked.Clear();
            ShowBlockSettings();
            _diagram.Show(null, null, _picked, false, null, false, "");
            UpdateBreadcrumbs();
            UpdatePaletteForMode();
            return;
        }

        _rows.Clear();
        AddRows(_scenario.Blocks, depth: 0);
        if (_rows.Count == 0)
            _blockRow = -1;
        else
            _blockRow = Math.Clamp(_blockRow, 0, _rows.Count - 1);
        ShowBlockSettings();
        string caption = _scenarioRecord?.Name ?? "";
        _picked.RemoveAll(block => !_rows.Any(row => ReferenceEquals(row.Block, block)));
        if (_picked.Count == 0 && CurrentBlock is ScenarioBlock current)
            _picked.Add(current);
        _diagram.Show(
            _scenarioRecord is null ? null : _scenario,
            CurrentBlock,
            _picked,
            _following && _runningBlock is null && !_selectionRun,
            _runningBlock,
            _following && _selectionRun,
            caption);
        UpdateBreadcrumbs();
        UpdatePaletteForMode();
    }

    private static void AddRows(List<ScenarioBlock> blocks, int depth, List<BlockRow> rows)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            rows.Add(new BlockRow(blocks[i], blocks, i, depth));
            if (blocks[i] is RepeatBlock repeat)
                AddRows(repeat.Children, depth + 1, rows);
        }
    }

    private void AddRows(List<ScenarioBlock> blocks, int depth)
        => AddRows(blocks, depth, _rows);

    private static string BlockSuffix(ScenarioBlock block)
        => block switch
        {
            FollowBlock follow => $" · {follow.Anchors.Count} точ.",
            LockBlock locked => locked.Keys.Count == 0 ? "" : $" · {locked.Keys.Count} кл.",
            DoorBlock door => $" · {door.DifficultyName}",
            SequenceBlock sequence => $" · {sequence.Clicks.Count} наж.",
            RepeatBlock repeat => $" · {repeat.Children.Count}",
            _ => "",
        };

    private void SelectBlock(int index)
    {
        _blockRow = index >= 0 && index < _rows.Count ? index : -1;
        _selected = -1;
        _libraryMapPreview = false;
        ShowBlockSettings();
        RefreshView();
    }

    private void ShowBlockSettings()
    {
        bool follow = EditedFollow is not null;
        bool locked = EditedLock is not null;
        bool sequence = EditedSequence is not null;
        bool repeat = EditedRepeat is not null;
        bool door = CurrentBlock is DoorBlock;
        bool openChest = CurrentBlock is OpenChestBlock;
        bool questMark = CurrentBlock is ClickQuestMarkBlock;
        bool useMap = CurrentBlock is UseMapBlock;
        bool acceptDialog = CurrentBlock is AcceptDialogBlock;
        bool mountHorses = CurrentBlock is MountHorsesBlock;
        _followOptions.Visible = follow;
        _lockOptions.Visible = locked;
        _openChestOptions.Visible = openChest;
        _doorOptions.Visible = door;
        _questMarkOptions.Visible = questMark;
        _useMapOptions.Visible = useMap;
        _clickRow.Visible = sequence;
        _repeatOptions.Visible = repeat;
        _list.Visible = follow;
        _clicks.Visible = sequence;
        if (_pointButtons is not null)
            _pointButtons.Visible = follow;
        if (_keysHost is not null)
            _keysHost.Visible = follow || locked;

        _suppressBlocks = true;
        if (EditedFollow is FollowBlock route)
        {
            _travel.SelectedIndex = route.Travel == TravelMode.Shortest ? 1 : 0;
            _delay.Value = Math.Clamp(route.DelayMilliseconds, (int)_delay.Minimum, (int)_delay.Maximum);
            _chaseLost.Value = Math.Clamp(route.ChaseLostMilliseconds, (int)_chaseLost.Minimum, (int)_chaseLost.Maximum);
            FillFollowMapCombo(route);
            if (!_libraryMapPreview)
                ShowFollowBlockMap(route);
            string mapName = string.IsNullOrWhiteSpace(route.MapFile)
                ? ""
                : " Карта: " + Path.GetFileNameWithoutExtension(route.MapFile) + ".";
            string routeFile = string.IsNullOrWhiteSpace(route.RouteFile) ? "" : " Файл: " + Path.GetFileName(route.RouteFile) + ".";
            _settingsHint.Text = route.Travel == TravelMode.Shortest
                ? "Кратчайший проход по полу. Клик с выбранной точкой — вставка после неё; клик по ребру — между точками. Порядок: «Раньше»/«Позже» или перетаскивание в списке." + mapName + routeFile
                : "Клик по карте: после выбранной точки или в конец. Клик по ребру — вставка между. Порядок: «Раньше»/«Позже», Ctrl+↑/↓ или drag в списке." + mapName + routeFile;
        }
        else if (EditedLock is LockBlock hold)
        {
            _lockTarget.SelectedIndex = hold.Target == AnchorSource.Chest ? 1 : 0;
            _radius.Value = Math.Clamp(hold.RadiusPixels, (int)_radius.Minimum, (int)_radius.Maximum);
            _lockWait.Value = Math.Clamp(hold.WaitMilliseconds, (int)_lockWait.Minimum, (int)_lockWait.Maximum);
            string goal = hold.Target == AnchorSource.Chest ? "сундуку" : "красному";
            _settingsHint.Text = $"Персонаж разворачивается лицом к {goal} по положению на карте. Клавиши жмутся, пока цель внутри радиуса. «Ожидание» — сколько ждать появления цели и подтверждения исчезновения.";
        }
        else if (sequence)
        {
            _settingsHint.Text = "Координаты внутри окна игры. Пауза в строке выдерживается после этого нажатия. Порядок меняется перетаскиванием строки. F9 снимает точку с курсора относительно этого окна.";
        }
        else if (EditedRepeat is RepeatBlock repeating)
        {
            _repeatCondition.SelectedIndex = 0;
            _repeatWait.Value = Math.Clamp(repeating.WaitMilliseconds, (int)_repeatWait.Minimum, (int)_repeatWait.Maximum);
            _settingsHint.Text = "Блоки внутри повторяются, пока на карте есть красный. «Ожидание» — сколько ждать первое появление; если за это время красного нет, повтор пропускается. Новый блок при выбранном повторе попадает внутрь.";
        }
        else if (CurrentBlock is DoorBlock doorBlock)
        {
            _doorDifficulty.SelectedIndex = DoorDifficulties.Clamp(doorBlock.Difficulty) - 1;
            _settingsHint.Text = "Ищет ближайшую бирюзовую дверь на карте и подходит к ней. После открытия окна входа кликает по шкале сложности, затем Enter и ждёт новую локацию.";
        }
        else if (CurrentBlock is OpenChestBlock chest)
        {
            _chestWait.Value = Math.Clamp(chest.WaitMilliseconds, (int)_chestWait.Minimum, (int)_chestWait.Maximum);
            _settingsHint.Text = "Ищет ближайший сундук на карте и подходит примерно на 12 пикселей. Затем кликает, пока значок не пропадёт. «Ожидание» — появление сундука и подтверждение исчезновения.";
        }
        else if (CurrentBlock is ClickQuestMarkBlock quest)
        {
            _questMarkTarget.SelectedIndex = quest.Mark == AnchorSource.QuestExclaim ? 1 : 0;
            _settingsHint.Text = "Ищет выбранный квестовый знак на карте, подходит и кликает. Шаг заканчивается, когда знак пропал или открылся диалог с галочкой.";
        }
        else if (CurrentBlock is UseMapBlock changeMap)
        {
            FillUseMapCombo(changeMap);
            _settingsHint.Text = "Устаревший блок. Лучше задайте карту прямо в «Следовать маршруту».";
        }
        else if (acceptDialog)
        {
            _settingsHint.Text = "Ищет зелёную галочку в окне диалога и нажимает её. Шаг заканчивается, когда кнопка исчезла.";
        }
        else if (mountHorses)
        {
            _settingsHint.Text = "Жмёт O каждую секунду, пока в центре экрана не появится голубая полоска посадки. Затем ждёт её исчезновения и заканчивает шаг.";
        }
        else
        {
            _settingsHint.Text = "Выберите блок, чтобы изменить его параметры.";
        }

        if (CurrentBlock is null)
            _blockHint.Text = "Добавьте блок. Двойной щелчок открывает его параметры. Точки маршрута ставятся на карте.";
        else
            _blockHint.Text = "Точки выбранного маршрута ставятся на карте снизу.";

        _suppressBlocks = false;
        _canvas.InputLocked = PlaybackLocked || !follow;
        LayoutSettings(follow, locked, sequence, repeat, acceptDialog || mountHorses, questMark || useMap || openChest || door);
    }

    private void FillUseMapCombo(UseMapBlock block)
    {
        FillLibraryMapCombo(_useMapTarget, block.MapFile, path => block.MapFile = path);
    }

    private void FillFollowMapCombo(FollowBlock block)
    {
        FillLibraryMapCombo(_followMapTarget, block.MapFile, path => block.MapFile = path);
    }

    private void FillLibraryMapCombo(ComboBox combo, string? current, Action<string> assign)
    {
        combo.Items.Clear();
        var maps = new List<string>();
        foreach (MapRecord map in _library.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.ImagePath))
                continue;
            if (maps.Any(item => string.Equals(item, map.ImagePath, StringComparison.OrdinalIgnoreCase)))
                continue;
            maps.Add(map.ImagePath);
        }

        if (!string.IsNullOrWhiteSpace(current)
            && maps.All(item => !string.Equals(item, current, StringComparison.OrdinalIgnoreCase)))
            maps.Insert(0, current);

        int selected = -1;
        for (int i = 0; i < maps.Count; i++)
        {
            combo.Items.Add(new MapChoice(maps[i]));
            if (string.Equals(maps[i], current, StringComparison.OrdinalIgnoreCase))
                selected = i;
        }

        if (selected < 0 && combo.Items.Count > 0)
        {
            selected = 0;
            assign(maps[0]);
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = selected;
    }

    private void OnFollowMapChanged()
    {
        if (_suppressBlocks || EditedFollow is not FollowBlock follow)
            return;
        if (_followMapTarget.SelectedItem is not MapChoice choice)
            return;

        string path = choice.Path;
        if (string.Equals(follow.MapFile, path, StringComparison.OrdinalIgnoreCase))
            return;

        PushUndo();
        follow.MapFile = path;
        _scenario.MapFile = path;
        _dirty = true;
        if (_scenarioRecord is not null)
            _scenarioRecord.Dirty = true;
        _libraryMapPreview = false;
        ShowFollowBlockMap(follow);
        RebuildBlocks();
        RefreshTitle();
    }

    private void ShowFollowBlockMap(FollowBlock follow)
    {
        string? path = follow.MapFile;
        if (string.IsNullOrWhiteSpace(path))
            path = _mapPath ?? _mapRecord?.ImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        if (string.Equals(_mapPath, path, StringComparison.OrdinalIgnoreCase) && _bitmap is not null)
        {
            _mapRecord = _library.Maps.FirstOrDefault(item =>
                string.Equals(item.ImagePath, path, StringComparison.OrdinalIgnoreCase)) ?? _mapRecord;
            return;
        }

        MapRecord? map = _library.Maps.FirstOrDefault(item =>
            string.Equals(item.ImagePath, path, StringComparison.OrdinalIgnoreCase));
        if (map is not null)
        {
            _mapRecord = map;
            EnsureMapImage(map);
        }
        else
        {
            _mapRecord = null;
            LoadMap(path);
        }

        _scenario.MapFile = path;
    }

    private void LayoutSettings(bool follow, bool locked, bool sequence, bool repeat, bool hintOnly, bool questMark)
    {
        if (_settingsPage is null)
            return;

        if (follow)
        {
            SetSettingsRow(0, SizeType.Absolute, 52);
            SetSettingsRow(1, SizeType.Absolute, 78);
            SetSettingsRow(2, SizeType.Percent, 2);
            SetSettingsRow(3, SizeType.Absolute, 148);
            SetSettingsRow(4, SizeType.Percent, 1);
        }
        else if (locked)
        {
            SetSettingsRow(0, SizeType.Absolute, 64);
            SetSettingsRow(1, SizeType.Absolute, 72);
            SetSettingsRow(2, SizeType.Absolute, 0);
            SetSettingsRow(3, SizeType.Absolute, 0);
            SetSettingsRow(4, SizeType.Percent, 100);
        }
        else if (questMark)
        {
            SetSettingsRow(0, SizeType.Absolute, 64);
            SetSettingsRow(1, SizeType.Absolute, 44);
            SetSettingsRow(2, SizeType.Absolute, 0);
            SetSettingsRow(3, SizeType.Absolute, 0);
            SetSettingsRow(4, SizeType.Percent, 100);
        }
        else if (sequence)
        {
            SetSettingsRow(0, SizeType.Absolute, 64);
            SetSettingsRow(1, SizeType.Absolute, 110);
            SetSettingsRow(2, SizeType.Percent, 100);
            SetSettingsRow(3, SizeType.Absolute, 0);
            SetSettingsRow(4, SizeType.Absolute, 0);
        }
        else if (repeat)
        {
            SetSettingsRow(0, SizeType.Absolute, 64);
            SetSettingsRow(1, SizeType.Absolute, 44);
            SetSettingsRow(2, SizeType.Absolute, 0);
            SetSettingsRow(3, SizeType.Absolute, 0);
            SetSettingsRow(4, SizeType.Percent, 100);
        }
        else if (hintOnly)
        {
            SetSettingsRow(0, SizeType.Absolute, 96);
            SetSettingsRow(1, SizeType.Absolute, 0);
            SetSettingsRow(2, SizeType.Absolute, 0);
            SetSettingsRow(3, SizeType.Absolute, 0);
            SetSettingsRow(4, SizeType.Percent, 100);
        }
        else
        {
            SetSettingsRow(0, SizeType.Percent, 100);
            SetSettingsRow(1, SizeType.Absolute, 0);
            SetSettingsRow(2, SizeType.Absolute, 0);
            SetSettingsRow(3, SizeType.Absolute, 0);
            SetSettingsRow(4, SizeType.Absolute, 0);
        }
    }

    private void SetSettingsRow(int index, SizeType type, float height)
        => _settingsPage!.RowStyles[index] = new RowStyle(type, height);

    private void AddBlock(ScenarioBlock block)
    {
        if (_scenarioRecord is null)
        {
            _status.Text = "Сначала создайте или загрузите сценарий.";
            return;
        }

        if (PlaybackLocked)
            return;
        PushUndo();
        List<ScenarioBlock> parent = _scenario.Blocks;
        int index = parent.Count;
        if (_blockRow >= 0 && _blockRow < _rows.Count)
        {
            BlockRow row = _rows[_blockRow];
            if (row.Block is RepeatBlock repeat)
            {
                parent = repeat.Children;
                index = parent.Count;
            }
            else
            {
                parent = row.Parent;
                index = row.Index + 1;
            }
        }

        parent.Insert(index, block);
        _picked.Clear();
        _picked.Add(block);
        _blockRow = RowOf(block);
        TouchRoute();
    }

    private void RemoveBlock()
    {
        if (PlaybackLocked || _blockRow < 0 || _blockRow >= _rows.Count)
            return;
        BlockRow row = _rows[_blockRow];
        if (row.Block is RepeatBlock repeat && repeat.Children.Count > 0 &&
            MessageBox.Show(this, "Удалить повтор вместе с блоками внутри?", "Удалить блок", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        PushUndo();
        row.Parent.RemoveAt(row.Index);
        _blockRow = Math.Min(_blockRow, Math.Max(0, _rows.Count - 2));
        TouchRoute();
    }

    private void MoveBlock(int delta)
    {
        if (PlaybackLocked || _blockRow < 0 || _blockRow >= _rows.Count)
            return;
        BlockRow row = _rows[_blockRow];
        int target = row.Index + delta;
        if (target < 0 || target >= row.Parent.Count)
            return;
        PushUndo();
        (row.Parent[row.Index], row.Parent[target]) = (row.Parent[target], row.Parent[row.Index]);
        _blockRow = RowOf(row.Parent[target]);
        TouchRoute();
    }

    private int RowOf(ScenarioBlock block)
    {
        RebuildBlocks();
        for (int i = 0; i < _rows.Count; i++)
        {
            if (ReferenceEquals(_rows[i].Block, block))
                return i;
        }

        return 0;
    }

    private void AddAnchor(AnchorSource source)
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow)
        {
            _status.Text = "Динамическая точка добавляется в блок «Следовать маршруту».";
            return;
        }

        PushUndo();
        int index = _selected >= 0 && _selected < follow.Anchors.Count ? _selected + 1 : follow.Anchors.Count;
        var anchor = new Anchor { Source = source };
        if (index > 0)
            anchor.Keys.AddRange(CloneKeys(index - 1 < follow.Anchors.Count ? follow.Anchors[Math.Min(index, follow.Anchors.Count - 1)].Keys : _defaultKeys));
        follow.Anchors.Insert(index, anchor);
        _selected = index;
        TouchRoute();
    }

    private List<MapPoint> FixedPositions()
    {
        var points = new List<MapPoint>();
        if (EditedFollow is not FollowBlock follow)
            return points;
        foreach (Anchor anchor in follow.Anchors)
        {
            if (anchor.Source == AnchorSource.Fixed)
                points.Add(anchor.Position);
        }

        return points;
    }

    private int FixedToAnchor(int fixedIndex)
    {
        if (EditedFollow is not FollowBlock follow || fixedIndex < 0)
            return -1;
        int seen = 0;
        for (int i = 0; i < follow.Anchors.Count; i++)
        {
            if (follow.Anchors[i].Source != AnchorSource.Fixed)
                continue;
            if (seen == fixedIndex)
                return i;
            seen++;
        }

        return -1;
    }

    private int AnchorToFixed(int anchorIndex)
    {
        if (EditedFollow is not FollowBlock follow || anchorIndex < 0 || anchorIndex >= follow.Anchors.Count)
            return -1;
        if (follow.Anchors[anchorIndex].Source != AnchorSource.Fixed)
            return -1;
        int seen = 0;
        for (int i = 0; i < anchorIndex; i++)
        {
            if (follow.Anchors[i].Source == AnchorSource.Fixed)
                seen++;
        }

        return seen;
    }

    private void AddClick()
    {
        if (EditedSequence is not SequenceBlock sequence || PlaybackLocked)
            return;
        PushUndo();
        sequence.Clicks.Add(new ScreenClick
        {
            X = (int)_clickX.Value,
            Y = (int)_clickY.Value,
            PauseMilliseconds = (int)_clickPause.Value,
            HoldMilliseconds = Math.Max(0, (int)_clickHold.Value),
        });
        TouchRoute();
        _clicks.SelectedIndex = sequence.Clicks.Count - 1;
    }

    private void RemoveClick()
    {
        if (EditedSequence is not SequenceBlock sequence || _clicks.SelectedIndex < 0 || PlaybackLocked)
            return;
        PushUndo();
        sequence.Clicks.RemoveAt(_clicks.SelectedIndex);
        TouchRoute();
    }

    private void CaptureClick()
    {
        if (PlaybackLocked)
        {
            _status.Text = "Остановите ведение, чтобы снять точку.";
            return;
        }

        if (EditedSequence is null)
        {
            _status.Text = "Сначала выберите блок «Нажатия».";
            return;
        }
        try
        {
            var config = FollowConfig.LoadOrCreate(FollowConfigPath);
            if (!GameWindowFocus.TryFocus(config.GameWindowTitleContains, out Rectangle client) || !GetCursorPos(out Point cursor))
            {
                _status.Text = "Окно игры не найдено. Проверьте gameWindowTitleContains.";
                return;
            }

            _clickX.Value = Math.Clamp(cursor.X - client.X, (int)_clickX.Minimum, (int)_clickX.Maximum);
            _clickY.Value = Math.Clamp(cursor.Y - client.Y, (int)_clickY.Minimum, (int)_clickY.Maximum);
            if (_clicks.SelectedIndex < 0)
                AddClick();
            else
                ApplySelectedClick();
            _status.Text = $"Точка снята: {_clickX.Value}, {_clickY.Value}.";
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
    }

    private void ShowSelectedClick()
    {
        if (_suppressBlocks || EditedSequence is not SequenceBlock sequence)
            return;
        int index = _clicks.SelectedIndex;
        if (index < 0 || index >= sequence.Clicks.Count)
            return;
        ScreenClick click = sequence.Clicks[index];
        _suppressBlocks = true;
        _clickX.Value = Math.Clamp(click.X, (int)_clickX.Minimum, (int)_clickX.Maximum);
        _clickY.Value = Math.Clamp(click.Y, (int)_clickY.Minimum, (int)_clickY.Maximum);
        _clickPause.Value = Math.Clamp(click.PauseMilliseconds, (int)_clickPause.Minimum, (int)_clickPause.Maximum);
        _clickHold.Value = Math.Clamp(click.HoldMilliseconds, (int)_clickHold.Minimum, (int)_clickHold.Maximum);
        _clickName.Text = click.Name ?? "";
        _suppressBlocks = false;
    }

    private void ApplySelectedClick()
    {
        if (_suppressBlocks || EditedSequence is not SequenceBlock sequence)
            return;
        int index = _clicks.SelectedIndex;
        if (index < 0 || index >= sequence.Clicks.Count)
            return;
        ScreenClick click = sequence.Clicks[index];
        click.X = (int)_clickX.Value;
        click.Y = (int)_clickY.Value;
        click.PauseMilliseconds = (int)_clickPause.Value;
        click.HoldMilliseconds = (int)_clickHold.Value;
        click.Name = ClickName(_clickName.Text);
        _dirty = true;
        RebuildClicks(keepSelection: true);
    }

    private void RebuildClicks(bool keepSelection)
    {
        if (EditedSequence is not SequenceBlock sequence)
        {
            _clicks.Items.Clear();
            return;
        }

        int selected = _clicks.SelectedIndex;
        _suppressBlocks = true;
        _clicks.BeginUpdate();
        _clicks.Items.Clear();
        for (int i = 0; i < sequence.Clicks.Count; i++)
            _clicks.Items.Add(FormatClick(sequence.Clicks[i], i));

        if (keepSelection && selected >= 0 && selected < _clicks.Items.Count)
            _clicks.SelectedIndex = selected;
        _clicks.EndUpdate();
        _suppressBlocks = false;
    }

    private void BeginClickDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || PlaybackLocked || EditedSequence is null)
            return;
        int index = _clicks.IndexFromPoint(e.Location);
        if (index < 0)
            return;
        _clickDragIndex = index;
        _clickDragOrigin = e.Location;
        _clickDragMoved = false;
    }

    private void ContinueClickDrag(MouseEventArgs e)
    {
        if (_clickDragIndex < 0 || e.Button != MouseButtons.Left || EditedSequence is not SequenceBlock sequence)
            return;
        if (!_clickDragMoved)
        {
            if (Math.Abs(e.Y - _clickDragOrigin.Y) < SystemInformation.DragSize.Height)
                return;
            ApplySelectedClick();
            PushUndo();
            _clickDragMoved = true;
            _clicks.Cursor = Cursors.SizeNS;
            _clicks.Capture = true;
        }

        int target = ClickDropIndex(sequence, e.Location);
        if (target < 0 || target == _clickDragIndex)
            return;
        ScreenClick click = sequence.Clicks[_clickDragIndex];
        sequence.Clicks.RemoveAt(_clickDragIndex);
        sequence.Clicks.Insert(target, click);
        _suppressBlocks = true;
        _clicks.BeginUpdate();
        for (int i = 0; i < sequence.Clicks.Count; i++)
            _clicks.Items[i] = FormatClick(sequence.Clicks[i], i);
        _clicks.SelectedIndex = target;
        _clicks.EndUpdate();
        _suppressBlocks = false;
        _clickDragIndex = target;
        _dirty = true;
        if (_scenarioRecord is not null)
            _scenarioRecord.Dirty = true;
    }

    private void EndClickDrag()
    {
        if (_clickDragIndex < 0)
            return;
        bool moved = _clickDragMoved;
        int index = _clickDragIndex;
        _clickDragIndex = -1;
        _clickDragMoved = false;
        _clicks.Cursor = Cursors.Default;
        if (_clicks.Capture)
            _clicks.Capture = false;
        if (!moved)
            return;
        _clicks.SelectedIndex = index;
        ShowSelectedClick();
        TouchRoute();
    }

    private int ClickDropIndex(SequenceBlock sequence, Point location)
    {
        if (sequence.Clicks.Count == 0)
            return -1;
        int index = _clicks.IndexFromPoint(location);
        if (index >= 0)
            return index;
        return location.Y <= _clicks.GetItemRectangle(0).Top ? 0 : sequence.Clicks.Count - 1;
    }

    private static string FormatClick(ScreenClick click, int index)
    {
        string name = string.IsNullOrWhiteSpace(click.Name) ? "" : click.Name.Trim() + "   ";
        return $"{index + 1}.  {name}{click.X}, {click.Y}   после {click.PauseMilliseconds} мс   удержание {click.HoldMilliseconds} мс";
    }

    private static string? ClickName(string text)
    {
        string trimmed = text.Trim().ReplaceLineEndings(" ");
        if (trimmed.Length > 80)
            trimmed = trimmed[..80].Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void CopyDefaultKeys(Scenario scenario)
    {
        if (_defaultKeys.Count == 0)
            return;
        foreach (ScenarioBlock block in Scenario.Walk(scenario.Blocks))
        {
            if (block is not FollowBlock follow)
                continue;
            for (int i = 1; i < follow.Anchors.Count; i++)
            {
                if (follow.Anchors[i].Keys.Count == 0)
                    follow.Anchors[i].Keys.AddRange(CloneKeys(_defaultKeys));
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);
}
