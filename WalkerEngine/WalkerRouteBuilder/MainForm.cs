using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal sealed partial class MainForm : Form
{
    private readonly WalkerMapEngine _engine = new();
    private Scenario _scenario = Scenario.CreateTemplate(2000);
    private readonly List<BlockRow> _rows = [];
    private readonly List<Scenario> _undo = [];
    private readonly List<Scenario> _redo = [];
    private int _blockRow;
    private readonly MapCanvas _canvas = new();
    private readonly ListBox _blocks = new();
    private readonly ListBox _list = new();
    private readonly CheckBox _loop = new();
    private readonly ComboBox _travel = new();
    private readonly ComboBox _repeatCondition = new();
    private readonly NumericUpDown _delay = new();
    private readonly NumericUpDown _chaseLost = new();
    private readonly ComboBox _lockTarget = new();
    private readonly ComboBox _questMarkTarget = new();
    private readonly ComboBox _useMapTarget = new();
    private readonly ComboBox _followMapTarget = new();
    private readonly NumericUpDown _radius = new();
    private readonly NumericUpDown _lockWait = new();
    private readonly NumericUpDown _repeatWait = new();
    private readonly FlowLayoutPanel _questMarkOptions = new() { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
    private readonly FlowLayoutPanel _useMapOptions = new() { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
    private readonly ListBox _clicks = new();
    private readonly NumericUpDown _clickX = new();
    private readonly NumericUpDown _clickY = new();
    private readonly NumericUpDown _clickPause = new();
    private readonly NumericUpDown _clickHold = new();
    private readonly TextBox _clickName = new()
    {
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(22, 22, 22),
        ForeColor = Color.White,
        MaxLength = 80,
    };
    private readonly Label _blockHint = new();
    private readonly Panel _followOptions = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _lockOptions = new() { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
    private readonly FlowLayoutPanel _openChestOptions = new() { Dock = DockStyle.Fill, WrapContents = false, Visible = false };
    private readonly NumericUpDown _chestWait = new();
    private readonly FlowLayoutPanel _doorOptions = new() { Dock = DockStyle.Fill, WrapContents = false, Visible = false };
    private readonly ComboBox _doorDifficulty = new();
    private readonly FlowLayoutPanel _repeatOptions = new() { Dock = DockStyle.Fill, WrapContents = false, Visible = false };
    private readonly Panel _clickRow = new() { Dock = DockStyle.Fill, Visible = false };
    private Control? _keysHost;
    private FlowLayoutPanel? _pointButtons;
    private TableLayoutPanel? _settingsPage;
    private readonly Label _settingsHint = new();
    private bool _suppressBlocks;
    private readonly Label _summary = new();
    private readonly NumericUpDown _speed = new();
    private readonly Button _play = new();
    private readonly Button _pause = new();
    private readonly Button _stop = new();
    private readonly Button _delete = new();
    private readonly Button _clear = new();
    private readonly Button _up = new();
    private readonly Button _down = new();
    private readonly Button _fromMarker = new();
    private readonly Button _follow = new();
    private readonly Button _launchGame = new();
    private readonly Button _log = new();
    private volatile bool _launchingGame;
    private CancellationTokenSource? _launchCts;
    private readonly ListBox _keys = new();
    private readonly TextBox _keyName = new();
    private readonly NumericUpDown _keyInterval = new();
    private readonly Button _addKey = new();
    private readonly Button _removeKey = new();
    private readonly Button _clearKeys = new();
    private readonly CheckBox _pressKeys = new();
    private readonly CheckBox _autoRestart = new();
    private readonly Label _keysCaption = new();
    private readonly ComboBox _goalWait = new();
    private readonly NumericUpDown _goalWaitMs = new();
    private readonly Label _goalWaitUnit = new();
    private readonly FlowLayoutPanel _goalWaitRow = new();
    private TableLayoutPanel? _keysPanel;
    private readonly List<RouteKey> _defaultKeys = [];
    private readonly ToolStripStatusLabel _status = new();
    private readonly ToolStripStatusLabel _runStats = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };

    private Bitmap? _bitmap;
    private MapBounds _bounds = MapBounds.Empty;
    private MapPoint? _detectedMarker;
    private string? _mapPath;
    private string? _routePath;
    private int _selected = -1;
    private bool _suppressList;
    private bool _dirty;
    private bool _paused;
    private bool _finished;
    private double _distance;
    private double _lastElapsed;
    private DateTimeOffset _origin;
    private readonly Stopwatch _watch = new();
    private HashSet<int> _badSegments = [];
    private CancellationTokenSource? _followCts;
    private int _followGeneration;
    private volatile bool _following;
    private bool _suppressKeys;
    private Keys _capturedKey = Keys.None;
    private int _pointListDragIndex = -1;

    public MainForm()
    {
        Text = "Конструктор сценариев";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 720);
        ClientSize = new Size(1560, 860);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.FromArgb(230, 230, 230);
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;

        var menu = BuildMenu();
        var toolbar = BuildToolbar();
        var status = BuildStatus();

        _canvas.Dock = DockStyle.Fill;
        _canvas.PointAdded += (_, point) => AddPoint(point);
        _canvas.PointInserted += (_, insert) => InsertPoint(insert);
        _canvas.PointMoved += (_, move) => MovePoint(move);
        _canvas.PointRemoveRequested += (_, index) => RemovePoint(FixedToAnchor(index));
        _canvas.SelectionChanged += (_, index) => SelectPoint(FixedToAnchor(index), center: false);
        _canvas.Hovered += (_, point) => ShowHover(point);

        Controls.Add(BuildStudio());
        Controls.Add(toolbar);
        Controls.Add(menu);
        Controls.Add(status);
        MainMenuStrip = menu;

        _timer.Tick += (_, _) => OnPlaybackTick();
        Shown += (_, _) => OnFirstShow();
        FormClosing += OnClosing;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            BackColor = Color.FromArgb(28, 28, 28),
            ForeColor = Color.White,
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
        };

        var file = new ToolStripMenuItem("Файл") { ForeColor = Color.White };
        file.DropDownItems.Add(Item("Загрузить карты...", Keys.Control | Keys.O, (_, _) => AddMaps()));
        file.DropDownItems.Add(Item("Снять карту с игровой сессии", Keys.None, (_, _) => CaptureMapFromSession()));
        file.DropDownItems.Add(Item("Новый сценарий", Keys.Control | Keys.N, (_, _) => CreateScenario(template: false)));
        file.DropDownItems.Add(Item("Новая цепочка", Keys.None, (_, _) => CreateChain()));
        file.DropDownItems.Add(Item("Загрузить сценарий...", Keys.Control | Keys.Shift | Keys.O, (_, _) => LoadScenario()));
        file.DropDownItems.Add(Item("Сохранить сценарий", Keys.Control | Keys.S, (_, _) => SaveRoute()));
        file.DropDownItems.Add(Item("Сохранить сценарий как...", Keys.None, (_, _) => SaveRouteAs()));
        file.DropDownItems.Add(Item("Удалить сценарий", Keys.None, (_, _) => DeleteSelectedScenario()));
        file.DropDownItems.Add(Item("Удалить цепочку", Keys.None, (_, _) => DeleteSelectedChain()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Загрузить маршрут...", Keys.None, (_, _) => OpenDrawnRoute()));
        file.DropDownItems.Add(Item("Экспорт маршрута...", Keys.None, (_, _) => SaveDrawnRoute()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Выход", Keys.None, (_, _) => Close()));

        var edit = new ToolStripMenuItem("Правка") { ForeColor = Color.White };
        edit.DropDownItems.Add(Item("Отменить", Keys.Control | Keys.Z, (_, _) => Undo()));
        edit.DropDownItems.Add(Item("Повторить", Keys.Control | Keys.Y, (_, _) => Redo()));
        edit.DropDownItems.Add(Item("Удалить точку", Keys.Delete, (_, _) => RemovePoint(_selected)));
        edit.DropDownItems.Add(Item("Очистить точки блока", Keys.None, (_, _) => ClearRoute()));

        var run = new ToolStripMenuItem("Прогон") { ForeColor = Color.White };
        run.DropDownItems.Add(Item("Старт", Keys.F5, (_, _) => Play()));
        run.DropDownItems.Add(Item("Пауза", Keys.None, (_, _) => Pause()));
        var stop = Item("Стоп", Keys.None, (_, _) => Stop());
        stop.ShortcutKeyDisplayString = "Esc";
        run.DropDownItems.Add(stop);
        run.DropDownItems.Add(Item("Вписать карту", Keys.None, (_, _) => _canvas.Fit()));
        run.DropDownItems.Add(new ToolStripSeparator());
        var lead = Item("Запустить сценарий", Keys.None, (_, _) => StartScenario());
        lead.ShortcutKeyDisplayString = "F6";
        var leadBlock = Item("Запустить блок", Keys.None, (_, _) => StartSelectedBlock());
        leadBlock.ShortcutKeyDisplayString = "F8";
        var leadStop = Item("Остановить ведение", Keys.None, (_, _) => StopFollow());
        leadStop.ShortcutKeyDisplayString = "F7";
        run.DropDownItems.Add(lead);
        run.DropDownItems.Add(leadBlock);
        run.DropDownItems.Add(leadStop);
        run.DropDownItems.Add(Item("Конфиг ведения...", Keys.None, (_, _) => OpenFollowConfig()));
        run.DropDownItems.Add(Item("Застревание...", Keys.None, (_, _) => OpenStuckRecovery()));
        run.DropDownItems.Add(new ToolStripSeparator());
        _historyMenuItem = Item("История запусков", Keys.None, (_, _) => ToggleRunHistoryPanel());
        _historyMenuItem.Checked = true;
        _historyMenuItem.CheckOnClick = false;
        run.DropDownItems.Add(_historyMenuItem);

        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(run);
        return menu;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text, null, onClick)
        {
            ForeColor = Color.White,
            ShortcutKeys = shortcut,
            BackColor = Color.FromArgb(28, 28, 28),
        };
        return item;
    }

    private FlowLayoutPanel BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(8, 8, 8, 4),
            BackColor = Color.FromArgb(28, 28, 28),
            WrapContents = false,
            AutoScroll = true,
        };

        bar.Controls.Add(Command("Сохранить", (_, _) => SaveRoute()));
        _follow.Text = "Сценарий  F6";
        _runBlock.Text = "Блок  F8";
        _stopFollow.Text = "Стоп  F7";
        StyleButton(_follow);
        StyleButton(_runBlock);
        StyleButton(_stopFollow);
        _launchGame.Text = "Запуск игры";
        StyleButton(_launchGame);
        _follow.Click += (_, _) => StartScenario();
        _runBlock.Click += (_, _) => StartSelectedBlock();
        _stopFollow.Click += (_, _) => StopFollow();
        _launchGame.Click += (_, _) => LaunchGame();
        bar.Controls.Add(_follow);
        bar.Controls.Add(_runBlock);
        bar.Controls.Add(_stopFollow);
        bar.Controls.Add(_launchGame);
        bar.Controls.Add(BuildLogButton());

        _loop.Text = "Зациклить";
        _loop.AutoSize = true;
        _loop.ForeColor = Color.FromArgb(230, 230, 230);
        _loop.Margin = new Padding(16, 8, 8, 0);
        _loop.CheckedChanged += (_, _) =>
        {
            if (_suppressBlocks)
                return;
            if (ViewingChainOverview && _chainRecord is not null)
            {
                _chainRecord.Document.Loop = _loop.Checked;
                TouchChain();
                return;
            }

            if (_scenarioRecord is null)
                return;
            PushUndo();
            _scenario.Loop = _loop.Checked;
            TouchRoute();
        };
        _pressKeys.Margin = new Padding(4, 8, 8, 0);
        _autoRestart.AutoSize = true;
        _autoRestart.Text = "Автоперезапуск";
        _autoRestart.Margin = new Padding(4, 8, 12, 0);
        _autoRestart.ForeColor = Color.FromArgb(230, 230, 230);
        _autoRestart.CheckedChanged += (_, _) =>
        {
            if (_suppressKeys || _suppressBlocks)
                return;
            PersistFollowUiFlags();
            if (_autoRestart.Checked && !RestartArmedInConfig())
            {
                _status.Text =
                    "Автоперезапуск включён, но нет executablePath — укажите путь в follow-config или secrets.";
            }
            else
            {
                _status.Text = _autoRestart.Checked
                    ? "Автоперезапуск при потере миникарты включён."
                    : "Автоперезапуск выключен.";
            }
        };
        bar.Controls.Add(_loop);
        bar.Controls.Add(_pressKeys);
        bar.Controls.Add(_autoRestart);

        _play.Click += (_, _) => Play();
        _pause.Click += (_, _) => Pause();
        _stop.Click += (_, _) => Stop();

        var speedLabel = new Label
        {
            Text = "Скорость, px/с",
            AutoSize = true,
            Margin = new Padding(12, 8, 4, 0),
            ForeColor = Color.FromArgb(210, 210, 210),
        };
        _speed.Minimum = 20;
        _speed.Maximum = 800;
        _speed.Value = 140;
        _speed.Increment = 10;
        _speed.Width = 70;
        _speed.Margin = new Padding(0, 4, 0, 0);
        bar.Controls.Add(speedLabel);
        bar.Controls.Add(_speed);
        return bar;
    }

    private Button BuildLogButton()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
            BackColor = Color.FromArgb(28, 28, 28),
            ForeColor = Color.White,
            ShowImageMargin = false,
        };
        var text = new ToolStripMenuItem("Текст")
        {
            Checked = FileLog.TextEnabled,
            CheckOnClick = true,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(28, 28, 28),
        };
        var shots = new ToolStripMenuItem("Снимки")
        {
            Checked = FileLog.ShotsEnabled,
            CheckOnClick = true,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(28, 28, 28),
        };
        text.CheckedChanged += (_, _) => ApplyLogChoice(text.Checked, shots.Checked);
        shots.CheckedChanged += (_, _) => ApplyLogChoice(text.Checked, shots.Checked);
        menu.Items.Add(text);
        menu.Items.Add(shots);

        _log.Text = FileLog.Caption;
        StyleButton(_log);
        _log.Click += (_, _) => menu.Show(_log, new Point(0, _log.Height));
        return _log;
    }

    private void ApplyLogChoice(bool text, bool shots)
    {
        FileLog.Configure(text, shots);
        _log.Text = FileLog.Caption;
        _status.Text = FileLog.Caption;
    }

    private void BuildSettingsWindow()
    {
        _settingsPage = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 32, 32),
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5,
        };
        _settingsPage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _settingsPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        _settingsPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        _settingsPage.RowStyles.Add(new RowStyle(SizeType.Percent, 2));
        _settingsPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 204));
        _settingsPage.RowStyles.Add(new RowStyle(SizeType.Percent, 1));

        _settingsHint.Dock = DockStyle.Fill;
        _settingsHint.AutoSize = false;
        _settingsHint.ForeColor = Color.FromArgb(176, 176, 176);
        _settingsHint.Text = "Параметры блока. У «Следовать маршруту» карта и точки задаются здесь — превью снизу.";

        var blockOptions = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        _travel.DropDownStyle = ComboBoxStyle.DropDownList;
        _travel.Width = 180;
        _travel.Margin = new Padding(0, 2, 12, 0);
        _travel.Items.AddRange(["По векторам", "Кратчайший путь"]);
        _travel.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedFollow is not FollowBlock follow)
                return;
            var mode = _travel.SelectedIndex == 1 ? TravelMode.Shortest : TravelMode.Vectors;
            if (follow.Travel == mode)
                return;
            PushUndo();
            follow.Travel = mode;
            TouchRoute();
        };
        _delay.Minimum = 0;
        _delay.Maximum = 120000;
        _delay.Increment = 500;
        _delay.Width = 80;
        _delay.Margin = new Padding(0, 2, 8, 0);
        _delay.ValueChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedFollow is not FollowBlock follow)
                return;
            if (follow.DelayMilliseconds == (int)_delay.Value)
                return;
            follow.DelayMilliseconds = (int)_delay.Value;
            _dirty = true;
        };
        _chaseLost.Minimum = 0;
        _chaseLost.Maximum = 120000;
        _chaseLost.Increment = 100;
        _chaseLost.Width = 80;
        _chaseLost.Margin = new Padding(0, 2, 8, 0);
        _chaseLost.ValueChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedFollow is not FollowBlock follow)
                return;
            if (follow.ChaseLostMilliseconds == (int)_chaseLost.Value)
                return;
            PushUndo();
            follow.ChaseLostMilliseconds = (int)_chaseLost.Value;
            TouchRoute();
        };
        _lockTarget.DropDownStyle = ComboBoxStyle.DropDownList;
        _lockTarget.Width = 120;
        _lockTarget.Margin = new Padding(0, 2, 12, 0);
        _lockTarget.Items.AddRange(["Красный", "Сундук"]);
        _lockTarget.SelectedIndex = 0;
        _lockTarget.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedLock is not LockBlock locked)
                return;
            var target = _lockTarget.SelectedIndex == 1 ? AnchorSource.Chest : AnchorSource.Skull;
            if (locked.Target == target)
                return;
            locked.Target = target;
            _dirty = true;
            RebuildBlocks();
        };
        _questMarkTarget.DropDownStyle = ComboBoxStyle.DropDownList;
        _questMarkTarget.Width = 160;
        _questMarkTarget.Margin = new Padding(0, 2, 12, 0);
        _questMarkTarget.Items.AddRange(["Знак вопроса", "Знак восклицания"]);
        _questMarkTarget.SelectedIndex = 0;
        _questMarkTarget.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBlocks || CurrentBlock is not ClickQuestMarkBlock quest)
                return;
            var mark = _questMarkTarget.SelectedIndex == 1 ? AnchorSource.QuestExclaim : AnchorSource.QuestQuestion;
            if (quest.Mark == mark)
                return;
            quest.Mark = mark;
            _dirty = true;
            RebuildBlocks();
        };
        _radius.Minimum = 0;
        _radius.Maximum = 400;
        _radius.Increment = 4;
        _radius.Width = 70;
        _radius.Margin = new Padding(0, 2, 8, 0);
        _radius.ValueChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedLock is not LockBlock locked)
                return;
            if (locked.RadiusPixels == (int)_radius.Value)
                return;
            locked.RadiusPixels = (int)_radius.Value;
            _dirty = true;
            if (_scenarioRecord is not null)
                _scenarioRecord.Dirty = true;
        };
        _lockWait.Minimum = 0;
        _lockWait.Maximum = 120000;
        _lockWait.Increment = 500;
        _lockWait.Width = 80;
        _lockWait.Margin = new Padding(0, 2, 8, 0);
        _lockWait.ValueChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedLock is not LockBlock locked)
                return;
            if (locked.WaitMilliseconds == (int)_lockWait.Value)
                return;
            locked.WaitMilliseconds = (int)_lockWait.Value;
            _dirty = true;
            if (_scenarioRecord is not null)
                _scenarioRecord.Dirty = true;
            RebuildBlocks();
        };
        _followOptions.BackColor = Color.FromArgb(32, 32, 32);
        _followOptions.Dock = DockStyle.Fill;
        _followMapTarget.DropDownStyle = ComboBoxStyle.DropDownList;
        _followMapTarget.Width = 120;
        _followMapTarget.Margin = new Padding(0, 2, 0, 0);
        _followMapTarget.SelectedIndexChanged += (_, _) => OnFollowMapChanged();
        _travel.Width = 140;
        var followRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            WrapContents = false,
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0),
            BackColor = Color.FromArgb(32, 32, 32),
        };
        followRow.Controls.Add(_travel);
        followRow.Controls.Add(InlineLabel("Пауза, мс"));
        followRow.Controls.Add(_delay);
        followRow.Controls.Add(InlineLabel("Пропажа в пути, мс"));
        followRow.Controls.Add(_chaseLost);
        followRow.Controls.Add(InlineLabel("Карта"));
        followRow.Controls.Add(_followMapTarget);
        _tips.SetToolTip(_chaseLost, "Сколько можно не видеть live-цель (череп/сундук/знак) уже в пути. 0 — бежать до точки даже если значок пропал.");

        var loadRoute = new Button { Text = "Загрузить маршрут" };
        var exportRoute = new Button { Text = "Экспорт маршрута" };
        StyleButton(loadRoute);
        StyleButton(exportRoute);
        loadRoute.AutoSize = true;
        exportRoute.AutoSize = true;
        loadRoute.Margin = new Padding(0, 2, 8, 0);
        exportRoute.Margin = new Padding(0, 2, 0, 0);
        loadRoute.Click += (_, _) => OpenDrawnRoute();
        exportRoute.Click += (_, _) => SaveDrawnRoute();
        _tips.SetToolTip(loadRoute, "Подставить сохранённый .json маршрут в этот блок «Следовать маршруту»");
        _tips.SetToolTip(exportRoute, "Сохранить точки текущего блока в файл маршрута");
        var routeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            WrapContents = false,
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0),
            BackColor = Color.FromArgb(32, 32, 32),
        };
        routeRow.Controls.Add(loadRoute);
        routeRow.Controls.Add(exportRoute);
        // Dock Top: последний добавленный оказывается сверху.
        _followOptions.Controls.Add(routeRow);
        _followOptions.Controls.Add(followRow);
        _lockOptions.BackColor = Color.FromArgb(32, 32, 32);
        _lockOptions.AutoSize = false;
        _lockOptions.Dock = DockStyle.Fill;
        _lockOptions.WrapContents = true;
        _lockOptions.Controls.Add(InlineLabel("Цель"));
        _lockOptions.Controls.Add(_lockTarget);
        _lockOptions.Controls.Add(InlineLabel("Радиус, px"));
        _lockOptions.Controls.Add(_radius);
        _lockOptions.Controls.Add(InlineLabel("Ожидание, мс"));
        _lockOptions.Controls.Add(_lockWait);
        _tips.SetToolTip(_lockWait, "Сколько ждать появления цели и подтверждения её исчезновения");
        _questMarkOptions.BackColor = Color.FromArgb(32, 32, 32);
        _questMarkOptions.AutoSize = false;
        _questMarkOptions.Dock = DockStyle.Fill;
        _questMarkOptions.WrapContents = false;
        _questMarkOptions.Visible = false;
        _questMarkOptions.Controls.Add(InlineLabel("Знак"));
        _questMarkOptions.Controls.Add(_questMarkTarget);
        _useMapTarget.DropDownStyle = ComboBoxStyle.DropDownList;
        _useMapTarget.Width = 220;
        _useMapTarget.Margin = new Padding(0, 2, 12, 0);
        _useMapTarget.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBlocks || CurrentBlock is not UseMapBlock useMap)
                return;
            if (_useMapTarget.SelectedItem is not MapChoice choice)
                return;
            if (string.Equals(useMap.MapFile, choice.Path, StringComparison.OrdinalIgnoreCase))
                return;
            useMap.MapFile = choice.Path;
            _dirty = true;
            if (_scenarioRecord is not null)
                _scenarioRecord.Dirty = true;
            RebuildBlocks();
        };
        _useMapOptions.BackColor = Color.FromArgb(32, 32, 32);
        _useMapOptions.AutoSize = false;
        _useMapOptions.Dock = DockStyle.Fill;
        _useMapOptions.WrapContents = false;
        _useMapOptions.Visible = false;
        _useMapOptions.Controls.Add(InlineLabel("Карта"));
        _useMapOptions.Controls.Add(_useMapTarget);
        _repeatCondition.DropDownStyle = ComboBoxStyle.DropDownList;
        _repeatCondition.Width = 220;
        _repeatCondition.Margin = new Padding(0, 2, 0, 0);
        _repeatCondition.Items.Add("Пока есть красный");
        _repeatCondition.SelectedIndex = 0;
        _repeatCondition.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedRepeat is not RepeatBlock repeat)
                return;
            if (_repeatCondition.SelectedIndex != 0 || repeat.Condition == RepeatCondition.WhileRed)
                return;
            PushUndo();
            repeat.Condition = RepeatCondition.WhileRed;
            TouchRoute();
        };
        _repeatWait.Minimum = 0;
        _repeatWait.Maximum = 120000;
        _repeatWait.Increment = 500;
        _repeatWait.Width = 80;
        _repeatWait.Margin = new Padding(0, 2, 8, 0);
        _repeatWait.ValueChanged += (_, _) =>
        {
            if (_suppressBlocks || EditedRepeat is not RepeatBlock repeat)
                return;
            if (repeat.WaitMilliseconds == (int)_repeatWait.Value)
                return;
            PushUndo();
            repeat.WaitMilliseconds = (int)_repeatWait.Value;
            TouchRoute();
        };
        _repeatOptions.BackColor = Color.FromArgb(32, 32, 32);
        _repeatOptions.AutoSize = false;
        _repeatOptions.Dock = DockStyle.Fill;
        _repeatOptions.WrapContents = false;
        _repeatOptions.Controls.Add(InlineLabel("Условие"));
        _repeatOptions.Controls.Add(_repeatCondition);
        _repeatOptions.Controls.Add(InlineLabel("Ожидание, мс"));
        _repeatOptions.Controls.Add(_repeatWait);
        _tips.SetToolTip(_repeatWait, "Сколько ждать первое появление красного. 0 — не ждать, сразу пропустить если нет.");
        _chestWait.Minimum = 0;
        _chestWait.Maximum = 120000;
        _chestWait.Increment = 500;
        _chestWait.Width = 80;
        _chestWait.Margin = new Padding(0, 2, 8, 0);
        _chestWait.ValueChanged += (_, _) =>
        {
            if (_suppressBlocks || CurrentBlock is not OpenChestBlock chest)
                return;
            if (chest.WaitMilliseconds == (int)_chestWait.Value)
                return;
            PushUndo();
            chest.WaitMilliseconds = (int)_chestWait.Value;
            TouchRoute();
        };
        _openChestOptions.BackColor = Color.FromArgb(32, 32, 32);
        _openChestOptions.AutoSize = false;
        _openChestOptions.Dock = DockStyle.Fill;
        _openChestOptions.WrapContents = false;
        _openChestOptions.Controls.Add(InlineLabel("Ожидание, мс"));
        _openChestOptions.Controls.Add(_chestWait);
        _tips.SetToolTip(_chestWait, "Сколько ждать появление сундука и подтверждение исчезновения после открытия.");
        _doorDifficulty.DropDownStyle = ComboBoxStyle.DropDownList;
        _doorDifficulty.Width = 160;
        _doorDifficulty.Margin = new Padding(0, 2, 8, 0);
        _doorDifficulty.Items.AddRange(DoorDifficulties.Names);
        _doorDifficulty.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressBlocks || CurrentBlock is not DoorBlock door)
                return;
            int value = _doorDifficulty.SelectedIndex + 1;
            if (door.Difficulty == value)
                return;
            PushUndo();
            door.Difficulty = value;
            TouchRoute();
        };
        _doorOptions.BackColor = Color.FromArgb(32, 32, 32);
        _doorOptions.AutoSize = false;
        _doorOptions.Dock = DockStyle.Fill;
        _doorOptions.WrapContents = false;
        _doorOptions.Controls.Add(InlineLabel("Сложность"));
        _doorOptions.Controls.Add(_doorDifficulty);
        _tips.SetToolTip(_doorDifficulty, "Клик по шкале сложности в окне входа перед кнопкой Enter.");
        blockOptions.Controls.Add(_followOptions);
        blockOptions.Controls.Add(_lockOptions);
        blockOptions.Controls.Add(_openChestOptions);
        blockOptions.Controls.Add(_doorOptions);
        blockOptions.Controls.Add(_questMarkOptions);
        blockOptions.Controls.Add(_useMapOptions);
        blockOptions.Controls.Add(_repeatOptions);
        ConfigureClickEditor(blockOptions);

        _pointButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0),
            WrapContents = true,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        _delete.Text = "Удалить";
        _clear.Text = "Очистить";
        _up.Text = "Раньше";
        _down.Text = "Позже";
        _fromMarker.Text = "Старт с маркера";
        var playerPoint = PointButton("Персонаж", AnchorSource.Player);
        var skullPoint = PointButton("Красный", AnchorSource.Skull);
        var chestPoint = PointButton("Сундук", AnchorSource.Chest);
        var questionPoint = PointButton("Вопрос", AnchorSource.QuestQuestion);
        var exclaimPoint = PointButton("Восклицание", AnchorSource.QuestExclaim);
        var startPoint = PointButton("Старт маршрута", AnchorSource.RouteStart);
        var loadRoutePoints = new Button { Text = "Загрузить маршрут" };
        var exportRoutePoints = new Button { Text = "Экспорт маршрута" };
        foreach (var button in new[] { playerPoint, skullPoint, chestPoint, questionPoint, exclaimPoint, startPoint, _delete, _clear, _up, _down, _fromMarker, loadRoutePoints, exportRoutePoints })
        {
            StyleButton(button, roomy: true);
            _pointButtons.Controls.Add(button);
        }

        loadRoutePoints.Click += (_, _) => OpenDrawnRoute();
        exportRoutePoints.Click += (_, _) => SaveDrawnRoute();
        _tips.SetToolTip(loadRoutePoints, "Подставить сохранённый .json маршрут в этот блок");
        _tips.SetToolTip(exportRoutePoints, "Сохранить точки текущего блока в файл маршрута");

        _delete.Click += (_, _) => RemovePoint(_selected);
        _clear.Click += (_, _) => ClearRoute();
        _up.Click += (_, _) => ShiftSelected(-1);
        _down.Click += (_, _) => ShiftSelected(1);
        _fromMarker.Click += (_, _) => StartFromMarker();
        _tips.SetToolTip(_up, "Сдвинуть точку раньше по маршруту (Ctrl+↑)");
        _tips.SetToolTip(_down, "Сдвинуть точку позже по маршруту (Ctrl+↓)");
        _tips.SetToolTip(_list, "Перетащите строку, чтобы изменить порядок. Клик по карте с выбранной точкой — вставка после неё; клик по ребру — вставка между.");

        _list.Dock = DockStyle.Fill;
        _list.BackColor = Color.FromArgb(22, 22, 22);
        _list.ForeColor = Color.FromArgb(230, 230, 230);
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.Font = new Font("Consolas", 10f);
        _list.IntegralHeight = false;
        _list.HorizontalScrollbar = true;
        _list.AllowDrop = true;
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (!_suppressList)
                SelectPoint(_list.SelectedIndex, center: false);
        };
        _list.MouseDown += OnPointListMouseDown;
        _list.MouseMove += OnPointListMouseMove;
        _list.DragOver += OnPointListDragOver;
        _list.DragDrop += OnPointListDragDrop;
        _list.DoubleClick += (_, _) =>
        {
            if (EditedFollow is FollowBlock follow && _selected >= 0 && _selected < follow.Anchors.Count
                && follow.Anchors[_selected].Source == AnchorSource.Fixed)
                _canvas.CenterOn(follow.Anchors[_selected].Position);
        };

        _clicks.Dock = DockStyle.Fill;
        _clicks.BackColor = Color.FromArgb(22, 22, 22);
        _clicks.ForeColor = Color.FromArgb(230, 230, 230);
        _clicks.BorderStyle = BorderStyle.FixedSingle;
        _clicks.IntegralHeight = false;
        _clicks.SelectedIndexChanged += (_, _) => ShowSelectedClick();

        var anchorHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(32, 32, 32) };
        anchorHost.Controls.Add(_clicks);
        anchorHost.Controls.Add(_list);

        _keysHost = BuildKeysPanel();
        _settingsPage.Controls.Add(_settingsHint, 0, 0);
        _settingsPage.Controls.Add(blockOptions, 0, 1);
        _settingsPage.Controls.Add(anchorHost, 0, 2);
        _settingsPage.Controls.Add(_pointButtons, 0, 3);
        _settingsPage.Controls.Add(_keysHost, 0, 4);
    }

    private Control BuildKeysPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 32, 32),
            Padding = new Padding(0, 4, 0, 0),
            ColumnCount = 1,
            RowCount = 5,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        _keysPanel = panel;

        _goalWait.DropDownStyle = ComboBoxStyle.DropDownList;
        _goalWait.Width = 180;
        _goalWait.DropDownWidth = 220;
        _goalWait.Margin = new Padding(0, 4, 8, 0);
        _goalWait.Items.AddRange(["До появления цели", "Не ожидать", "Установить время ожидания"]);
        _goalWait.SelectedIndex = (int)GoalWait.Timed;
        _goalWait.SelectedIndexChanged += (_, _) => ApplyGoalWait();
        _goalWaitMs.Minimum = 100;
        _goalWaitMs.Maximum = 120000;
        _goalWaitMs.Increment = 100;
        _goalWaitMs.Value = 2000;
        _goalWaitMs.Width = 72;
        _goalWaitMs.Margin = new Padding(0, 2, 4, 0);
        _goalWaitMs.Visible = false;
        _goalWaitMs.ValueChanged += (_, _) => ApplyGoalWait();
        _goalWaitUnit.Text = "мс";
        _goalWaitUnit.AutoSize = true;
        _goalWaitUnit.Margin = new Padding(0, 8, 0, 0);
        _goalWaitUnit.ForeColor = Color.FromArgb(210, 210, 210);
        _goalWaitUnit.Visible = false;
        _goalWaitRow.Dock = DockStyle.Fill;
        _goalWaitRow.Height = 36;
        _goalWaitRow.WrapContents = false;
        _goalWaitRow.Visible = false;
        _goalWaitRow.BackColor = Color.FromArgb(32, 32, 32);
        _goalWaitRow.Controls.Add(_goalWait);
        _goalWaitRow.Controls.Add(_goalWaitMs);
        _goalWaitRow.Controls.Add(_goalWaitUnit);

        _keysCaption.AutoSize = true;
        _keysCaption.Margin = new Padding(0, 4, 12, 0);
        _keysCaption.Text = "Выберите вектор";
        _keysCaption.Font = new Font(Font, FontStyle.Bold);
        _pressKeys.AutoSize = true;
        _pressKeys.Text = "Нажимать клавиши";
        _pressKeys.Checked = true;
        _pressKeys.Margin = new Padding(4, 8, 12, 0);
        _pressKeys.ForeColor = Color.FromArgb(230, 230, 230);
        _pressKeys.CheckedChanged += (_, _) =>
        {
            if (!_suppressKeys)
                SaveRouteKeys();
        };
        var title = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 28,
            WrapContents = false,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        title.Controls.Add(_keysCaption);

        _keyName.Width = 88;
        _keyName.ReadOnly = true;
        _keyName.PlaceholderText = "клавиша";
        _keyName.BackColor = Color.FromArgb(22, 22, 22);
        _keyName.ForeColor = Color.White;
        _keyName.BorderStyle = BorderStyle.FixedSingle;
        _keyName.Margin = new Padding(0, 4, 6, 0);
        _keyName.KeyDown += (_, e) => CaptureRouteKey(e);

        _keyInterval.Minimum = 50;
        _keyInterval.Maximum = 120000;
        _keyInterval.Value = 1000;
        _keyInterval.Increment = 100;
        _keyInterval.Width = 72;
        _keyInterval.Margin = new Padding(0, 2, 4, 0);
        _keyInterval.ValueChanged += (_, _) => UpdateSelectedKeyInterval();

        var intervalLabel = new Label
        {
            Text = "мс",
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0),
            ForeColor = Color.FromArgb(210, 210, 210),
        };

        _addKey.Text = "Добавить";
        _removeKey.Text = "Удалить";
        _clearKeys.Text = "Очистить";
        StyleButton(_addKey, roomy: true);
        StyleButton(_removeKey, roomy: true);
        StyleButton(_clearKeys, roomy: true);
        _addKey.Click += (_, _) => AddRouteKey();
        _removeKey.Click += (_, _) => RemoveRouteKey();
        _clearKeys.Click += (_, _) => ClearRouteKeys();

        var editor = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 52,
            WrapContents = false,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        editor.Controls.Add(_keyName);
        editor.Controls.Add(_keyInterval);
        editor.Controls.Add(intervalLabel);
        editor.Controls.Add(_addKey);

        _keys.Dock = DockStyle.Fill;
        _keys.BackColor = Color.FromArgb(22, 22, 22);
        _keys.ForeColor = Color.FromArgb(230, 230, 230);
        _keys.BorderStyle = BorderStyle.FixedSingle;
        _keys.Font = new Font("Consolas", 10f);
        _keys.IntegralHeight = false;
        _keys.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressKeys)
                return;
            SyncKeyEditor();
            UpdateButtons();
        };

        var removeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 48,
            WrapContents = false,
            BackColor = Color.FromArgb(32, 32, 32),
        };
        removeRow.Controls.Add(_removeKey);
        removeRow.Controls.Add(_clearKeys);

        panel.Controls.Add(_goalWaitRow, 0, 0);
        panel.Controls.Add(title, 0, 1);
        panel.Controls.Add(editor, 0, 2);
        panel.Controls.Add(_keys, 0, 3);
        panel.Controls.Add(removeRow, 0, 4);
        return panel;
    }

    private void CaptureRouteKey(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu)
            return;
        if (e.KeyCode is Keys.F6 or Keys.F7 or Keys.F9)
        {
            _status.Text = e.KeyCode == Keys.F9
                ? "F9 снимает точку блока «Нажатия»."
                : "F6 и F7 заняты ведением.";
            return;
        }

        _capturedKey = e.KeyCode;
        _keyName.Text = FormatKey(e.KeyCode);
        AddRouteKey();
    }

    private void AddRouteKey()
    {
        List<RouteKey>? segment = SelectedSegmentKeys();
        if (segment is null)
        {
            _status.Text = "Клавиши задаются на вектор. Выберите в списке точку со второй.";
            return;
        }

        if (_capturedKey == Keys.None)
        {
            _status.Text = "Щёлкните поле «клавиша» и нажмите нужную клавишу.";
            return;
        }

        int interval = (int)_keyInterval.Value;
        int existing = segment.FindIndex(item => item.Key == _capturedKey);
        if (existing >= 0 && segment[existing].Interval == interval)
        {
            _keys.SelectedIndex = existing;
            return;
        }

        var binding = new RouteKey(_capturedKey, interval);
        if (existing >= 0)
            segment[existing] = binding;
        else
            segment.Add(binding);

        TouchKeys();
        _keys.SelectedIndex = segment.FindIndex(item => item.Key == _capturedKey);
        _status.Text = EditedLock is not null
            ? $"На фиксации {FormatKey(_capturedKey)} каждые {interval} мс."
            : $"На векторе к точке {_selected + 1} {FormatKey(_capturedKey)} каждые {interval} мс.";
    }

    private void RemoveRouteKey()
    {
        List<RouteKey>? segment = SelectedSegmentKeys();
        int index = _keys.SelectedIndex;
        if (segment is null || index < 0 || index >= segment.Count)
            return;

        segment.RemoveAt(index);
        TouchKeys();
    }

    private void ClearRouteKeys()
    {
        List<RouteKey>? segment = SelectedSegmentKeys();
        if (segment is null || segment.Count == 0)
            return;

        segment.Clear();
        TouchKeys();
        _status.Text = $"Клавиши вектора к точке {_selected + 1} очищены.";
    }

    private void LoadRouteKeys()
    {
        _defaultKeys.Clear();
        try
        {
            FollowConfig config = FollowConfig.LoadOrCreate(FollowConfigPath);
            foreach (KeyPressConfig item in config.Keys)
            {
                if (TryParseRouteKey(item, out RouteKey key))
                    _defaultKeys.Add(key);
            }

            _suppressKeys = true;
            _suppressBlocks = true;
            _pressKeys.Checked = config.PressKeys;
            _scenario.PressKeys = config.PressKeys;
            _autoRestart.Checked = config.Restart.Enabled;
            _suppressBlocks = false;
            _suppressKeys = false;
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }

        ShowRouteKeys();
    }

    private void SaveRouteKeys()
        => PersistFollowUiFlags();

    private void PersistFollowUiFlags()
    {
        try
        {
            FollowConfig config = FollowConfig.LoadOrCreate(FollowConfigPath);
            config.PressKeys = _pressKeys.Checked;
            config.Restart.Enabled = _autoRestart.Checked;
            _scenario.PressKeys = _pressKeys.Checked;
            config.Save(FollowConfigPath);
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
    }

    private static bool RestartArmedInConfig()
    {
        try
        {
            FollowConfig config = FollowConfig.LoadOrCreate(FollowConfigPath);
            return !string.IsNullOrWhiteSpace(config.Restart.ExecutablePath);
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSelectedKeyInterval()
    {
        if (_suppressKeys)
            return;

        List<RouteKey>? segment = SelectedSegmentKeys();
        int index = _keys.SelectedIndex;
        if (segment is null || index < 0 || index >= segment.Count)
            return;

        int interval = (int)_keyInterval.Value;
        if (segment[index].Interval == interval)
            return;

        segment[index] = segment[index] with { Interval = interval };
        TouchKeys();
        _suppressKeys = true;
        _keys.SelectedIndex = index;
        _suppressKeys = false;
    }

    private void SyncKeyEditor()
    {
        List<RouteKey>? segment = SelectedSegmentKeys();
        int index = _keys.SelectedIndex;
        if (segment is null || index < 0 || index >= segment.Count)
            return;

        _suppressKeys = true;
        _capturedKey = segment[index].Key;
        _keyName.Text = FormatKey(_capturedKey);
        _keyInterval.Value = Math.Clamp(segment[index].Interval, (int)_keyInterval.Minimum, (int)_keyInterval.Maximum);
        _suppressKeys = false;
    }

    private void ShowRouteKeys()
    {
        List<RouteKey>? segment = SelectedSegmentKeys();
        _keysCaption.Text = EditedLock is not null
            ? "Клавиши фиксации"
            : segment is null ? "Выберите вектор" : $"Вектор к точке {_selected + 1}";
        _suppressKeys = true;
        _keys.BeginUpdate();
        _keys.Items.Clear();
        if (segment is not null)
        {
            foreach (RouteKey item in segment)
                _keys.Items.Add($"{FormatKey(item.Key)}    {item.Interval} мс");
        }

        _keys.EndUpdate();
        _suppressKeys = false;
        ShowGoalWait();
        UpdateButtons();
    }

    private Anchor? SelectedConcreteAnchor()
    {
        if (EditedFollow is not FollowBlock follow || _selected < 0 || _selected >= follow.Anchors.Count)
            return null;
        Anchor anchor = follow.Anchors[_selected];
        return anchor.Source is AnchorSource.Skull or AnchorSource.Chest
            or AnchorSource.QuestQuestion or AnchorSource.QuestExclaim
            ? anchor
            : null;
    }

    private void ShowGoalWait()
    {
        Anchor? anchor = SelectedConcreteAnchor();
        bool show = anchor is not null;
        _goalWaitRow.Visible = show;
        if (_keysPanel is not null)
            _keysPanel.RowStyles[0].Height = show ? 36 : 0;
        if (anchor is null)
            return;

        _suppressKeys = true;
        _goalWait.SelectedIndex = (int)anchor.Wait;
        int ms = Math.Clamp(anchor.WaitMilliseconds, (int)_goalWaitMs.Minimum, (int)_goalWaitMs.Maximum);
        _goalWaitMs.Value = ms;
        bool timed = anchor.Wait == GoalWait.Timed;
        _goalWaitMs.Visible = timed;
        _goalWaitUnit.Visible = timed;
        _suppressKeys = false;
    }

    private void ApplyGoalWait()
    {
        if (_suppressKeys)
            return;
        Anchor? anchor = SelectedConcreteAnchor();
        if (anchor is null || _goalWait.SelectedIndex < 0)
            return;

        var wait = (GoalWait)_goalWait.SelectedIndex;
        bool timed = wait == GoalWait.Timed;
        _goalWaitMs.Visible = timed;
        _goalWaitUnit.Visible = timed;
        int ms = (int)_goalWaitMs.Value;
        if (anchor.Wait == wait && (!timed || anchor.WaitMilliseconds == ms))
            return;

        anchor.Wait = wait;
        if (timed)
            anchor.WaitMilliseconds = ms;
        _dirty = true;
        if (_scenarioRecord is not null)
            _scenarioRecord.Dirty = true;
        RebuildList();
    }

    private void TouchKeys()
    {
        _dirty = true;
        ShowRouteKeys();
        RebuildList();
    }

    private List<RouteKey>? SelectedSegmentKeys()
    {
        if (EditedLock is LockBlock locked)
            return locked.Keys;
        if (EditedFollow is FollowBlock follow && _selected > 0 && _selected < follow.Anchors.Count)
            return follow.Anchors[_selected].Keys;
        return null;
    }

    private static bool TryParseRouteKey(KeyPressConfig item, out RouteKey key)
    {
        key = new RouteKey(Keys.None, 1000);
        if (string.IsNullOrWhiteSpace(item.Key) || !Enum.TryParse(item.Key, ignoreCase: true, out Keys parsed) || parsed == Keys.None)
            return false;
        if (parsed is Keys.F6 or Keys.F7 or Keys.F9)
            return false;

        key = new RouteKey(parsed, Math.Clamp(item.IntervalMilliseconds, 50, 120_000));
        return true;
    }

    private static bool TryParseRouteKey(RouteKeyDto item, out RouteKey key)
        => TryParseRouteKey(new KeyPressConfig { Key = item.Key, IntervalMilliseconds = item.IntervalMilliseconds }, out key);

    private List<RouteKey> CloneKeys(IReadOnlyList<RouteKey> source) => [.. source];

    private static string FormatKey(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9)
            return ((int)(key - Keys.D0)).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return "Num" + (int)(key - Keys.NumPad0);
        return key.ToString();
    }

    private StatusStrip BuildStatus()
    {
        var strip = new StatusStrip
        {
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.FromArgb(220, 220, 220),
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
            SizingGrip = false,
        };
        _status.Spring = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = Color.FromArgb(220, 220, 220);
        _status.Text = "Откройте карту локации.";
        _runStats.Spring = false;
        _runStats.TextAlign = ContentAlignment.MiddleRight;
        _runStats.ForeColor = Color.FromArgb(180, 190, 200);
        _runStats.Text = "";
        strip.Items.Add(_status);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(_runStats);
        return strip;
    }

    private void ShowRunProgress(RunProgress progress)
    {
        string lapLabel = progress.Looping ? $"Круг {progress.Lap}" : "Проход";
        string current = FormatRunSpan(progress.LapElapsed);
        string total = FormatRunSpan(progress.TotalElapsed);
        string counts = FormatLapCounts(progress.ChestsOpened, progress.RedsKilled, progress.Skipped);
        string last = progress.LastLap is LapRecord done
            ? $" · прошлый {FormatRunSpan(done.Duration)}"
            : "";
        _runStats.Text = $"{lapLabel} · {current} · всего {total} · {counts}{last}";

        int sumChests = progress.ChestsOpened;
        int sumReds = progress.RedsKilled;
        int sumSkipped = progress.Skipped;
        var lines = new List<string>();
        for (int i = 0; i < progress.Laps.Count; i++)
        {
            LapRecord lap = progress.Laps[i];
            sumChests += lap.ChestsOpened;
            sumReds += lap.RedsKilled;
            sumSkipped += lap.Skipped;
            lines.Add($"{i + 1}: {FormatRunSpan(lap.Duration)} · {FormatLapCounts(lap.ChestsOpened, lap.RedsKilled, lap.Skipped)}");
        }

        if (progress.Laps.Count > 0 || progress.ChestsOpened + progress.RedsKilled + progress.Skipped > 0)
            lines.Add($"Итого: {FormatLapCounts(sumChests, sumReds, sumSkipped)}");
        _runStats.ToolTipText = lines.Count > 0
            ? string.Join(Environment.NewLine, lines)
            : "Текущий запуск сценария";
    }

    private static string FormatLapCounts(int chests, int reds, int skipped)
        => $"сундуки {chests} · красные {reds} · пропуск {skipped}";

    private void ClearRunProgress()
    {
        _runStats.Text = "";
        _runStats.ToolTipText = "";
    }

    private static string FormatRunSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private static Button Command(string text, EventHandler onClick)
    {
        var button = new Button { Text = text };
        StyleButton(button);
        button.Click += onClick;
        return button;
    }

    private static void StyleButton(Button button, bool roomy = false)
    {
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(48, 48, 48);
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = Color.FromArgb(82, 82, 82);
        button.Margin = new Padding(0, 0, roomy ? 8 : 6, roomy ? 8 : 6);
        button.Padding = roomy ? new Padding(16, 8, 16, 8) : new Padding(8, 2, 8, 2);
        button.MinimumSize = roomy ? new Size(96, 40) : new Size(0, 30);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool start = RegisterHotKey(Handle, FollowHotkeyStart, HotkeyNoRepeat, (uint)Keys.F6);
        bool stop = RegisterHotKey(Handle, FollowHotkeyStop, HotkeyNoRepeat, (uint)Keys.F7);
        bool block = RegisterHotKey(Handle, FollowHotkeyBlock, HotkeyNoRepeat, (uint)Keys.F8);
        bool capture = RegisterHotKey(Handle, FollowHotkeyCapture, HotkeyNoRepeat, (uint)Keys.F9);
        if (!start || !stop || !block || !capture)
            _status.Text = "F6, F7, F8 или F9 уже заняты другим приложением.";
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, FollowHotkeyStart);
        UnregisterHotKey(Handle, FollowHotkeyStop);
        UnregisterHotKey(Handle, FollowHotkeyBlock);
        UnregisterHotKey(Handle, FollowHotkeyCapture);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == HotkeyMessage)
        {
            int id = m.WParam.ToInt32();
            if (id == FollowHotkeyStart)
                StartScenario();
            else if (id == FollowHotkeyBlock)
                StartSelectedBlock();
            else if (id == FollowHotkeyStop)
                StopFollow();
            else if (id == FollowHotkeyCapture)
                CaptureClick();
            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Delete && _diagram.ContainsFocus && !PlaybackLocked)
        {
            if (ViewingChainOverview)
                RemoveChainStep();
            else
                RemoveBlock();
            return true;
        }

        if (!PlaybackLocked && EditedFollow is not null && (_list.Focused || _canvas.Focused || _pointButtons?.ContainsFocus == true))
        {
            if (keyData == (Keys.Control | Keys.Up))
            {
                ShiftSelected(-1);
                return true;
            }

            if (keyData == (Keys.Control | Keys.Down))
            {
                ShiftSelected(1);
                return true;
            }
        }

        if (keyData == Keys.Escape && (_timer.Enabled || _paused || _finished || _following))
        {
            if (_following)
                StopFollow();
            else
                Stop();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnFirstShow()
    {
        PlaceStudioSplitters();
        LoadRouteKeys();
        _dirty = false;
        LoadLibrary();
        LoadRunHistory();
        BeginInvoke(new Action(() =>
        {
            PlaceStudioSplitters();
            _diagram.Fit();
            _canvas.Fit();
        }));
    }

    private void OpenMap()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Карта локации",
            Filter = "PNG (*.png)|*.png",
            FileName = _mapPath is null ? "" : Path.GetFileName(_mapPath),
            InitialDirectory = InitialDirectory(_mapPath),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadMap(dialog.FileName);
    }

    private void LoadMap(string path)
    {
        ColorImage image;
        try
        {
            image = ColorImage.Load(path);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось открыть карту", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Stop();
        var seen = _engine.Observe(image, DateTimeOffset.UnixEpoch);
        _engine.ForgetBefore(DateTimeOffset.MaxValue);

        var bitmap = ToBitmap(image);
        var previous = _bitmap;
        _bitmap = bitmap;
        _mapPath = path;
        RefreshTitle();
        _bounds = seen.HasMap
            ? seen.Bounds
            : new MapBounds(0, 0, image.Width - 1, image.Height - 1);
        _detectedMarker = seen.PlayerFound ? seen.Position : null;
        _canvas.SetMap(bitmap, _bounds, _detectedMarker);
        previous?.Dispose();
        RefreshView();
        _status.Text = seen.PlayerFound
            ? $"Карта открыта, маркер кадра: {FormatPoint(seen.Position)}."
            : "Карта открыта. Зелёный маркер на кадре не найден.";
    }

    private void OpenRoute()
    {
        if (_scenario.Blocks.Count > 0 &&
            MessageBox.Show(this, "Текущий сценарий будет заменён.", "Открыть сценарий", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        using var dialog = new OpenFileDialog
        {
            Title = "Сценарий",
            Filter = "Сценарий (*.json)|*.json",
            InitialDirectory = MapLibrary.ScenariosFolder,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var scenario = ScenarioFile.Load(dialog.FileName, out bool legacy);
            Stop();
            if (legacy)
                CopyDefaultKeys(scenario);
            _scenario = scenario;
            _undo.Clear();
            _redo.Clear();
            _routePath = dialog.FileName;
            _dirty = false;
            _blockRow = 0;
            _selected = -1;
            _suppressBlocks = true;
            _loop.Checked = scenario.Loop;
            _pressKeys.Checked = scenario.PressKeys;
            _suppressBlocks = false;
            if (scenario.MapFile is { Length: > 0 } mapFile && File.Exists(mapFile) &&
                !string.Equals(mapFile, _mapPath, StringComparison.OrdinalIgnoreCase))
                LoadMap(mapFile);
            RefreshView();
            _status.Text = $"Сценарий открыт: {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось открыть маршрут", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveRoute()
    {
        if (ViewingChainOverview)
        {
            SaveChain();
            return;
        }

        if (string.IsNullOrWhiteSpace(_routePath))
        {
            SaveRouteAs();
            return;
        }

        WriteRoute(_routePath);
    }

    private void SaveRouteAs()
    {
        if (ViewingChainOverview)
        {
            SaveChain();
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Сохранить сценарий",
            Filter = "Сценарий (*.json)|*.json",
            FileName = string.IsNullOrWhiteSpace(_routePath) ? "scenario.json" : Path.GetFileName(_routePath),
            InitialDirectory = ScenarioDirectory(),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _routePath = dialog.FileName;
        WriteRoute(dialog.FileName);
    }

    private void WriteRoute(string path)
    {
        try
        {
            string? activeMap = _scenario.MapFile ?? _mapRecord?.ImagePath ?? _mapPath;
            if (!string.IsNullOrWhiteSpace(activeMap))
            {
                _scenario.EnsureMap(activeMap);
                _scenario.MapFile = activeMap;
            }

            _scenario.PressKeys = _pressKeys.Checked;
            _scenario.Loop = _loop.Checked;
            MapLibrary.StoreBlockRoutes(_scenario, _scenario.MapFile, _scenarioRecord?.Name ?? Path.GetFileNameWithoutExtension(path));
            ScenarioFile.Save(path, _scenario);
            _dirty = false;
            if (_scenarioRecord is not null)
            {
                bool renamed = !string.Equals(_scenarioRecord.FilePath, path, StringComparison.OrdinalIgnoreCase);
                _scenarioRecord.FilePath = path;
                _scenarioRecord.Document = _scenario;
                _scenarioRecord.Dirty = false;
                if (renamed || string.IsNullOrWhiteSpace(_scenarioRecord.Name))
                    _scenarioRecord.Name = Path.GetFileNameWithoutExtension(path);
                _routePath = path;
            }

            SaveLibrary();
            RefreshLibraryTree();
            RefreshTitle();
            _status.Text = $"Сценарий сохранён: {Path.GetFileName(path)}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось сохранить маршрут", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AddPoint(MapPoint point)
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow)
        {
            _status.Text = "Точки рисуются в выбранном блоке «Следовать маршруту».";
            return;
        }

        PushUndo();
        // Есть выделение — вставляем после него, иначе в конец.
        int index = _selected >= 0 && _selected < follow.Anchors.Count ? _selected + 1 : follow.Anchors.Count;
        var anchor = new Anchor { Source = AnchorSource.Fixed, Position = point };
        if (index > 0)
        {
            List<RouteKey> keys = follow.Anchors[index - 1].Keys.Count > 0
                ? follow.Anchors[index - 1].Keys
                : _defaultKeys;
            anchor.Keys.AddRange(CloneKeys(keys));
        }

        follow.Anchors.Insert(index, anchor);
        _selected = index;
        TouchRoute();
        _status.Text = index < follow.Anchors.Count - 1
            ? $"Точка вставлена после {index}."
            : $"Точка {index + 1} добавлена.";
    }

    private void InsertPoint(PointInsert insert)
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow)
            return;
        PushUndo();
        // insert.Index — индекс среди Fixed-точек на карте (между i и i+1 → i+1).
        int anchorIndex = FixedToAnchor(insert.Index);
        if (anchorIndex < 0)
            anchorIndex = follow.Anchors.Count;
        var anchor = new Anchor { Source = AnchorSource.Fixed, Position = insert.Point };
        if (anchorIndex > 0)
            anchor.Keys.AddRange(CloneKeys(follow.Anchors[anchorIndex - 1].Keys.Count > 0 ? follow.Anchors[anchorIndex - 1].Keys : _defaultKeys));
        follow.Anchors.Insert(anchorIndex, anchor);
        _selected = anchorIndex;
        TouchRoute();
        _status.Text = $"Точка вставлена между {insert.Index} и {insert.Index + 1}.";
    }

    private void MovePoint(PointMove move)
    {
        int anchorIndex = FixedToAnchor(move.Index);
        if (PlaybackLocked || EditedFollow is not FollowBlock follow || anchorIndex < 0)
            return;
        if (move.BeginsGesture)
            PushUndo();
        follow.Anchors[anchorIndex].Position = move.Point;
        _selected = anchorIndex;
        _dirty = true;
        if (_scenarioRecord is not null)
            _scenarioRecord.Dirty = true;
        // Во время drag не трогаем списки/схему — иначе Invalidate карты откладывается до MouseUp.
        if (move.EndsGesture)
            TouchRoute();
        else
            _canvas.SetRoute(FixedPositions(), AnchorToFixed(_selected), _badSegments);
    }

    private void RemovePoint(int index)
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow || index < 0 || index >= follow.Anchors.Count)
            return;
        PushUndo();
        follow.Anchors.RemoveAt(index);
        _selected = follow.Anchors.Count == 0 ? -1 : Math.Min(index, follow.Anchors.Count - 1);
        TouchRoute();
    }

    private void ClearRoute()
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow || follow.Anchors.Count == 0)
            return;
        if (MessageBox.Show(this, "Удалить все точки этого блока?", "Очистить", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        PushUndo();
        follow.Anchors.Clear();
        _selected = -1;
        TouchRoute();
    }

    private void ShiftSelected(int delta)
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow || _selected < 0)
            return;
        int target = _selected + delta;
        if (target < 0 || target >= follow.Anchors.Count)
            return;
        PushUndo();
        (follow.Anchors[_selected], follow.Anchors[target]) = (follow.Anchors[target], follow.Anchors[_selected]);
        _selected = target;
        TouchRoute();
    }

    private void MoveAnchorTo(int from, int to)
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow)
            return;
        if (from < 0 || from >= follow.Anchors.Count || to < 0 || to >= follow.Anchors.Count || from == to)
            return;
        PushUndo();
        Anchor item = follow.Anchors[from];
        follow.Anchors.RemoveAt(from);
        follow.Anchors.Insert(to, item);
        _selected = to;
        TouchRoute();
    }

    private void OnPointListMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || PlaybackLocked)
        {
            _pointListDragIndex = -1;
            return;
        }

        int index = _list.IndexFromPoint(e.Location);
        _pointListDragIndex = index >= 0 && index < _list.Items.Count ? index : -1;
    }

    private void OnPointListMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _pointListDragIndex < 0 || PlaybackLocked)
            return;
        if ((Control.ModifierKeys & Keys.Control) != 0)
            return;
        _list.DoDragDrop(_pointListDragIndex, DragDropEffects.Move);
    }

    private void OnPointListDragOver(object? sender, DragEventArgs e)
    {
        e.Effect = !PlaybackLocked && e.Data?.GetDataPresent(typeof(int)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnPointListDragDrop(object? sender, DragEventArgs e)
    {
        if (PlaybackLocked || e.Data?.GetData(typeof(int)) is not int from)
            return;
        Point client = _list.PointToClient(new Point(e.X, e.Y));
        int to = _list.IndexFromPoint(client);
        if (to < 0)
            to = _list.Items.Count - 1;
        MoveAnchorTo(from, to);
        _pointListDragIndex = -1;
    }

    private void StartFromMarker()
    {
        if (PlaybackLocked || EditedFollow is not FollowBlock follow || _detectedMarker is not MapPoint marker)
        {
            _status.Text = EditedFollow is null
                ? "Сначала выберите блок «Следовать маршруту»."
                : "На открытой карте зелёный маркер не найден.";
            return;
        }

        if (follow.Anchors.Count > 0 && follow.Anchors[0].Source == AnchorSource.Fixed && !_bounds.IsEmpty)
        {
            double distance = RouteGeometry.PixelLength(_bounds, follow.Anchors[0].Position, marker);
            if (distance < 2)
            {
                _status.Text = "Первая точка уже стоит на маркере кадра.";
                return;
            }
        }

        PushUndo();
        follow.Anchors.Insert(0, new Anchor { Source = AnchorSource.Fixed, Position = marker });
        _selected = 0;
        TouchRoute();
        _status.Text = "Старт поставлен на маркер кадра.";
    }

    private void SelectPoint(int index, bool center)
    {
        FollowBlock? follow = EditedFollow;
        _selected = follow is not null && index >= 0 && index < follow.Anchors.Count ? index : -1;
        if (_suppressList)
            return;
        SyncListSelection();
        _canvas.SetRoute(FixedPositions(), AnchorToFixed(_selected), _badSegments);
        ShowRouteKeys();
        UpdateSummary();
        if (center && _selected >= 0 && follow!.Anchors[_selected].Source == AnchorSource.Fixed)
            _canvas.CenterOn(follow.Anchors[_selected].Position);
    }

    private void Play()
    {
        if (_following)
        {
            _status.Text = "Сначала остановите ведение персонажа.";
            return;
        }

        if (_bitmap is null)
        {
            _status.Text = "Сначала откройте карту.";
            return;
        }

        if (EditedFollow is not FollowBlock playing || playing.Travel != TravelMode.Vectors || FixedPositions().Count < 2)
        {
            _status.Text = "Прогон показывает нарисованные векторы выбранного блока. Нужны хотя бы две точки на карте.";
            return;
        }

        if (_paused)
        {
            _paused = false;
            _watch.Start();
            _timer.Start();
            UpdateButtons();
            return;
        }

        if (_timer.Enabled)
            return;

        _engine.ForgetBefore(DateTimeOffset.MaxValue);
        _distance = 0;
        _lastElapsed = 0;
        _finished = false;
        _paused = false;
        _origin = DateTimeOffset.UtcNow;
        _watch.Restart();
        _timer.Start();
        PublishPose(RouteGeometry.Pose(FixedPositions(), _bounds, 0));
        UpdateButtons();
    }

    private void Pause()
    {
        if (!_timer.Enabled)
            return;
        _timer.Stop();
        _watch.Stop();
        _paused = true;
        UpdateButtons();
        _status.Text = "Пауза. Стоп снимет маркер прогона и снова откроет правку.";
    }

    private void Stop()
    {
        bool wasActive = _timer.Enabled || _paused || _finished;
        _timer.Stop();
        _watch.Reset();
        _paused = false;
        _finished = false;
        _distance = 0;
        _lastElapsed = 0;
        _engine.ForgetBefore(DateTimeOffset.MaxValue);
        _canvas.ClearRunner();
        UpdateButtons();
        if (wasActive)
            _status.Text = "Прогон остановлен.";
    }

    private void OnPlaybackTick()
    {
        double elapsed = _watch.Elapsed.TotalSeconds;
        double delta = Math.Max(0, elapsed - _lastElapsed);
        _lastElapsed = elapsed;
        _distance += (double)_speed.Value * delta;

        var pose = RouteGeometry.Pose(FixedPositions(), _bounds, _distance);
        PublishPose(pose);
        if (!pose.Finished)
            return;

        _timer.Stop();
        _watch.Stop();
        _finished = true;
        _paused = false;
        UpdateButtons();
        _status.Text = "Маршрут пройден. Маркер стоит на последней точке.";
    }

    private void PublishPose(RoutePose pose)
    {
        var stamp = _origin + TimeSpan.FromSeconds(Math.Max(0, _lastElapsed));
        _engine.Report(pose.Position, stamp);
        var stored = _engine.PositionAt(stamp);
        _canvas.SetRunner(stored, pose.HeadingRadians, pose.SegmentIndex);

        double degrees = (pose.HeadingRadians * 180.0 / Math.PI % 360 + 360) % 360;
        string place = _engine.HasMap && _engine.IsWalkable(stored) ? "на карте" : "вне карты";
        double progress = pose.TotalLength <= 0 ? 100 : Math.Clamp(pose.Distance / pose.TotalLength * 100, 0, 100);
        _status.Text = $"Прогон  {progress:0}%   точка {FormatPoint(stored)}   угол {degrees:0}°   {place}";
        UpdateSummary(pose);
    }

    private void Undo()
    {
        if (PlaybackLocked || _undo.Count == 0)
            return;
        _redo.Add(_scenario.Clone());
        _scenario = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _dirty = true;
        _selected = -1;
        RememberScenarioDocument();
        RefreshView();
    }

    private void Redo()
    {
        if (PlaybackLocked || _redo.Count == 0)
            return;
        _undo.Add(_scenario.Clone());
        _scenario = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _dirty = true;
        _selected = -1;
        RememberScenarioDocument();
        RefreshView();
    }

    private void PushUndo()
    {
        _undo.Add(_scenario.Clone());
        if (_undo.Count > 80)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void TouchRoute()
    {
        _dirty = true;
        if (_scenarioRecord is not null)
            _scenarioRecord.Dirty = true;
        _finished = false;
        _canvas.ClearRunner();
        ClearRouteOverlay();
        _libraryMapPreview = false;
        RefreshView();
    }

    private void RefreshView()
    {
        // Превью карты/маршрута из библиотеки — без точек текущего блока.
        List<MapPoint> drawn = _routeOverlay
            ?? (_libraryMapPreview ? [] : FixedPositions());
        int selected = _routeOverlay is null && !_libraryMapPreview ? AnchorToFixed(_selected) : -1;
        _badSegments = _bitmap is null || drawn.Count == 0
            ? []
            : RouteGeometry.OffMapSegments(drawn, _bounds, IsWalkable);
        _canvas.SetRoute(drawn, selected, _badSegments);
        RebuildBlocks();
        RebuildList();
        RebuildClicks(keepSelection: true);
        UpdateSummary();
        ShowRouteKeys();
        UpdateButtons();
    }

    private void RebuildList()
    {
        _suppressList = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        if (EditedFollow is FollowBlock follow)
        {
            for (int i = 0; i < follow.Anchors.Count; i++)
                _list.Items.Add(DescribePoint(follow, i));
        }

        if (_selected >= 0 && _selected < _list.Items.Count)
            _list.SelectedIndex = _selected;
        _list.EndUpdate();
        _suppressList = false;
    }

    private void SyncListSelection()
    {
        _suppressList = true;
        _list.SelectedIndex = _selected >= 0 && _selected < _list.Items.Count ? _selected : -1;
        _suppressList = false;
    }

    private string DescribePoint(FollowBlock follow, int index)
    {
        Anchor anchor = follow.Anchors[index];
        string place = anchor.Source switch
        {
            AnchorSource.Player => "текущее положение",
            AnchorSource.Skull => "красный",
            AnchorSource.Chest => "сундук",
            AnchorSource.QuestQuestion => "вопрос",
            AnchorSource.QuestExclaim => "восклицание",
            AnchorSource.RouteStart => "старт маршрута",
            _ => FormatPoint(anchor.Position),
        };
        string wait = anchor.Source is AnchorSource.Skull or AnchorSource.Chest
            or AnchorSource.QuestQuestion or AnchorSource.QuestExclaim
            ? anchor.Wait switch
            {
                GoalWait.UntilAppears => "  до появления",
                GoalWait.Skip => "  не ждать",
                _ => anchor.WaitMilliseconds % 1000 == 0
                    ? $"  ждать {anchor.WaitMilliseconds / 1000} с"
                    : $"  ждать {anchor.WaitMilliseconds} мс",
            }
            : "";
        if (index == 0)
            return $"01  старт   {place}{wait}";

        string vector = "вектор";
        if (anchor.Source == AnchorSource.Fixed && follow.Anchors[index - 1].Source == AnchorSource.Fixed && !_bounds.IsEmpty)
        {
            vector = $"{RouteGeometry.HeadingDegrees(_bounds, follow.Anchors[index - 1].Position, anchor.Position):0}°  {RouteGeometry.PixelLength(_bounds, follow.Anchors[index - 1].Position, anchor.Position):0} px";
        }

        int fixedIndex = AnchorToFixed(index);
        string warning = fixedIndex > 0 && _badSegments.Contains(fixedIndex - 1) ? "  вне карты" : "";
        string keys = anchor.Keys.Count > 0
            ? "  [" + string.Join(", ", anchor.Keys.Select(item => FormatKey(item.Key))) + "]"
            : "";
        return $"{index + 1,2}  {vector}   {place}{warning}{keys}{wait}";
    }

    private void UpdateSummary(RoutePose? pose = null)
    {
        if (_bitmap is null)
        {
            _summary.Text = "Карта ещё не открыта.";
            return;
        }

        List<MapPoint> drawn = FixedPositions();
        double length = RouteGeometry.TotalLength(drawn, _bounds);
        int vectors = Math.Max(0, drawn.Count - 1);
        string blocks = $"Блоков: {Scenario.Walk(_scenario.Blocks).Count()}.";
        string off = _badSegments.Count == 0
            ? "Все векторы лежат на цветной карте."
            : "Вне карты: " + string.Join(", ", _badSegments.Order().Select(index => (index + 1).ToString(CultureInfo.CurrentCulture))) + ".";
        string marker = _detectedMarker is MapPoint detected
            ? $"Маркер кадра: {FormatPoint(detected)}."
            : "Маркер кадра не найден.";
        string run = pose is RoutePose current
            ? $"Сейчас: {FormatPoint(current.Position)}, угол {(current.HeadingRadians * 180 / Math.PI % 360 + 360) % 360:0}°."
            : "Прогон ещё не запущен.";

        _summary.Text =
            $"{blocks} Векторов на карте: {vectors}.{Environment.NewLine}" +
            $"Длина: {length:0} px{Environment.NewLine}" +
            $"{off}{Environment.NewLine}" +
            $"{marker}{Environment.NewLine}" +
            $"{run}{Environment.NewLine}" +
            "Угол: 0° вправо по карте, далее по часовой.";
    }

    private void UpdateButtons()
    {
        bool locked = PlaybackLocked;
        _delete.Enabled = !locked && EditedFollow is not null && _selected >= 0;
        _clear.Enabled = !locked && EditedFollow is { Anchors.Count: > 0 };
        _up.Enabled = !locked && _selected > 0;
        _down.Enabled = !locked && EditedFollow is FollowBlock follow && _selected >= 0 && _selected < follow.Anchors.Count - 1;
        _fromMarker.Enabled = !locked && EditedFollow is not null && _detectedMarker is not null;
        _play.Enabled = !_following && _bitmap is not null && (_paused || (EditedFollow is FollowBlock preview && preview.Travel == TravelMode.Vectors && FixedPositions().Count >= 2));
        _pause.Enabled = _timer.Enabled;
        _stop.Enabled = _timer.Enabled || _paused || _finished;
        bool canRunDocument = ViewingChainOverview
            ? _chainRecord is not null
            : _scenarioRecord is not null;
        bool busy = _following || _launchingGame;
        _follow.Enabled = !busy && !_timer.Enabled && !_paused && canRunDocument;
        _follow.Text = ViewingChainOverview ? "Цепочка  F6" : "Сценарий  F6";
        _runBlock.Enabled = !ViewingChainOverview && !busy && !_timer.Enabled && !_paused && _picked.Count > 0;
        _runBlock.Text = _picked.Count > 1 ? $"Блоки {_picked.Count}  F8" : "Блок  F8";
        _stopFollow.Enabled = busy;
        _launchGame.Enabled = !busy && !_timer.Enabled && !_paused;
        _loop.Enabled = !busy;
        _canvas.InputLocked = locked || EditedFollow is null;
        _speed.Enabled = !locked;
        bool editKeys = !_following && SelectedSegmentKeys() is not null;
        _keyName.Enabled = editKeys;
        _keyInterval.Enabled = editKeys;
        _addKey.Enabled = editKeys;
        _removeKey.Enabled = editKeys && _keys.SelectedIndex >= 0;
        _clearKeys.Enabled = editKeys && SelectedSegmentKeys() is { Count: > 0 };
        _keys.Enabled = editKeys;
        _pressKeys.Enabled = !busy;
        _autoRestart.Enabled = !busy;
        bool editWait = !_following && SelectedConcreteAnchor() is not null;
        _goalWait.Enabled = editWait;
        _goalWaitMs.Enabled = editWait && _goalWait.SelectedIndex == (int)GoalWait.Timed;
        RefreshScenarioLabels();
    }

    private void ShowHover(MapPoint? point)
    {
        if (point is not MapPoint place)
        {
            _mapCursor.Text = "Курсор на карте не выбран.";
            return;
        }

        string ground = _bitmap is not null && IsWalkable(place) ? "на карте" : "вне карты";
        string text = $"Курсор  {FormatPoint(place)}   {ground}";
        _mapCursor.Text = text;
        if (_timer.Enabled || _following)
            return;
        _status.Text = text;
    }

    private bool IsWalkable(MapPoint point)
        => _engine.HasMap && _engine.IsWalkable(point);

    private bool PlaybackLocked => _timer.Enabled || _paused || _following;

    private static string FollowConfigPath => Path.Combine(AppContext.BaseDirectory, "follow-config.json");

    private void BeginFollow(Scenario running, bool wholeScenario)
    {
        if (_following)
            return;

        if (_timer.Enabled || _paused)
        {
            _runningBlock = null;
            _selectionRun = false;
            _status.Text = "Сначала остановите прогон точки.";
            RebuildBlocks();
            return;
        }

        FollowConfig config;
        try
        {
            config = FollowConfig.LoadOrCreate(FollowConfigPath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Конфиг ведения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _runningBlock = null;
            _selectionRun = false;
            return;
        }

        bool pinWindow = config.HasMapInClient && !string.IsNullOrWhiteSpace(config.GameWindowTitleContains);
        bool absoluteMap = config.MapRegion.Width >= 8 && config.MapRegion.Height >= 8;
        if (!pinWindow && !absoluteMap)
        {
            _status.Text = config.HasMapInClient
                ? "В follow-config.json укажите gameWindowTitleContains — часть заголовка окна игры."
                : "В follow-config.json задайте mapInClient или mapRegion.";
            OpenFollowConfig();
            _runningBlock = null;
            _selectionRun = false;
            RebuildBlocks();
            return;
        }

        if (running.Blocks.Count == 0)
        {
            _runningBlock = null;
            _selectionRun = false;
            _status.Text = wholeScenario
                ? "Сценарий пуст. Перетащите блоки на схему."
                : "В этом блоке нечего выполнять.";
            RebuildBlocks();
            return;
        }

        _scenario.PressKeys = _pressKeys.Checked;
        _scenario.Loop = _loop.Checked;
        if (wholeScenario)
            running.Loop = _loop.Checked;
        running.PressKeys = _pressKeys.Checked;
        config.PressKeys = _pressKeys.Checked;
        config.Restart.Enabled = _autoRestart.Checked;
        PersistFollowUiFlags();

        int generation = ++_followGeneration;
        _followCts = new CancellationTokenSource();
        var token = _followCts.Token;
        var follower = new CharacterFollower();
        FileLog.RunStarted(config, running, wholeScenario, _scenarioRecord?.Name);
        BeginRunHistory(wholeScenario, running.Loop);
        follower.Status += text => Post(() => _status.Text = text);
        follower.Progress += progress => Post(() =>
        {
            if (generation != _followGeneration)
                return;
            ShowRunProgress(progress);
            UpdateRunHistory(progress);
        });
        ClearRunProgress();
        _following = true;
        UpdateButtons();
        RebuildBlocks();

        var thread = new Thread(() =>
        {
            try
            {
                follower.Run(config, running, token);
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Post(() => _status.Text = exception.Message);
            }
            finally
            {
                FileLog.Trace("run", token.IsCancellationRequested ? "stop cancelled" : "stop finished");
                _following = false;
                Post(() =>
                {
                    if (generation != _followGeneration)
                        return;
                    FinishRunHistory(token.IsCancellationRequested);
                    _runningBlock = null;
                    _selectionRun = false;
                    if (token.IsCancellationRequested)
                        _status.Text = "Ведение остановлено.";
                    UpdateButtons();
                    RebuildBlocks();
                });
            }
        })
        {
            IsBackground = true,
            Name = "CharacterFollower",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void StopFollow()
    {
        if (_following)
            _followCts?.Cancel();
        if (_launchingGame)
            _launchCts?.Cancel();
    }

    private void LaunchGame()
    {
        if (_following || _launchingGame || _timer.Enabled || _paused)
            return;

        FollowConfig config;
        try
        {
            PersistFollowUiFlags();
            config = FollowConfig.LoadOrCreate(FollowConfigPath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Запуск игры", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.Restart.ExecutablePath))
        {
            _status.Text = "Укажите restart.executablePath в follow-config или secrets.";
            OpenFollowConfig();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.GameWindowTitleContains))
        {
            _status.Text = "Укажите gameWindowTitleContains в follow-config.json.";
            OpenFollowConfig();
            return;
        }

        int generation = ++_followGeneration;
        _launchCts = new CancellationTokenSource();
        var token = _launchCts.Token;
        _launchingGame = true;
        _status.Text = "Запуск игры…";
        FileLog.Trace("restart", "manual-launch");
        UpdateButtons();

        var thread = new Thread(() =>
        {
            bool ok = false;
            try
            {
                ok = GameRestart.TryRecover(config, text => Post(() =>
                {
                    if (generation == _followGeneration)
                        _status.Text = text;
                }), token);
            }
            catch (Exception exception)
            {
                FileLog.Trace("restart", exception.ToString());
                Post(() =>
                {
                    if (generation == _followGeneration)
                        _status.Text = exception.Message;
                });
            }
            finally
            {
                _launchingGame = false;
                Post(() =>
                {
                    if (generation != _followGeneration)
                        return;
                    if (token.IsCancellationRequested)
                        _status.Text = "Запуск игры остановлен.";
                    else if (ok)
                        _status.Text = "Игра запущена и в мире.";
                    UpdateButtons();
                });
            }
        })
        {
            IsBackground = true,
            Name = "GameLaunch",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void OpenStuckRecovery()
    {
        try
        {
            FollowConfig config = FollowConfig.LoadOrCreate(FollowConfigPath);
            using var dialog = new StuckRecoveryForm(config.StuckRecoveryMilliseconds, config.StuckRecoveryShiftPixels, config.StuckRecoveryKeys);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            if (!FollowConfig.TryParseRecoveryKeys(dialog.KeysText, out List<string> keys, out string? error))
            {
                MessageBox.Show(this, error, "Застревание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            config.StuckRecoveryMilliseconds = dialog.Milliseconds;
            config.StuckRecoveryShiftPixels = dialog.ShiftPixels;
            config.StuckRecoveryKeys = keys;
            config.Save(FollowConfigPath);
            string listed = keys.Count == 0 ? "клавиши не заданы" : string.Join(", ", keys);
            _status.Text = $"Застревание: {dialog.Milliseconds} мс, сдвиг {dialog.ShiftPixels} px, {listed}. Действует со следующего запуска.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Застревание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenFollowConfig()
    {
        try
        {
            FollowConfig.LoadOrCreate(FollowConfigPath);
            Process.Start(new ProcessStartInfo(FollowConfigPath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Конфиг ведения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Окно уже закрывается.
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_dirty)
        {
            var answer = MessageBox.Show(
                this,
                "Сценарий изменён. Сохранить его?",
                "Конструктор сценариев",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
                e.Cancel = true;
            else if (answer == DialogResult.Yes)
            {
                SaveRoute();
                if (_dirty)
                    e.Cancel = true;
            }
        }

        if (!e.Cancel && _chainDirty && _chainRecord is not null)
        {
            var answer = MessageBox.Show(
                this,
                $"Цепочка «{_chainRecord.Name}» изменена. Сохранить её?",
                "Конструктор сценариев",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
                e.Cancel = true;
            else if (answer == DialogResult.Yes)
            {
                SaveChain();
                if (_chainDirty)
                    e.Cancel = true;
            }
        }

        if (!e.Cancel)
            SaveLibrary();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _followCts?.Cancel();
        _timer.Stop();
        _timer.Dispose();
        _bitmap?.Dispose();
        _treeBold?.Dispose();
        _tips.Dispose();
        base.OnFormClosed(e);
    }

    private static string FormatPoint(MapPoint point)
        => $"x {point.X.ToString("0.000", CultureInfo.CurrentCulture)}   y {point.Y.ToString("0.000", CultureInfo.CurrentCulture)}";

    private string ScenarioDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_routePath))
        {
            string? directory = Path.GetDirectoryName(_routePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                return directory;
        }

        return MapLibrary.ScenariosFolder;
    }

    private static string InitialDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            return Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        return FindSampleMap() is string sample
            ? Path.GetDirectoryName(sample) ?? Environment.CurrentDirectory
            : Environment.CurrentDirectory;
    }

    private static string? FindSampleMap()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 7 && directory is not null; i++, directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "WalkerEngine.Tests", "map.png");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static Bitmap ToBitmap(ColorImage image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rectangle = new Rectangle(0, 0, image.Width, image.Height);
        var data = bitmap.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            int stride = data.Stride;
            var bytes = new byte[stride * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < image.Width; x++)
                {
                    var color = image[x, y];
                    int index = row + x * 4;
                    bytes[index] = color.B;
                    bytes[index + 1] = color.G;
                    bytes[index + 2] = color.R;
                    bytes[index + 3] = color.A == 0 ? (byte)255 : color.A;
                }
            }

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private const int FollowHotkeyStart = 0x5701;
    private const int FollowHotkeyStop = 0x5702;
    private const int FollowHotkeyBlock = 0x5703;
    private const int FollowHotkeyCapture = 0x5704;
    private const int HotkeyMessage = 0x0312;
    private const uint HotkeyNoRepeat = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
}

internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => Color.FromArgb(28, 28, 28);
    public override Color ToolStripGradientMiddle => Color.FromArgb(28, 28, 28);
    public override Color ToolStripGradientEnd => Color.FromArgb(28, 28, 28);
    public override Color MenuStripGradientBegin => Color.FromArgb(28, 28, 28);
    public override Color MenuStripGradientEnd => Color.FromArgb(28, 28, 28);
    public override Color MenuItemSelected => Color.FromArgb(62, 62, 62);
    public override Color MenuItemBorder => Color.FromArgb(82, 82, 82);
    public override Color MenuBorder => Color.FromArgb(70, 70, 70);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(62, 62, 62);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(62, 62, 62);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(48, 48, 48);
    public override Color MenuItemPressedGradientMiddle => Color.FromArgb(48, 48, 48);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(48, 48, 48);
    public override Color ToolStripDropDownBackground => Color.FromArgb(28, 28, 28);
    public override Color ImageMarginGradientBegin => Color.FromArgb(28, 28, 28);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(28, 28, 28);
    public override Color ImageMarginGradientEnd => Color.FromArgb(28, 28, 28);
    public override Color SeparatorDark => Color.FromArgb(70, 70, 70);
    public override Color SeparatorLight => Color.FromArgb(70, 70, 70);
    public override Color ToolStripBorder => Color.FromArgb(48, 48, 48);
    public override Color StatusStripGradientBegin => Color.FromArgb(24, 24, 24);
    public override Color StatusStripGradientEnd => Color.FromArgb(24, 24, 24);
}
