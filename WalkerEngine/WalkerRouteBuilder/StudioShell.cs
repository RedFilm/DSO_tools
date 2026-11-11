using WalkerEngine;

namespace WalkerRouteBuilder;

internal sealed partial class MainForm
{
    private readonly DiagramCanvas _diagram = new();
    private readonly TreeView _libraryTree = new();
    private readonly Button _runBlock = new();
    private readonly Button _stopFollow = new();
    private readonly ToolTip _tips = new();
    private MapLibrary _library = new();
    private MapRecord? _mapRecord;
    private ScenarioRecord? _scenarioRecord;
    private RouteRecord? _routeRecord;
    private List<MapPoint>? _routeOverlay;
    private bool _libraryMapPreview;
    private List<RouteRecord>? _routesCache;
    private readonly List<ScenarioBlock> _picked = [];
    private ScenarioBlock? _runningBlock;
    private bool _selectionRun;
    private bool _suppressLibrary;
    private bool _libraryReady;
    private SplitContainer? _studioSplit;
    private SplitContainer? _workSplit;
    private SplitContainer? _inspectorSplit;
    private SplitContainer? _mapSplit;
    private SplitContainer? _historySplit;
    private readonly Label _mapCursor = new();
    private Font? _treeBold;
    private readonly ListBox _runHistoryList = new();
    private readonly ListBox _runHistoryLaps = new();
    private readonly Label _runHistoryDetail = new();
    private RunHistoryStore _runHistory = new();
    private RunHistoryEntry? _activeRun;
    private RunProgress? _lastRunProgress;
    private ToolStripMenuItem? _historyMenuItem;

    private Control BuildStudio()
    {
        BuildSettingsWindow();
        _treeBold = new Font(_libraryTree.Font, FontStyle.Bold);

        var studio = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(17, 19, 22),
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel1,
        };
        var historyHost = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Color.FromArgb(17, 19, 22),
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
        };
        var work = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(17, 19, 22),
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
        };
        var stage = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Color.FromArgb(17, 19, 22),
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
        };

        _diagram.Dock = DockStyle.Fill;
        _diagram.BlockSelected += OnDiagramSelection;
        _diagram.InsertKind += InsertDropped;
        _diagram.MoveBlock += RelocateBlock;
        _diagram.BlockMenu += ShowBlockMenu;
        _diagram.PauseRequested += EditBlockPause;
        WireChainDiagram();
        _diagramHint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            AutoSize = false,
            Padding = new Padding(16, 4, 8, 0),
            BackColor = Color.FromArgb(17, 19, 22),
            ForeColor = Color.FromArgb(148, 154, 164),
            Text = "Ctrl добавляет блок, Shift берёт диапазон. F6 — сценарий, F8 — выбранные, F7 — стоп.",
        };
        var diagramHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17, 19, 22) };
        diagramHost.Controls.Add(_diagram);
        diagramHost.Controls.Add(_diagramHint);
        diagramHost.Controls.Add(BuildBreadcrumbs());

        var mapSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(17, 19, 22),
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
        };
        var mapHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 12), Padding = new Padding(8) };
        mapHost.Controls.Add(_canvas);
        mapSplit.Panel1.Controls.Add(mapHost);
        mapSplit.Panel2.Controls.Add(BuildMapInfo());

        stage.Panel1.Controls.Add(diagramHost);
        stage.Panel2.Controls.Add(mapSplit);
        work.Panel1.Controls.Add(stage);
        if (_settingsPage is not null)
            work.Panel2.Controls.Add(_settingsPage);
        historyHost.Panel1.Controls.Add(work);
        historyHost.Panel2.Controls.Add(BuildRunHistoryPanel());
        studio.Panel1.Controls.Add(BuildLibraryRail());
        studio.Panel2.Controls.Add(historyHost);

        _studioSplit = studio;
        _workSplit = work;
        _inspectorSplit = stage;
        _mapSplit = mapSplit;
        _historySplit = historyHost;
        return studio;
    }

    private Control BuildRunHistoryPanel()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 24, 28),
            Padding = new Padding(12, 8, 12, 8),
        };

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.FromArgb(22, 24, 28),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "История запусков",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(196, 200, 208),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var clear = Command("Очистить", (_, _) => ClearRunHistory());
        clear.AutoSize = true;
        clear.Margin = new Padding(4, 0, 0, 0);
        var remove = Command("Удалить", (_, _) => RemoveSelectedRunHistory());
        remove.AutoSize = true;
        remove.Margin = new Padding(4, 0, 0, 0);
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(remove, 1, 0);
        header.Controls.Add(clear, 2, 0);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 24, 28),
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
        };

        StyleHistoryList(_runHistoryList);
        _runHistoryList.SelectedIndexChanged += (_, _) => ShowSelectedRunHistory();
        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(58, 64, 72),
        };
        listHost.Controls.Add(_runHistoryList);

        StyleHistoryList(_runHistoryLaps);
        _runHistoryDetail.Dock = DockStyle.Top;
        _runHistoryDetail.Height = 44;
        _runHistoryDetail.AutoSize = false;
        _runHistoryDetail.Padding = new Padding(8, 4, 8, 4);
        _runHistoryDetail.ForeColor = Color.FromArgb(196, 200, 208);
        _runHistoryDetail.BackColor = Color.FromArgb(16, 18, 21);
        _runHistoryDetail.Text = "Выберите запуск слева.";

        var detailHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(58, 64, 72),
        };
        var detailInner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(16, 18, 21) };
        detailInner.Controls.Add(_runHistoryLaps);
        detailInner.Controls.Add(_runHistoryDetail);
        detailHost.Controls.Add(detailInner);

        body.Panel1.Controls.Add(listHost);
        body.Panel2.Controls.Add(detailHost);
        body.Panel1MinSize = 80;
        body.Panel2MinSize = 80;

        root.Controls.Add(body);
        root.Controls.Add(header);
        root.Resize += (_, _) => PlaceHistoryDetailSplit(body, preferredDetailWidth: 320);
        return root;
    }

    private static void PlaceHistoryDetailSplit(SplitContainer body, int preferredDetailWidth)
    {
        int room = body.Width - body.SplitterWidth;
        if (room < 400)
            return;
        int panel1Min = Math.Min(220, Math.Max(80, room / 3));
        int panel2Min = Math.Min(260, Math.Max(80, room / 3));
        if (panel1Min + panel2Min > room)
            return;
        TryPlaceSplitter(body, panel1Min, panel2Min, Math.Clamp(room - preferredDetailWidth, panel1Min, room - panel2Min));
    }

    private static void StyleHistoryList(ListBox list)
    {
        list.Dock = DockStyle.Fill;
        list.BorderStyle = BorderStyle.None;
        list.BackColor = Color.FromArgb(16, 18, 21);
        list.ForeColor = Color.FromArgb(230, 232, 236);
        list.IntegralHeight = false;
        list.ItemHeight = 22;
        list.HorizontalScrollbar = true;
    }

    private void LoadRunHistory()
    {
        _runHistory = RunHistoryStore.Load();
        RefreshRunHistoryList(selectFirst: true);
    }

    private void RefreshRunHistoryList(bool selectFirst = false)
    {
        object? selected = _runHistoryList.SelectedItem;
        _runHistoryList.BeginUpdate();
        _runHistoryList.Items.Clear();
        foreach (RunHistoryEntry entry in _runHistory.Entries)
            _runHistoryList.Items.Add(entry);
        _runHistoryList.EndUpdate();

        if (_runHistoryList.Items.Count == 0)
        {
            _runHistoryDetail.Text = "Запусков ещё не было.";
            _runHistoryLaps.Items.Clear();
            return;
        }

        if (selectFirst)
            _runHistoryList.SelectedIndex = 0;
        else if (selected is RunHistoryEntry keep)
        {
            int index = _runHistory.Entries.FindIndex(item => item.Id == keep.Id);
            _runHistoryList.SelectedIndex = index >= 0 ? index : 0;
        }
        else if (_runHistoryList.SelectedIndex < 0)
            _runHistoryList.SelectedIndex = 0;
        else
            ShowSelectedRunHistory();
    }

    private void ShowSelectedRunHistory()
    {
        if (_runHistoryList.SelectedItem is not RunHistoryEntry entry)
        {
            _runHistoryDetail.Text = "Выберите запуск слева.";
            _runHistoryLaps.Items.Clear();
            return;
        }

        string when = entry.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        string state = entry.InProgress ? "идёт" : entry.Cancelled ? "остановлен" : "завершён";
        string loop = entry.Looping ? " · цикл" : "";
        string average = entry.AverageLapDuration is TimeSpan avg
            ? $" · среднее {FormatRunSpan(avg)}"
            : "";
        _runHistoryDetail.Text =
            $"{when} · {entry.ScenarioName} · {entry.Scope}{loop} · {state}\n" +
            $"Всего {FormatRunSpan(entry.Duration)}{average} · сундуки {entry.ChestsOpened} · красные {entry.RedsKilled} · пропуск {entry.Skipped}";

        _runHistoryLaps.BeginUpdate();
        _runHistoryLaps.Items.Clear();
        if (entry.Laps.Count == 0)
            _runHistoryLaps.Items.Add("Кругов пока нет.");
        else
        {
            for (int i = 0; i < entry.Laps.Count; i++)
            {
                RunLapEntry lap = entry.Laps[i];
                string mark = lap.Incomplete ? " (не завершён)" : "";
                _runHistoryLaps.Items.Add(
                    $"Круг {i + 1}: {FormatRunSpan(lap.Duration)} · сундуки {lap.ChestsOpened} · красные {lap.RedsKilled} · пропуск {lap.Skipped}{mark}");
            }
        }

        _runHistoryLaps.EndUpdate();
    }

    private void ClearRunHistory()
    {
        if (_runHistory.Entries.Count == 0)
            return;
        if (MessageBox.Show(this, "Очистить всю историю запусков?", "История", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        _activeRun = null;
        _runHistory.Clear();
        RefreshRunHistoryList();
        _status.Text = "История запусков очищена.";
    }

    private void RemoveSelectedRunHistory()
    {
        if (_runHistoryList.SelectedItem is not RunHistoryEntry entry)
            return;
        if (ReferenceEquals(entry, _activeRun))
            _activeRun = null;
        _runHistory.Remove(entry);
        RefreshRunHistoryList(selectFirst: true);
    }

    private void ToggleRunHistoryPanel()
    {
        if (_historySplit is null)
            return;
        _historySplit.Panel2Collapsed = !_historySplit.Panel2Collapsed;
        if (_historyMenuItem is not null)
            _historyMenuItem.Checked = !_historySplit.Panel2Collapsed;
        if (!_historySplit.Panel2Collapsed)
            PlaceStudioSplitters();
    }

    private string RunScopeLabel(bool wholeScenario)
    {
        if (ViewingChainOverview)
            return "Цепочка";
        if (wholeScenario)
            return "Сценарий";
        if (_selectionRun)
            return "Выбранные блоки";
        if (_runningBlock is not null)
            return "Блок: " + _runningBlock.Title;
        return "Блок";
    }

    private void BeginRunHistory(bool wholeScenario, bool looping)
    {
        string name = ViewingChainOverview
            ? (_chainRecord?.Name ?? "Цепочка")
            : (_scenarioRecord?.Name ?? "Сценарий");
        _lastRunProgress = null;
        _activeRun = _runHistory.Begin(name, RunScopeLabel(wholeScenario), looping);
        RefreshRunHistoryList();
        _runHistoryList.SelectedIndex = 0;
    }

    private void UpdateRunHistory(RunProgress progress)
    {
        _lastRunProgress = progress;
        if (_activeRun is null)
            return;
        _runHistory.ApplyProgress(_activeRun, progress);
        int index = _runHistoryList.Items.IndexOf(_activeRun);
        if (index < 0)
            RefreshRunHistoryList();
        else
        {
            bool keep = _runHistoryList.SelectedIndex == index;
            _runHistoryList.Items.RemoveAt(index);
            _runHistoryList.Items.Insert(index, _activeRun);
            if (keep)
                _runHistoryList.SelectedIndex = index;
        }

        if (_runHistoryList.SelectedItem is RunHistoryEntry selected && selected.Id == _activeRun.Id)
            ShowSelectedRunHistory();
    }

    private void FinishRunHistory(bool cancelled)
    {
        if (_activeRun is null)
            return;
        _runHistory.Finish(_activeRun, _lastRunProgress, cancelled);
        _activeRun = null;
        _lastRunProgress = null;
        RefreshRunHistoryList();
    }

    private Control BuildMapInfo()
    {
        var info = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 24, 28),
            Padding = new Padding(12, 10, 12, 10),
        };
        var fit = Command("Вписать", (_, _) => _canvas.Fit());
        fit.Dock = DockStyle.Top;
        fit.AutoSize = false;
        fit.Height = 34;
        fit.Margin = Padding.Empty;

        _mapCursor.Dock = DockStyle.Top;
        _mapCursor.Height = 28;
        _mapCursor.AutoSize = false;
        _mapCursor.TextAlign = ContentAlignment.MiddleLeft;
        _mapCursor.Padding = new Padding(0, 8, 0, 0);
        _mapCursor.ForeColor = Color.FromArgb(196, 200, 208);
        _mapCursor.BackColor = Color.FromArgb(22, 24, 28);
        _mapCursor.Text = "Курсор на карте не выбран.";

        _summary.Dock = DockStyle.Fill;
        _summary.AutoSize = false;
        _summary.TextAlign = ContentAlignment.TopLeft;
        _summary.Padding = new Padding(0, 8, 0, 0);
        _summary.BackColor = Color.FromArgb(22, 24, 28);
        _summary.ForeColor = Color.FromArgb(196, 200, 208);

        info.Controls.Add(_summary);
        info.Controls.Add(_mapCursor);
        info.Controls.Add(fit);
        return info;
    }

    private Control BuildLibraryRail()
    {
        var rail = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 24, 28),
            Padding = new Padding(12, 8, 12, 8),
        };

        var mapsCaption = RailCaption("Библиотека");
        mapsCaption.Dock = DockStyle.Top;
        mapsCaption.Height = 26;

        var treeHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 280,
            Padding = new Padding(1),
            BackColor = Color.FromArgb(58, 64, 72),
        };
        treeHost.Controls.Add(BuildLibraryTree());

        var buttons = BuildLibraryButtons();
        buttons.Dock = DockStyle.Top;
        buttons.Height = 78;

        _blocksCaption = RailCaption("Блоки");
        _blocksCaption.Dock = DockStyle.Top;
        _blocksCaption.Height = 28;

        _palette = BuildPalette();
        _palette.Dock = DockStyle.Fill;

        rail.Controls.Add(_palette);
        rail.Controls.Add(_blocksCaption);
        rail.Controls.Add(buttons);
        rail.Controls.Add(treeHost);
        rail.Controls.Add(mapsCaption);
        return rail;
    }

    private static Label RailCaption(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(168, 174, 184),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };

    private Control BuildLibraryTree()
    {
        _libraryTree.Dock = DockStyle.Fill;
        _libraryTree.BackColor = Color.FromArgb(16, 18, 21);
        _libraryTree.ForeColor = Color.FromArgb(230, 232, 236);
        _libraryTree.BorderStyle = BorderStyle.None;
        _libraryTree.HideSelection = false;
        _libraryTree.FullRowSelect = true;
        _libraryTree.ShowLines = false;
        _libraryTree.ItemHeight = 26;
        _libraryTree.Indent = 18;
        _libraryTree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
        _libraryTree.DrawNode += DrawLibraryNode;
        _libraryTree.AfterSelect += (_, e) => OnLibrarySelected(e.Node);

        var maps = DarkMenu();
        maps.Items.Add(MenuItem("Убрать карту", (_, _) => RemoveSelectedMap()));

        var scenarios = DarkMenu();
        scenarios.Items.Add(MenuItem("Запустить  F6", (_, _) => StartScenario()));
        scenarios.Items.Add(MenuItem("Переименовать", (_, _) => RenameSelectedScenario()));
        scenarios.Items.Add(MenuItem("Убрать из списка", (_, _) => RemoveSelectedScenario()));
        scenarios.Items.Add(MenuItem("Удалить", (_, _) => DeleteSelectedScenario()));

        var chains = DarkMenu();
        chains.Items.Add(MenuItem("Запустить  F6", (_, _) => StartChain()));
        chains.Items.Add(MenuItem("Переименовать", (_, _) => RenameSelectedChain()));
        chains.Items.Add(MenuItem("Убрать из списка", (_, _) => RemoveSelectedChain()));
        chains.Items.Add(MenuItem("Удалить", (_, _) => DeleteSelectedChain()));

        var scenarioRoot = DarkMenu();
        scenarioRoot.Items.Add(MenuItem("Новый сценарий", (_, _) => CreateScenario(template: false)));
        scenarioRoot.Items.Add(MenuItem("Сценарий по шаблону", (_, _) => CreateScenario(template: true)));
        scenarioRoot.Items.Add(MenuItem("Загрузить сценарий...", (_, _) => LoadScenario()));

        var chainRoot = DarkMenu();
        chainRoot.Items.Add(MenuItem("Новая цепочка", (_, _) => CreateChain()));

        var mapRoot = DarkMenu();
        mapRoot.Items.Add(MenuItem("Загрузить карты...", (_, _) => AddMaps()));
        mapRoot.Items.Add(MenuItem("Снять с игровой сессии", (_, _) => CaptureMapFromSession()));

        var routeRoot = DarkMenu();
        routeRoot.Items.Add(MenuItem("Открыть папку маршрутов", (_, _) => OpenRoutesFolder()));

        _libraryTree.ItemDrag += (_, e) =>
        {
            if (e.Item is TreeNode node && node.Tag is ScenarioRecord scenario && !string.IsNullOrWhiteSpace(scenario.FilePath) && ViewingChainOverview)
                _libraryTree.DoDragDrop(DiagramCanvas.ScenarioRefPrefix + scenario.FilePath, DragDropEffects.Copy);
        };
        _libraryTree.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.Node is null)
                return;
            _libraryTree.SelectedNode = e.Node;
            if (e.Node.Tag is ScenarioRecord)
                scenarios.Show(_libraryTree, e.Location);
            else if (e.Node.Tag is ChainRecord)
                chains.Show(_libraryTree, e.Location);
            else if (e.Node.Tag is MapRecord)
                maps.Show(_libraryTree, e.Location);
            else if (e.Node.Tag is LibrarySection section)
            {
                if (section == LibrarySection.Scenarios)
                    scenarioRoot.Show(_libraryTree, e.Location);
                else if (section == LibrarySection.Chains)
                    chainRoot.Show(_libraryTree, e.Location);
                else if (section == LibrarySection.Routes)
                    routeRoot.Show(_libraryTree, e.Location);
                else
                    mapRoot.Show(_libraryTree, e.Location);
            }
        };
        return _libraryTree;
    }

    private static void OpenRoutesFolder()
    {
        string folder = MapLibrary.RoutesFolder;
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private Control BuildLibraryButtons()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 78,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.FromArgb(22, 24, 28),
            Padding = new Padding(0, 6, 0, 2),
            Margin = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.Controls.Add(RailButton("Карты", "Загрузить PNG в базу", (_, _) => AddMaps()), 0, 0);
        grid.Controls.Add(RailButton("Снять", "Снять миникарту с игровой сессии", (_, _) => CaptureMapFromSession()), 1, 0);
        grid.Controls.Add(RailButton("Сценарий", "Новый пустой сценарий", (_, _) => CreateScenario(template: false)), 0, 1);
        grid.Controls.Add(RailButton("Цепочка", "Новая цепочка сценариев", (_, _) => CreateChain()), 1, 1);
        return grid;
    }

    private Button RailButton(string text, string tip, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 4, 2),
            AutoSize = false,
            MinimumSize = new Size(0, 0),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        StyleButton(button);
        button.AutoSize = false;
        button.MinimumSize = new Size(0, 28);
        button.Padding = new Padding(4, 2, 4, 2);
        button.Margin = new Padding(0, 2, 4, 2);
        button.Click += onClick;
        _tips.SetToolTip(button, tip);
        return button;
    }

    private void DrawLibraryNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node is null)
            return;

        bool selected = ReferenceEquals(e.Node, _libraryTree.SelectedNode);
        var row = new Rectangle(0, e.Bounds.Y, _libraryTree.ClientSize.Width, e.Bounds.Height);
        using var background = new SolidBrush(selected ? Color.FromArgb(42, 78, 68) : Color.FromArgb(16, 18, 21));
        e.Graphics.FillRectangle(background, row);

        int textX = e.Node.Level * _libraryTree.Indent + 8;
        if (e.Node.Nodes.Count > 0)
        {
            var glyph = new Point(textX, e.Bounds.Y + e.Bounds.Height / 2);
            using var glyphBrush = new SolidBrush(Color.FromArgb(170, 176, 186));
            Point[] triangle = e.Node.IsExpanded
                ? [new Point(glyph.X, glyph.Y - 2), new Point(glyph.X + 8, glyph.Y - 2), new Point(glyph.X + 4, glyph.Y + 3)]
                : [new Point(glyph.X + 1, glyph.Y - 4), new Point(glyph.X + 1, glyph.Y + 4), new Point(glyph.X + 6, glyph.Y)];
            e.Graphics.FillPolygon(glyphBrush, triangle);
            textX += 14;
        }

        var textBounds = new Rectangle(textX, e.Bounds.Y, Math.Max(10, _libraryTree.ClientSize.Width - textX - 6), e.Bounds.Height);
        Font font = e.Node.NodeFont ?? _libraryTree.Font;
        Color color = selected ? Color.White : Color.FromArgb(226, 228, 232);
        TextRenderer.DrawText(e.Graphics, e.Node.Text, font, textBounds, color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    private Control BuildPalette()
    {
        var palette = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            AutoSize = false,
            BackColor = Color.FromArgb(22, 24, 28),
            Padding = new Padding(0, 4, 0, 0),
            Margin = Padding.Empty,
        };
        palette.Controls.Add(PaletteCard("follow", "Следовать маршруту", "Векторы или кратчайший путь"));
        palette.Controls.Add(PaletteCard("lock", "Зафиксироваться", "Клавиши, пока цель в радиусе"));
        palette.Controls.Add(PaletteCard("door", "Войти в дверь", "Дверь → сложность → Enter"));
        palette.Controls.Add(PaletteCard("openChest", "Открыть сундук", "Ближайший сундук, клик до пропажи"));
        palette.Controls.Add(PaletteCard("clickQuestMark", "Клик по знаку", "Вопрос или восклицание на карте"));
        palette.Controls.Add(PaletteCard("acceptDialog", "Принять диалог", "Зелёная галочка в окне квеста"));
        palette.Controls.Add(PaletteCard("mountHorses", "По кОням!", "O раз в секунду → полоска → ждём"));
        palette.Controls.Add(PaletteCard("sequence", "Нажатия", "Клики внутри окна игры"));
        palette.Controls.Add(PaletteCard("repeat", "Повтор", "Пока на карте есть красный"));
        palette.Resize += (_, _) => FitPaletteCards(palette);
        return palette;
    }

    private static void FitPaletteCards(FlowLayoutPanel palette)
    {
        int width = Math.Max(140, palette.ClientSize.Width - palette.Padding.Horizontal - 4);
        foreach (Control card in palette.Controls)
        {
            if (card.Width != width)
                card.Width = width;
        }
    }

    private Control PaletteCard(string kind, string title, string detail)
    {
        var card = new Panel
        {
            Height = 58,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.FromArgb(30, 33, 38),
            Cursor = Cursors.SizeAll,
        };
        card.Paint += (_, e) =>
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, card.Width - 2f, card.Height - 2f);
            using var fill = new SolidBrush(Color.FromArgb(30, 33, 38));
            using var path = RoundPalette(bounds, 10f);
            graphics.FillPath(fill, path);
            Color accent = BlockAppearance.Accent(kind);
            using var accentBrush = new SolidBrush(accent);
            graphics.FillRectangle(accentBrush, 0, 0, 4, card.Height);
            using var titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            using var detailFont = new Font("Segoe UI", 8f);
            using var titleBrush = new SolidBrush(Color.FromArgb(236, 238, 242));
            using var detailBrush = new SolidBrush(Color.FromArgb(150, 156, 166));
            graphics.DrawString(title, titleFont, titleBrush, 16f, 10f);
            graphics.DrawString(detail, detailFont, detailBrush, 16f, 32f);
        };

        Point? down = null;
        card.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                down = e.Location;
        };
        card.MouseMove += (_, e) =>
        {
            if (down is not Point start || e.Button != MouseButtons.Left)
                return;
            if (Math.Abs(e.X - start.X) + Math.Abs(e.Y - start.Y) < 5)
                return;
            down = null;
            card.DoDragDrop(kind, DragDropEffects.Copy);
        };
        card.MouseUp += (_, e) =>
        {
            if (down is null || e.Button != MouseButtons.Left)
                return;
            down = null;
            if (_scenarioRecord is null)
            {
                _status.Text = "Сначала создайте или загрузите сценарий для выбранной карты.";
                return;
            }

            AddBlock(CreateBlock(kind));
        };
        return card;
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundPalette(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2f;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void PlaceStudioSplitters()
    {
        if (_studioSplit is null || _workSplit is null || _inspectorSplit is null)
            return;

        int studioRoom = _studioSplit.Width - _studioSplit.SplitterWidth;
        TryPlaceSplitter(_studioSplit, 240, Math.Min(700, Math.Max(400, studioRoom / 2)), 288);

        if (_historySplit is not null && !_historySplit.Panel2Collapsed)
        {
            int historyRoom = _historySplit.Height - _historySplit.SplitterWidth;
            int historyHeight = Math.Min(180, Math.Max(120, historyRoom / 4));
            TryPlaceSplitter(_historySplit, 200, 120, historyRoom - historyHeight);
        }

        int workRoom = _workSplit.Width - _workSplit.SplitterWidth;
        TryPlaceSplitter(_workSplit, 340, 340, workRoom - 480);

        int available = _inspectorSplit.Height - _inspectorSplit.SplitterWidth;
        int mapHeight = available * 42 / 100;
        TryPlaceSplitter(_inspectorSplit, 160, 160, available - mapHeight);

        if (_mapSplit is not null)
        {
            int room = _mapSplit.Width - _mapSplit.SplitterWidth;
            if (room > 460)
                TryPlaceSplitter(_mapSplit, 220, 240, room - Math.Min(300, room - 220));
        }
    }

    private static void TryPlaceSplitter(SplitContainer split, int panel1Min, int panel2Min, int distance)
    {
        int room = split.Orientation == Orientation.Vertical
            ? split.Width - split.SplitterWidth
            : split.Height - split.SplitterWidth;
        if (room <= 0)
            return;

        panel1Min = Math.Clamp(panel1Min, 0, Math.Max(0, room - 1));
        panel2Min = Math.Clamp(panel2Min, 0, Math.Max(0, room - panel1Min));
        if (panel1Min + panel2Min > room)
        {
            panel2Min = Math.Max(0, room - panel1Min);
            if (panel1Min + panel2Min > room)
                return;
        }

        int min = panel1Min;
        int max = room - panel2Min;
        if (max < min)
            return;

        try
        {
            split.Panel1MinSize = panel1Min;
            split.Panel2MinSize = panel2Min;
            split.SplitterDistance = Math.Clamp(distance, min, max);
        }
        catch (ArgumentException)
        {
            // Контейнер ещё не выложен — повтор при следующем resize/show.
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void LoadLibrary()
    {
        _library = MapLibrary.Load();
        _libraryReady = true;
        RefreshLibraryTree();
        if (_library.Scenarios.Count > 0)
        {
            SelectScenario(_library.Scenarios[0]);
            return;
        }

        if (_library.Maps.Count > 0)
        {
            ShowMapOnly(_library.Maps[0]);
            _status.Text = "Карты загружены. Создайте сценарий — карта выбирается в блоке «Следовать маршруту».";
            return;
        }

        RefreshView();
        _status.Text = "Добавьте карты и создайте сценарий.";
    }

    private void AddMaps()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Загрузить карты в базу",
            Filter = "PNG (*.png)|*.png",
            Multiselect = true,
            InitialDirectory = InitialDirectory(_mapRecord?.SourcePath ?? _mapRecord?.ImagePath ?? _mapPath),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        MapRecord? first = null;
        int added = 0;
        foreach (string file in dialog.FileNames)
        {
            string full = Path.GetFullPath(file);
            if (_library.Maps.Any(item =>
                    string.Equals(item.ImagePath, full, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.SourcePath, full, StringComparison.OrdinalIgnoreCase)))
                continue;

            string stored;
            try
            {
                stored = MapLibrary.ImportMap(full);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Не удалось загрузить карту", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                continue;
            }

            var map = new MapRecord { ImagePath = stored, SourcePath = null };
            _library.Maps.Add(map);
            first ??= map;
            added++;
        }

        if (added == 0)
        {
            _status.Text = "Эти карты уже есть в списке.";
            return;
        }

        SaveLibrary();
        RefreshLibraryTree();
        if (_scenarioRecord is null && first is not null)
            ShowMapOnly(first);
        else if (first is not null && EditedFollow is FollowBlock)
            ShowBlockSettings();
        _status.Text = added == 1
            ? $"Карта загружена в базу: {Path.GetFileName(first!.ImagePath)}."
            : $"В базу загружено карт: {added}.";
    }

    private void CaptureMapFromSession()
    {
        FollowConfig config;
        try
        {
            config = FollowConfig.LoadOrCreate(FollowConfigPath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Конфиг ведения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool pinWindow = config.HasMapInClient && !string.IsNullOrWhiteSpace(config.GameWindowTitleContains);
        bool absoluteMap = config.MapRegion.Width >= 8 && config.MapRegion.Height >= 8;
        if (!pinWindow && !absoluteMap)
        {
            _status.Text = config.HasMapInClient
                ? "В follow-config.json укажите gameWindowTitleContains."
                : "В follow-config.json задайте mapInClient или mapRegion.";
            OpenFollowConfig();
            return;
        }

        Rectangle? client = null;
        if (pinWindow)
        {
            if (!GameWindowFocus.TryFocus(config.GameWindowTitleContains!, out Rectangle found))
            {
                _status.Text = "Окно игры не найдено. Запустите сессию и повторите.";
                return;
            }

            client = found;
        }

        Rectangle region = config.MapRectangle(client);
        Bitmap? bitmap = ScreenMapSource.Copy(region);
        if (bitmap is null)
        {
            _status.Text = "Не удалось снять миникарту. Проверьте область mapInClient / mapRegion.";
            return;
        }

        string temp = Path.Combine(Path.GetTempPath(), "walker-capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
        try
        {
            bitmap.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception exception)
        {
            bitmap.Dispose();
            MessageBox.Show(this, exception.Message, "Снимок карты", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            bitmap.Dispose();
        }

        string stored;
        try
        {
            stored = MapLibrary.ImportMap(temp);
        }
        catch (Exception exception)
        {
            try { File.Delete(temp); } catch { /* ignore */ }
            MessageBox.Show(this, exception.Message, "Снимок карты", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try { File.Delete(temp); } catch { /* ignore */ }

        MapRecord? map = _library.Maps.FirstOrDefault(item =>
            string.Equals(item.ImagePath, stored, StringComparison.OrdinalIgnoreCase));
        if (map is null)
        {
            map = new MapRecord { ImagePath = stored, SourcePath = "session-capture" };
            _library.Maps.Add(map);
        }

        SaveLibrary();
        RefreshLibraryTree();

        if (EditedFollow is FollowBlock follow)
        {
            PushUndo();
            follow.MapFile = stored;
            _dirty = true;
            if (_scenarioRecord is not null)
                _scenarioRecord.Dirty = true;
            ShowFollowBlockMap(follow);
            RebuildBlocks();
            ShowBlockSettings();
            _status.Text = $"Карта снята ({region.Width}×{region.Height}) и назначена блоку «Следовать маршруту».";
            return;
        }

        EnsureMapImage(map);
        _mapRecord = map;
        RefreshTitle();
        _status.Text = $"Карта снята: {Path.GetFileName(stored)} ({region.Width}×{region.Height}). Выберите её в блоке маршрута.";
    }

    private void TouchScenario()
    {
        _dirty = true;
        if (_scenarioRecord is not null)
            _scenarioRecord.Dirty = true;
        RefreshTitle();
        RefreshScenarioLabels();
    }

    private void CreateScenario(bool template)
    {
        if (!ConfirmLeaveDocument())
            return;

        string name = NextScenarioName();
        Scenario document = template ? BuildTemplate() : new Scenario { PressKeys = _pressKeys.Checked };
        document.PressKeys = _pressKeys.Checked;
        if (_mapPath is { Length: > 0 })
            document.MapFile = _mapPath;

        string path = MapLibrary.NewScenarioFilePath(name);
        try
        {
            ScenarioFile.Save(path, document);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Сценарий", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var record = new ScenarioRecord
        {
            Name = name,
            FilePath = path,
            Document = document,
        };
        _library.Scenarios.Add(record);
        SaveLibrary();
        SelectScenario(record);
        _status.Text = template
            ? $"Шаблон «{name}» создан. Карту для маршрута выберите в блоке «Следовать маршруту»."
            : $"Сценарий «{name}» пуст. Перетащите блоки и укажите карту в маршруте.";
    }

    private void LoadScenario()
    {
        if (!ConfirmLeaveDocument())
            return;

        using var dialog = new OpenFileDialog
        {
            Title = "Сценарий",
            Filter = "Сценарий (*.json)|*.json",
            InitialDirectory = MapLibrary.ScenariosFolder,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        Scenario document;
        try
        {
            document = ScenarioFile.Load(dialog.FileName, out bool legacy);
            if (legacy)
                CopyDefaultKeys(document);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось открыть сценарий", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string name = Path.GetFileNameWithoutExtension(dialog.FileName);
        string path;
        try
        {
            path = MapLibrary.ImportScenarioFile(dialog.FileName, name);
            MapLibrary.StoreBlockRoutes(document, document.MapFile, name);
            ScenarioFile.Save(path, document);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось сохранить сценарий в базу", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ScenarioRecord? existing = _library.Scenarios.FirstOrDefault(item =>
            string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new ScenarioRecord
            {
                Name = name,
                FilePath = path,
                Document = document,
            };
            _library.Scenarios.Add(existing);
        }
        else
        {
            existing.Document = document;
            existing.Dirty = false;
            existing.Name = name;
        }

        SaveLibrary();
        SelectScenario(existing);
        _status.Text = $"Сценарий открыт: {name}.";
    }

    private void RemoveSelectedMap()
    {
        MapRecord? map = _libraryTree.SelectedNode?.Tag as MapRecord ?? _mapRecord;
        if (map is null)
            return;
        if (MessageBox.Show(this, "Убрать карту из списка? Файл PNG на диске останется.", "Карты", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        _library.Maps.Remove(map);
        SaveLibrary();
        if (ReferenceEquals(map, _mapRecord))
            _mapRecord = null;

        RefreshLibraryTree();
        if (_scenarioRecord is not null)
        {
            ApplyPreviewMap(_scenarioRecord.Document);
            RefreshTitle();
            _status.Text = "Карта убрана из библиотеки.";
            return;
        }

        if (_library.Maps.Count > 0)
            ShowMapOnly(_library.Maps[0]);
        else
        {
            RefreshTitle();
            RefreshView();
            _status.Text = "Список карт пуст.";
        }
    }

    private void RemoveSelectedScenario()
    {
        ScenarioRecord? record = _libraryTree.SelectedNode?.Tag as ScenarioRecord ?? _scenarioRecord;
        if (record is null)
            return;
        if (MessageBox.Show(this, "Убрать сценарий из списка? Файл на диске останется.", record.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        DetachScenario(record);
        _status.Text = $"Сценарий «{record.Name}» убран из списка.";
    }

    private void DeleteSelectedScenario()
    {
        ScenarioRecord? record = _libraryTree.SelectedNode?.Tag as ScenarioRecord ?? _scenarioRecord;
        if (record is null)
        {
            _status.Text = "Сначала выберите сценарий.";
            return;
        }

        if (ReferenceEquals(record, _scenarioRecord) && PlaybackLocked)
        {
            _status.Text = "Остановите сценарий, чтобы удалить его.";
            return;
        }

        string question = string.IsNullOrWhiteSpace(record.FilePath)
            ? $"Удалить сценарий «{record.Name}»?"
            : $"Удалить сценарий «{record.Name}» вместе с файлом?";
        if (MessageBox.Show(this, question, "Удалить сценарий", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        if (!string.IsNullOrWhiteSpace(record.FilePath) && File.Exists(record.FilePath))
        {
            bool shared = _library.Scenarios.Any(other =>
                !ReferenceEquals(other, record)
                && string.Equals(other.FilePath, record.FilePath, StringComparison.OrdinalIgnoreCase));
            if (!shared)
            {
                try
                {
                    File.Delete(record.FilePath);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, exception.Message, "Не удалось удалить сценарий", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
        }

        DetachScenario(record);
        _status.Text = $"Сценарий «{record.Name}» удалён.";
    }

    private void DetachScenario(ScenarioRecord record)
    {
        if (ReferenceEquals(record, _scenarioRecord))
        {
            _dirty = false;
            record.Dirty = false;
        }

        _library.Scenarios.Remove(record);
        SaveLibrary();
        if (!ReferenceEquals(record, _scenarioRecord))
        {
            RefreshLibraryTree();
            return;
        }

        _scenarioRecord = null;
        if (_library.Scenarios.Count > 0)
        {
            SelectScenario(_library.Scenarios[0]);
            return;
        }

        if (_library.Maps.Count > 0)
            ShowMapOnly(_library.Maps[0]);
        else
        {
            RefreshLibraryTree();
            RefreshTitle();
            RefreshView();
        }
    }

    private void RenameSelectedScenario()
    {
        ScenarioRecord? record = _libraryTree.SelectedNode?.Tag as ScenarioRecord ?? _scenarioRecord;
        if (record is null)
            return;

        string? name = PromptText("Имя сценария", record.Name);
        if (string.IsNullOrWhiteSpace(name) || name == record.Name)
            return;
        record.Name = name.Trim();
        SaveLibrary();
        RefreshLibraryTree();
        RefreshTitle();
    }

    private void SelectScenario(ScenarioRecord record)
    {
        if (ReferenceEquals(record, _scenarioRecord) && !ViewingChainOverview)
        {
            ClearRouteOverlay();
            _libraryMapPreview = false;
            ApplyPreviewMap(record.Document);
            SyncLibrarySelection();
            RefreshView();
            return;
        }

        if (!ConfirmLeaveDocument())
        {
            SyncLibrarySelection();
            return;
        }

        _chainRecord = null;
        _chainStep = null;
        _editingScenarioFromChain = false;
        _chainDirty = false;
        _scenarioRecord = record;
        _scenario = record.Document;
        _routePath = record.FilePath;
        _dirty = record.Dirty;
        ClearRouteOverlay();
        _libraryMapPreview = false;
        _undo.Clear();
        _redo.Clear();
        _blockRow = record.Document.Blocks.Count > 0 ? 0 : -1;
        _selected = -1;
        _suppressBlocks = true;
        _loop.Checked = record.Document.Loop;
        _pressKeys.Checked = record.Document.PressKeys;
        _suppressBlocks = false;
        ApplyPreviewMap(record.Document);
        RefreshTitle();
        SyncLibrarySelection();
        RefreshView();
    }

    private void ApplyPreviewMap(Scenario document)
    {
        string? path = PreviewMapPath(document);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _mapRecord = null;
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
    }

    private static string? PreviewMapPath(Scenario document)
    {
        foreach (ScenarioBlock block in Scenario.Walk(document.Blocks))
        {
            if (block is FollowBlock follow && !string.IsNullOrWhiteSpace(follow.MapFile) && File.Exists(follow.MapFile))
                return follow.MapFile;
        }

        if (!string.IsNullOrWhiteSpace(document.MapFile) && File.Exists(document.MapFile))
            return document.MapFile;
        return null;
    }

    private void ShowMapOnly(MapRecord map)
    {
        if (!ConfirmLeaveDocument())
        {
            SyncLibrarySelection();
            return;
        }

        _mapRecord = map;
        _scenarioRecord = null;
        _chainRecord = null;
        _chainStep = null;
        _editingScenarioFromChain = false;
        _chainDirty = false;
        _routePath = null;
        _dirty = false;
        _scenario = new Scenario { PressKeys = _pressKeys.Checked };
        _undo.Clear();
        _redo.Clear();
        ClearRouteOverlay();
        _libraryMapPreview = true;
        _blockRow = -1;
        _selected = -1;
        _picked.Clear();
        EnsureMapImage(map);
        RefreshTitle();
        SyncLibrarySelection();
        RefreshView();
        _status.Text = $"Карта {Path.GetFileName(map.ImagePath)}. Создайте сценарий — карту укажите в блоке маршрута.";
    }

    private void ClearRouteOverlay()
    {
        _routeRecord = null;
        _routeOverlay = null;
    }

    private void InvalidateRoutesCache()
        => _routesCache = null;

    private List<RouteRecord> CachedRoutes()
        => _routesCache ??= MapLibrary.DiscoverRoutes();

    private void PreviewLibraryRoute(RouteRecord route)
    {
        RouteDocument document;
        try
        {
            document = RouteDocument.Load(route.FilePath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось открыть маршрут", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SyncLibrarySelection();
            return;
        }

        List<MapPoint> points;
        try
        {
            points = document.ToPoints();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось открыть маршрут", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SyncLibrarySelection();
            return;
        }

        MapRecord? map = ResolveRouteMap(route, document);
        if (_scenarioRecord is null && !ViewingChainOverview)
        {
            if (_chainRecord is not null)
            {
                if (!ConfirmLeaveDocument())
                {
                    SyncLibrarySelection();
                    return;
                }

                _chainRecord = null;
                _chainStep = null;
                _editingScenarioFromChain = false;
                _chainDirty = false;
                _routePath = null;
                _dirty = false;
                _scenario = new Scenario { PressKeys = _pressKeys.Checked };
                _blockRow = -1;
                _selected = -1;
                _picked.Clear();
            }

            if (map is not null)
            {
                _mapRecord = map;
                EnsureMapImage(map);
            }
        }
        else if (map is not null)
        {
            _mapRecord = map;
            EnsureMapImage(map);
        }

        _routeRecord = route;
        _routeOverlay = points;
        _libraryMapPreview = true;
        RefreshTitle();
        SyncLibrarySelection();
        RefreshView();
        _status.Text = points.Count == 0
            ? $"Маршрут «{route.Name}» пуст."
            : $"Маршрут «{route.Name}»: {points.Count} точек на карте.";
    }

    private MapRecord? ResolveRouteMap(RouteRecord route, RouteDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.MapFile))
        {
            try
            {
                string path = MapLibrary.ToFullPath(document.MapFile);
                MapRecord? byPath = _library.Maps.FirstOrDefault(item =>
                    string.Equals(item.ImagePath, path, StringComparison.OrdinalIgnoreCase));
                if (byPath is not null)
                    return byPath;

                string fileName = Path.GetFileName(path);
                MapRecord? byName = _library.Maps.FirstOrDefault(item =>
                    string.Equals(Path.GetFileName(item.ImagePath), fileName, StringComparison.OrdinalIgnoreCase));
                if (byName is not null)
                    return byName;
            }
            catch
            {
                // ignore bad MapFile
            }
        }

        if (!string.IsNullOrWhiteSpace(route.MapFolder))
        {
            MapRecord? byFolder = _library.Maps.FirstOrDefault(item =>
                string.Equals(MapLibrary.MapFolderKey(item.ImagePath), route.MapFolder, StringComparison.OrdinalIgnoreCase));
            if (byFolder is not null)
                return byFolder;
        }

        return _mapRecord;
    }

    private bool ConfirmLeaveScenario()
    {
        if (!_dirty || _scenarioRecord is null)
            return true;

        DialogResult answer = MessageBox.Show(
            this,
            $"Сохранить сценарий «{_scenarioRecord.Name}»?",
            "Конструктор сценариев",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel)
            return false;
        if (answer == DialogResult.Yes)
        {
            SaveRoute();
            return !_dirty;
        }

        if (_scenarioRecord.FilePath is { Length: > 0 } path && File.Exists(path))
        {
            try
            {
                _scenarioRecord.Document = ScenarioFile.Load(path, out bool legacy);
                if (legacy)
                    CopyDefaultKeys(_scenarioRecord.Document);
            }
            catch (Exception exception)
            {
                _status.Text = exception.Message;
            }
        }

        _scenarioRecord.Dirty = false;
        _dirty = false;
        return true;
    }

    private void EnsureMapImage(MapRecord map)
    {
        if (!File.Exists(map.ImagePath))
        {
            _status.Text = "Файл карты не найден: " + map.ImagePath;
            return;
        }

        if (string.Equals(_mapPath, map.ImagePath, StringComparison.OrdinalIgnoreCase) && _bitmap is not null)
            return;
        LoadMap(map.ImagePath);
    }

    private void RefreshLibraryTree()
    {
        InvalidateRoutesCache();
        string? topPath = NodePath(_libraryTree.TopNode);
        string? selectedPath = NodePath(_libraryTree.SelectedNode);

        _suppressLibrary = true;
        _libraryTree.BeginUpdate();
        _libraryTree.Nodes.Clear();

        var mapsRoot = new TreeNode("Карты") { Tag = LibrarySection.Maps };
        foreach (MapRecord map in _library.Maps)
        {
            string mapTitle = Path.GetFileNameWithoutExtension(map.ImagePath);
            if (!File.Exists(map.ImagePath))
                mapTitle += "  (нет файла)";
            mapsRoot.Nodes.Add(new TreeNode(mapTitle) { Tag = map });
        }

        var scenariosRoot = new TreeNode("Сценарии") { Tag = LibrarySection.Scenarios };
        foreach (ScenarioRecord scenario in _library.Scenarios)
        {
            var child = new TreeNode(ScenarioCaption(scenario)) { Tag = scenario };
            if (ReferenceEquals(scenario, _scenarioRecord) && !_editingScenarioFromChain)
                child.NodeFont = _treeBold;
            scenariosRoot.Nodes.Add(child);
        }

        var chainsRoot = new TreeNode("Цепочки") { Tag = LibrarySection.Chains };
        foreach (ChainRecord chain in _library.Chains)
        {
            var child = new TreeNode(ChainCaption(chain)) { Tag = chain };
            if (ReferenceEquals(chain, _chainRecord))
                child.NodeFont = _treeBold;
            chainsRoot.Nodes.Add(child);
        }

        var routesRoot = new TreeNode("Маршруты") { Tag = LibrarySection.Routes };
        var routeFolders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (RouteRecord route in CachedRoutes())
        {
            TreeNode parent = routesRoot;
            if (!string.IsNullOrWhiteSpace(route.MapFolder))
            {
                if (!routeFolders.TryGetValue(route.MapFolder, out TreeNode? folderNode))
                {
                    folderNode = new TreeNode(route.MapFolder) { Tag = LibrarySection.Routes };
                    routeFolders[route.MapFolder] = folderNode;
                    routesRoot.Nodes.Add(folderNode);
                }

                parent = folderNode;
            }

            var child = new TreeNode(route.Name) { Tag = route };
            if (_routeRecord is not null
                && string.Equals(_routeRecord.FilePath, route.FilePath, StringComparison.OrdinalIgnoreCase))
                child.NodeFont = _treeBold;
            parent.Nodes.Add(child);
        }

        _libraryTree.Nodes.Add(mapsRoot);
        _libraryTree.Nodes.Add(scenariosRoot);
        _libraryTree.Nodes.Add(chainsRoot);
        _libraryTree.Nodes.Add(routesRoot);
        mapsRoot.Expand();
        scenariosRoot.Expand();
        chainsRoot.Expand();
        routesRoot.Expand();
        foreach (TreeNode folder in routeFolders.Values)
            folder.Expand();

        TreeNode? selected = FindPreferredLibraryNode() ?? FindNodeByPath(selectedPath);
        if (selected is not null)
            _libraryTree.SelectedNode = selected;

        TreeNode? top = FindNodeByPath(topPath);
        if (top is not null)
            _libraryTree.TopNode = top;

        _libraryTree.EndUpdate();
        _suppressLibrary = false;
    }

    private TreeNode? FindPreferredLibraryNode()
    {
        if (_routeRecord is not null)
            return FindLibraryNode(_routeRecord);
        if (_libraryMapPreview && _mapRecord is not null)
            return FindLibraryNode(_mapRecord);
        if (_chainRecord is not null && !_editingScenarioFromChain)
            return FindLibraryNode(_chainRecord);
        if (_scenarioRecord is not null && !_editingScenarioFromChain)
            return FindLibraryNode(_scenarioRecord);
        return FindLibraryNode(_mapRecord);
    }

    private void SyncLibrarySelection()
    {
        if (!_libraryReady)
            return;

        if (_libraryTree.Nodes.Count == 0)
        {
            RefreshLibraryTree();
            return;
        }

        TreeNode? preferred = FindPreferredLibraryNode();

        _suppressLibrary = true;
        foreach (TreeNode root in _libraryTree.Nodes)
            UpdateLibraryNodeStyles(root);

        if (preferred is not null && !ReferenceEquals(_libraryTree.SelectedNode, preferred))
            _libraryTree.SelectedNode = preferred;
        _suppressLibrary = false;
    }

    private void UpdateLibraryNodeStyles(TreeNode node)
    {
        Font? font = null;
        if (node.Tag is ScenarioRecord scenario
            && ReferenceEquals(scenario, _scenarioRecord)
            && !_editingScenarioFromChain)
            font = _treeBold;
        else if (node.Tag is ChainRecord chain && ReferenceEquals(chain, _chainRecord))
            font = _treeBold;
        else if (node.Tag is RouteRecord route
            && _routeRecord is not null
            && string.Equals(route.FilePath, _routeRecord.FilePath, StringComparison.OrdinalIgnoreCase))
            font = _treeBold;
        else if (node.Tag is MapRecord map
            && _libraryMapPreview
            && ReferenceEquals(map, _mapRecord))
            font = _treeBold;

        if (node.Tag is ScenarioRecord s)
        {
            string caption = ScenarioCaption(s);
            if (node.Text != caption)
                node.Text = caption;
        }
        else if (node.Tag is ChainRecord c)
        {
            string caption = ChainCaption(c);
            if (node.Text != caption)
                node.Text = caption;
        }

        if (!ReferenceEquals(node.NodeFont, font))
            node.NodeFont = font;

        foreach (TreeNode child in node.Nodes)
            UpdateLibraryNodeStyles(child);
    }

    private static string? NodePath(TreeNode? node)
    {
        if (node is null)
            return null;
        var parts = new List<string>();
        for (TreeNode? current = node; current is not null; current = current.Parent)
            parts.Add(current.Text);
        parts.Reverse();
        return string.Join('\u001f', parts);
    }

    private TreeNode? FindNodeByPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        string[] parts = path.Split('\u001f');
        TreeNodeCollection nodes = _libraryTree.Nodes;
        TreeNode? current = null;
        foreach (string part in parts)
        {
            current = null;
            foreach (TreeNode node in nodes)
            {
                if (node.Text == part)
                {
                    current = node;
                    break;
                }
            }

            if (current is null)
                return null;
            nodes = current.Nodes;
        }

        return current;
    }

    private string ChainCaption(ChainRecord chain)
    {
        string caption = chain.Name + (chain.Dirty || (ReferenceEquals(chain, _chainRecord) && _chainDirty) ? " •" : "");
        if (_following && ReferenceEquals(chain, _chainRecord) && ViewingChainOverview)
            caption += "  · идёт";
        return caption;
    }

    private void RefreshScenarioLabels()
    {
        if (!_libraryReady)
            return;
        _suppressLibrary = true;
        foreach (TreeNode root in _libraryTree.Nodes)
        {
            if (root.Tag is not LibrarySection section || section != LibrarySection.Scenarios)
                continue;
            foreach (TreeNode child in root.Nodes)
            {
                if (child.Tag is not ScenarioRecord scenario)
                    continue;
                string caption = ScenarioCaption(scenario);
                if (child.Text != caption)
                    child.Text = caption;
                Font? font = ReferenceEquals(scenario, _scenarioRecord) ? _treeBold : null;
                if (!ReferenceEquals(child.NodeFont, font))
                    child.NodeFont = font;
            }
        }

        _suppressLibrary = false;
    }

    private string ScenarioCaption(ScenarioRecord scenario)
    {
        string caption = scenario.Name + (scenario.Dirty ? " •" : "");
        if (_following && ReferenceEquals(scenario, _scenarioRecord))
            caption += "  · идёт";
        return caption;
    }

    private TreeNode? FindLibraryNode(object? tag)
    {
        if (tag is null)
            return null;
        return FindLibraryNode(_libraryTree.Nodes, tag);
    }

    private static TreeNode? FindLibraryNode(TreeNodeCollection nodes, object tag)
    {
        foreach (TreeNode node in nodes)
        {
            if (LibraryTagsMatch(node.Tag, tag))
                return node;
            TreeNode? nested = FindLibraryNode(node.Nodes, tag);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static bool LibraryTagsMatch(object? nodeTag, object tag)
    {
        if (ReferenceEquals(nodeTag, tag))
            return true;
        if (nodeTag is RouteRecord left && tag is RouteRecord right)
            return string.Equals(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void OnLibrarySelected(TreeNode? node)
    {
        if (_suppressLibrary || node is null)
            return;
        if (node.Tag is ChainRecord chain)
            SelectChain(chain);
        else if (node.Tag is ScenarioRecord scenario)
            SelectScenario(scenario);
        else if (node.Tag is RouteRecord route)
            PreviewLibraryRoute(route);
        else if (node.Tag is MapRecord map)
        {
            if (_scenarioRecord is not null || ViewingChainOverview)
            {
                ClearRouteOverlay();
                _libraryMapPreview = true;
                _mapRecord = map;
                EnsureMapImage(map);
                RefreshTitle();
                SyncLibrarySelection();
                RefreshView();
                _status.Text = "Карта: " + Path.GetFileNameWithoutExtension(map.ImagePath) + ". Назначьте её блоку «Следовать маршруту».";
                return;
            }

            ShowMapOnly(map);
        }
    }

    private void RefreshTitle()
    {
        string map = _mapRecord is null ? "" : Path.GetFileNameWithoutExtension(_mapRecord.ImagePath);
        if (ViewingChainOverview && _chainRecord is not null)
        {
            Text = map.Length == 0
                ? $"Конструктор сценариев — цепочка {_chainRecord.Name}"
                : $"Конструктор сценариев — цепочка {_chainRecord.Name} — {map}";
            return;
        }

        if (_editingScenarioFromChain && _chainRecord is not null && _scenarioRecord is not null)
        {
            Text = $"Конструктор сценариев — {_chainRecord.Name} › {_scenarioRecord.Name}";
            return;
        }

        string scenario = _scenarioRecord?.Name ?? "";
        Text = scenario.Length == 0
            ? map.Length == 0
                ? "Конструктор сценариев"
                : $"Конструктор сценариев — {map}"
            : map.Length == 0
                ? $"Конструктор сценариев — {scenario}"
                : $"Конструктор сценариев — {scenario} — {map}";
    }

    private void SaveLibrary()
    {
        try
        {
            _library.Save();
        }
        catch (Exception exception)
        {
            _status.Text = "Список карт не сохранён: " + exception.Message;
        }
    }

    private Scenario BuildTemplate()
    {
        int pause = 2000;
        try
        {
            pause = FollowConfig.LoadOrCreate(FollowConfigPath).AntagonistPauseMilliseconds;
        }
        catch
        {
            // Шаблон соберётся с паузой по умолчанию.
        }

        Scenario scenario = Scenario.CreateTemplate(pause);
        foreach (ScenarioBlock block in Scenario.Walk(scenario.Blocks))
        {
            if (block is LockBlock locked && locked.Keys.Count == 0)
                locked.Keys.AddRange(CloneKeys(_defaultKeys));
        }

        return scenario;
    }

    private string NextScenarioName()
    {
        var names = _library.Scenarios.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("Сценарий"))
            return "Сценарий";
        for (int i = 2; i < 200; i++)
        {
            string candidate = $"Сценарий {i}";
            if (!names.Contains(candidate))
                return candidate;
        }

        return "Сценарий " + _library.Scenarios.Count;
    }

    private ScenarioBlock CreateBlock(string kind)
        => kind switch
        {
            "lock" => LockWithDefaults(),
            "door" => new DoorBlock(),
            "openChest" => new OpenChestBlock(),
            "clickQuestMark" => new ClickQuestMarkBlock(),
            "acceptDialog" => new AcceptDialogBlock(),
            "mountHorses" => new MountHorsesBlock(),
            "useMap" => UseMapWithDefault(),
            "sequence" => new SequenceBlock(),
            "repeat" => new RepeatBlock(),
            _ => FollowWithActiveMap(),
        };

    private FollowBlock FollowWithActiveMap()
        => new() { MapFile = _mapPath ?? _mapRecord?.ImagePath ?? _library.Maps.FirstOrDefault()?.ImagePath };

    private UseMapBlock UseMapWithDefault()
        => new() { MapFile = _mapPath ?? _mapRecord?.ImagePath ?? _library.Maps.FirstOrDefault()?.ImagePath };

    private LockBlock LockWithDefaults()
    {
        var locked = new LockBlock();
        locked.Keys.AddRange(CloneKeys(_defaultKeys));
        return locked;
    }

    private void EditBlockPause(ScenarioBlock block)
    {
        if (PlaybackLocked)
        {
            _status.Text = "Остановите ведение, чтобы менять паузу.";
            return;
        }

        string? text = PromptText("Пауза после блока, мс", block.PauseAfterMilliseconds.ToString(System.Globalization.CultureInfo.CurrentCulture));
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out int milliseconds))
        {
            _status.Text = "Пауза задаётся целым числом миллисекунд.";
            return;
        }

        milliseconds = Math.Clamp(milliseconds, 0, 120_000);
        if (milliseconds == block.PauseAfterMilliseconds)
            return;
        PushUndo();
        block.PauseAfterMilliseconds = milliseconds;
        TouchRoute();
        _status.Text = milliseconds == 0
            ? "Пауза между блоками снята."
            : $"Пауза между блоками: {milliseconds} мс.";
    }

    private void OnDiagramSelection(DiagramPick pick)
    {
        if (pick.Block is null)
        {
            _picked.Clear();
            SelectBlock(-1);
            return;
        }

        int row = RowOf(pick.Block);
        if (row < 0)
            return;

        if (pick.KeepIfPicked && _picked.Any(block => ReferenceEquals(block, pick.Block)))
        {
            SelectBlock(row);
            return;
        }

        if (pick.Range && _blockRow >= 0)
        {
            int start = Math.Min(_blockRow, row);
            int end = Math.Max(_blockRow, row);
            _picked.Clear();
            for (int i = start; i <= end && i < _rows.Count; i++)
                _picked.Add(_rows[i].Block);
            SelectBlock(row);
            return;
        }

        if (pick.Toggle)
        {
            int found = _picked.FindIndex(block => ReferenceEquals(block, pick.Block));
            if (found >= 0)
            {
                _picked.RemoveAt(found);
                if (_picked.Count == 0)
                    SelectBlock(-1);
                else if (ReferenceEquals(CurrentBlock, pick.Block))
                    SelectBlock(RowOf(_picked[^1]));
                else
                    RebuildBlocks();
                return;
            }

            _picked.Add(pick.Block);
            SelectBlock(row);
            return;
        }

        _picked.Clear();
        _picked.Add(pick.Block);
        SelectBlock(row);
    }

    private void InsertDropped(object? sender, DiagramDrop drop)
    {
        if (_scenarioRecord is null)
        {
            _status.Text = "Сначала создайте или загрузите сценарий.";
            return;
        }

        if (PlaybackLocked)
        {
            _status.Text = "Остановите ведение, чтобы менять схему.";
            return;
        }

        ScenarioBlock block = CreateBlock(drop.Kind);
        PushUndo();
        int index = Math.Clamp(drop.Index, 0, drop.Parent.Count);
        drop.Parent.Insert(index, block);
        _blockRow = RowOf(block);
        TouchRoute();
    }

    private void RelocateBlock(object? sender, DiagramMove move)
    {
        if (_scenarioRecord is null || PlaybackLocked)
            return;
        if (!TryLocate(move.Block, out List<ScenarioBlock> currentParent, out int currentIndex))
            return;
        if (HoldsList(move.Block, move.Parent))
            return;

        int index = Math.Clamp(move.Index, 0, move.Parent.Count);
        if (ReferenceEquals(currentParent, move.Parent) && (index == currentIndex || index == currentIndex + 1))
            return;

        PushUndo();
        currentParent.RemoveAt(currentIndex);
        if (ReferenceEquals(currentParent, move.Parent) && currentIndex < index)
            index--;
        index = Math.Clamp(index, 0, move.Parent.Count);
        move.Parent.Insert(index, move.Block);
        _picked.Clear();
        _picked.Add(move.Block);
        _blockRow = RowOf(move.Block);
        TouchRoute();
    }

    private void ShowBlockMenu(ScenarioBlock block, Point location)
    {
        var menu = DarkMenu();
        menu.Items.Add(MenuItem(_picked.Count > 1 ? $"Запустить выбранные  F8" : "Запустить блок  F8", (_, _) => StartSelectedBlock()));
        menu.Items.Add(MenuItem("Удалить", (_, _) => RemoveBlock()));
        menu.Show(_diagram, location);
    }

    private bool TryLocate(ScenarioBlock block, out List<ScenarioBlock> parent, out int index)
        => TryLocateIn(_scenario.Blocks, block, out parent, out index);

    private static bool TryLocateIn(List<ScenarioBlock> blocks, ScenarioBlock block, out List<ScenarioBlock> parent, out int index)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], block))
            {
                parent = blocks;
                index = i;
                return true;
            }

            if (blocks[i] is RepeatBlock repeat && TryLocateIn(repeat.Children, block, out parent, out index))
                return true;
        }

        parent = blocks;
        index = -1;
        return false;
    }

    private static bool HoldsList(ScenarioBlock block, List<ScenarioBlock> list)
    {
        if (block is not RepeatBlock repeat)
            return false;
        if (ReferenceEquals(repeat.Children, list))
            return true;
        foreach (ScenarioBlock child in repeat.Children)
        {
            if (HoldsList(child, list))
                return true;
        }

        return false;
    }

    private void SaveDrawnRoute()
    {
        FollowBlock? follow = RouteTarget(create: false);
        if (follow is null || follow.Anchors.All(anchor => anchor.Source != AnchorSource.Fixed))
        {
            _status.Text = "Сначала нарисуйте маршрут в блоке «Следовать маршруту».";
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Экспорт маршрута",
            Filter = "Маршрут (*.json)|*.json",
            FileName = SuggestedRouteFileName(),
            InitialDirectory = MapLibrary.RoutesFolderFor(_mapRecord?.ImagePath ?? _mapPath),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            CaptureRoute(follow, _mapRecord?.ImagePath ?? _mapPath).Save(dialog.FileName);
            follow.RouteFile = MapLibrary.IsInside(dialog.FileName, MapLibrary.RoutesFolder)
                ? Path.GetFullPath(dialog.FileName)
                : MapLibrary.ImportRoute(dialog.FileName, _mapRecord?.ImagePath ?? _mapPath);
            InvalidateRoutesCache();
            TouchRoute();
            RefreshLibraryTree();
            _status.Text = $"Маршрут экспортирован: {Path.GetFileName(follow.RouteFile)}.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось экспортировать маршрут", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenDrawnRoute()
    {
        if (_scenarioRecord is null)
        {
            _status.Text = "Сначала создайте или откройте сценарий.";
            return;
        }

        if (PlaybackLocked)
        {
            _status.Text = "Остановите ведение, чтобы загрузить маршрут.";
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Загрузить маршрут в блок «Следовать маршруту»",
            Filter = "Маршрут (*.json)|*.json",
            InitialDirectory = MapLibrary.RoutesFolderFor(_mapRecord?.ImagePath ?? _mapPath),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        RouteDocument document;
        try
        {
            document = RouteDocument.Load(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось загрузить маршрут", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (document.Points.Count == 0)
        {
            _status.Text = "В файле нет точек маршрута.";
            return;
        }

        FollowBlock? follow = RouteTarget(create: false);
        if (follow is not null && follow.Anchors.Count > 0 &&
            MessageBox.Show(this, "Заменить точки выбранного блока «Следовать маршруту»?", "Загрузить маршрут", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        PushUndo();
        follow = RouteTarget(create: true);
        if (follow is null)
            return;

        try
        {
            ApplyRoute(follow, document);
            follow.RouteFile = MapLibrary.ImportRoute(dialog.FileName, _mapRecord?.ImagePath ?? _mapPath);
            InvalidateRoutesCache();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось загрузить маршрут", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _blockRow = RowOf(follow);
        _selected = follow.Anchors.Count > 0 ? 0 : -1;
        TouchRoute();
        RefreshLibraryTree();
        _status.Text = $"Маршрут загружен в блок: {follow.Anchors.Count} точек ({Path.GetFileName(follow.RouteFile)}).";
    }

    private FollowBlock? RouteTarget(bool create)
    {
        if (EditedFollow is FollowBlock selected)
            return selected;

        foreach (ScenarioBlock block in _scenario.Blocks)
        {
            if (block is FollowBlock follow && follow.Travel == TravelMode.Vectors)
                return follow;
        }

        foreach (ScenarioBlock block in Scenario.Walk(_scenario.Blocks))
        {
            if (block is FollowBlock follow)
                return follow;
        }

        if (!create || _scenarioRecord is null)
            return null;

        var created = new FollowBlock();
        _scenario.Blocks.Insert(0, created);
        return created;
    }

    private static RouteDocument CaptureRoute(FollowBlock follow, string? mapFile)
    {
        var document = new RouteDocument { MapFile = mapFile };
        foreach (Anchor anchor in follow.Anchors)
        {
            if (anchor.Source != AnchorSource.Fixed)
                continue;

            document.Points.Add(new RoutePointDto
            {
                X = anchor.Position.X,
                Y = anchor.Position.Y,
                Keys = anchor.Keys.Count == 0
                    ? null
                    : anchor.Keys.Select(key => new RouteKeyDto
                    {
                        Key = key.Key.ToString(),
                        IntervalMilliseconds = key.Interval,
                    }).ToList(),
            });
        }

        return document;
    }

    private static void ApplyRoute(FollowBlock follow, RouteDocument document)
    {
        follow.Travel = TravelMode.Vectors;
        follow.Anchors.Clear();
        bool withoutKeys = document.Points.Count <= 1 || document.Points.Skip(1).All(point => point.Keys is null);
        for (int i = 0; i < document.Points.Count; i++)
        {
            RoutePointDto point = document.Points[i];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidDataException("В маршруте есть координата, которая не является числом.");

            var anchor = new Anchor
            {
                Source = AnchorSource.Fixed,
                Position = new MapPoint(point.X, point.Y),
            };
            if (!withoutKeys && i > 0 && point.Keys is { } stored)
            {
                foreach (RouteKeyDto item in stored)
                {
                    if (ScenarioFile.TryKey(item.Key, item.IntervalMilliseconds, out RouteKey key))
                        anchor.Keys.Add(key);
                }
            }

            follow.Anchors.Add(anchor);
        }
    }

    private string SuggestedRouteFileName()
    {
        string map = string.IsNullOrWhiteSpace(_mapPath) ? "route" : Path.GetFileNameWithoutExtension(_mapPath);
        return map + ".route.json";
    }

    private void StartScenario()
    {
        if (ViewingChainOverview)
        {
            StartChain();
            return;
        }

        if (_scenarioRecord is null)
        {
            _status.Text = "Нет сценария для запуска.";
            return;
        }

        _runningBlock = null;
        _selectionRun = false;
        BeginFollow(_scenario.Clone(), wholeScenario: true);
    }

    private void StartSelectedBlock()
    {
        if (ViewingChainOverview)
        {
            _status.Text = "В режиме цепочки F8 не используется — запустите цепочку целиком (F6).";
            return;
        }

        List<ScenarioBlock> blocks = BlocksToRun();
        if (blocks.Count == 0)
        {
            _status.Text = "Выберите блоки на схеме. Ctrl добавляет к выбору, Shift берёт диапазон.";
            return;
        }

        var run = new Scenario
        {
            Loop = false,
            PressKeys = _pressKeys.Checked,
            MapFile = _mapPath,
        };
        foreach (ScenarioBlock block in blocks)
            run.Blocks.Add(block.Clone());
        _runningBlock = blocks.Count == 1 ? blocks[0] : null;
        _selectionRun = blocks.Count > 1;
        BeginFollow(run, wholeScenario: false);
    }

    private List<ScenarioBlock> BlocksToRun()
    {
        var blocks = new List<ScenarioBlock>();
        foreach (BlockRow row in _rows)
        {
            if (!_picked.Any(block => ReferenceEquals(block, row.Block)))
                continue;
            if (AncestorPicked(row))
                continue;
            blocks.Add(row.Block);
        }

        if (blocks.Count == 0 && CurrentBlock is not null)
            blocks.Add(CurrentBlock);
        return blocks;
    }

    private bool AncestorPicked(BlockRow row)
    {
        foreach (BlockRow other in _rows)
        {
            if (other.Block is not RepeatBlock repeat || !ReferenceEquals(repeat.Children, row.Parent))
                continue;
            return _picked.Any(block => ReferenceEquals(block, repeat)) || AncestorPicked(other);
        }

        return false;
    }

    private void RememberScenarioDocument()
    {
        if (_scenarioRecord is null)
            return;
        _scenarioRecord.Document = _scenario;
        _scenarioRecord.Dirty = _dirty;
        _scenarioRecord.FilePath = _routePath;
    }

    private static ContextMenuStrip DarkMenu()
        => new()
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
            BackColor = Color.FromArgb(28, 28, 28),
            ForeColor = Color.White,
        };

    private static ToolStripMenuItem MenuItem(string text, EventHandler onClick)
        => new(text, null, onClick)
        {
            ForeColor = Color.White,
            BackColor = Color.FromArgb(28, 28, 28),
        };

    private string? PromptText(string title, string initial)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 108),
            BackColor = Color.FromArgb(32, 32, 32),
            ForeColor = Color.White,
            Font = Font,
        };
        var box = new TextBox
        {
            Text = initial,
            Dock = DockStyle.Top,
            Margin = new Padding(12),
            BackColor = Color.FromArgb(22, 22, 22),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(32, 32, 32),
        };
        var ok = new Button { Text = "ОК", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel };
        StyleButton(ok);
        StyleButton(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        form.Controls.Add(buttons);
        form.Controls.Add(box);
        form.Padding = new Padding(12, 12, 12, 0);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

internal enum LibrarySection
{
    Maps,
    Scenarios,
    Chains,
    Routes,
}

internal sealed class MapChoice
{
    public MapChoice(string path) => Path = path;

    public string Path { get; }

    public override string ToString()
        => System.IO.Path.GetFileNameWithoutExtension(Path);
}
