namespace WalkerRouteBuilder;

internal sealed partial class MainForm
{
    private ChainRecord? _chainRecord;
    private ChainStep? _chainStep;
    private ChainStep? _runningChainStep;
    private bool _editingScenarioFromChain;
    private Label? _breadcrumbs;
    private Label? _diagramHint;
    private Control? _palette;
    private Label? _blocksCaption;
    private bool _chainDirty;

    private bool ViewingChainOverview
        => _chainRecord is not null && !_editingScenarioFromChain;

    private void WireChainDiagram()
    {
        _diagram.StepSelected += OnChainStepSelected;
        _diagram.StepOpened += OpenChainStep;
        _diagram.InsertScenario += InsertChainScenario;
        _diagram.MoveStep += RelocateChainStep;
        _diagram.StepMenu += ShowChainStepMenu;
        _diagram.StepPauseRequested += EditChainStepPause;
    }

    private Control BuildBreadcrumbs()
    {
        _breadcrumbs = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            AutoSize = false,
            Padding = new Padding(16, 6, 8, 0),
            BackColor = Color.FromArgb(17, 19, 22),
            ForeColor = Color.FromArgb(196, 200, 208),
            Font = new Font("Segoe UI", 9.25f),
            Visible = false,
            Cursor = Cursors.Hand,
        };
        _breadcrumbs.Click += (_, _) => ExitChainDrillDown();
        return _breadcrumbs;
    }

    private void UpdateBreadcrumbs()
    {
        if (_breadcrumbs is null)
            return;
        if (_editingScenarioFromChain && _chainRecord is not null && _scenarioRecord is not null)
        {
            _breadcrumbs.Text = $"Цепочка «{_chainRecord.Name}»  ›  Сценарий «{_scenarioRecord.Name}»   (клик — назад к цепочке)";
            _breadcrumbs.Visible = true;
            return;
        }

        _breadcrumbs.Visible = false;
        _breadcrumbs.Text = "";
    }

    private void UpdatePaletteForMode()
    {
        bool chain = ViewingChainOverview;
        if (_palette is not null)
            _palette.Visible = !chain;
        if (_blocksCaption is not null)
            _blocksCaption.Text = chain ? "Цепочка" : "Блоки";
        if (_diagramHint is not null)
        {
            _diagramHint.Text = chain
                ? "Перетащите сценарий из библиотеки. Двойной клик — открыть сценарий. F6 — цепочка, F7 — стоп."
                : "Ctrl добавляет блок, Shift берёт диапазон. F6 — сценарий, F8 — выбранные, F7 — стоп.";
        }
    }

    private void CreateChain()
    {
        if (!ConfirmLeaveDocument())
            return;

        string name = NextChainName();
        var document = new ScenarioChain { Loop = false };
        string path = MapLibrary.NewChainFilePath(name);
        try
        {
            ChainFile.Save(path, document);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Цепочка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var record = new ChainRecord
        {
            Name = name,
            FilePath = path,
            Document = document,
        };
        _library.Chains.Add(record);
        SaveLibrary();
        SelectChain(record);
        _status.Text = $"Цепочка «{name}» пуста. Перетащите сценарии из библиотеки.";
    }

    private string NextChainName()
    {
        var used = new HashSet<string>(_library.Chains.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < 10_000; i++)
        {
            string name = "Цепочка " + i;
            if (!used.Contains(name))
                return name;
        }

        return "Цепочка";
    }

    private void RenameSelectedChain()
    {
        ChainRecord? record = _libraryTree.SelectedNode?.Tag as ChainRecord ?? _chainRecord;
        if (record is null)
            return;

        string? name = PromptText("Имя цепочки", record.Name);
        if (string.IsNullOrWhiteSpace(name) || name == record.Name)
            return;
        record.Name = name.Trim();
        SaveLibrary();
        RefreshLibraryTree();
        RefreshTitle();
        UpdateBreadcrumbs();
    }

    private void RemoveSelectedChain()
    {
        ChainRecord? record = _libraryTree.SelectedNode?.Tag as ChainRecord ?? _chainRecord;
        if (record is null)
            return;
        if (MessageBox.Show(this, "Убрать цепочку из списка? Файл на диске останется.", record.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        DetachChain(record, deleteFile: false);
        _status.Text = $"Цепочка «{record.Name}» убрана из списка.";
    }

    private void DeleteSelectedChain()
    {
        ChainRecord? record = _libraryTree.SelectedNode?.Tag as ChainRecord ?? _chainRecord;
        if (record is null)
        {
            _status.Text = "Сначала выберите цепочку.";
            return;
        }

        if (ReferenceEquals(record, _chainRecord) && PlaybackLocked)
        {
            _status.Text = "Остановите прогон, чтобы удалить цепочку.";
            return;
        }

        string question = string.IsNullOrWhiteSpace(record.FilePath)
            ? $"Удалить цепочку «{record.Name}»?"
            : $"Удалить цепочку «{record.Name}» вместе с файлом?";
        if (MessageBox.Show(this, question, "Удалить цепочку", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        if (!string.IsNullOrWhiteSpace(record.FilePath) && File.Exists(record.FilePath))
        {
            try
            {
                File.Delete(record.FilePath);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Не удалось удалить цепочку", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        DetachChain(record, deleteFile: true);
        _status.Text = $"Цепочка «{record.Name}» удалена.";
    }

    private void DetachChain(ChainRecord record, bool deleteFile)
    {
        _ = deleteFile;
        if (ReferenceEquals(record, _chainRecord))
        {
            _chainDirty = false;
            record.Dirty = false;
            _editingScenarioFromChain = false;
            _chainStep = null;
            _runningChainStep = null;
        }

        _library.Chains.Remove(record);
        SaveLibrary();
        if (!ReferenceEquals(record, _chainRecord))
        {
            RefreshLibraryTree();
            return;
        }

        _chainRecord = null;
        if (_library.Chains.Count > 0)
        {
            SelectChain(_library.Chains[0]);
            return;
        }

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

    private void SelectChain(ChainRecord record)
    {
        if (ReferenceEquals(record, _chainRecord) && !_editingScenarioFromChain)
        {
            ClearRouteOverlay();
            _libraryMapPreview = false;
            SyncLibrarySelection();
            RefreshView();
            return;
        }

        if (!ConfirmLeaveDocument())
        {
            SyncLibrarySelection();
            return;
        }

        _editingScenarioFromChain = false;
        _scenarioRecord = null;
        _routePath = null;
        _dirty = false;
        ClearRouteOverlay();
        _libraryMapPreview = false;
        _blockRow = -1;
        _selected = -1;
        _picked.Clear();
        _chainRecord = record;
        _chainStep = record.Document.Steps.FirstOrDefault();
        _chainDirty = record.Dirty;
        _undo.Clear();
        _redo.Clear();
        _suppressBlocks = true;
        _loop.Checked = record.Document.Loop;
        _suppressBlocks = false;
        RefreshTitle();
        SyncLibrarySelection();
        RefreshView();
        _status.Text = $"Цепочка «{record.Name}». Перетащите сценарии на схему.";
    }

    private void OpenChainStep(ChainStep step)
    {
        if (_chainRecord is null)
            return;

        ScenarioRecord? scenario = ResolveChainScenario(step);
        if (scenario is null)
        {
            _status.Text = "Файл сценария не найден. Шаг нельзя открыть.";
            _chainStep = step;
            RefreshView();
            return;
        }

        if (_dirty && _scenarioRecord is not null && !ConfirmLeaveScenario())
            return;

        _chainStep = step;
        _editingScenarioFromChain = true;
        _scenarioRecord = scenario;
        _scenario = scenario.Document;
        _routePath = scenario.FilePath;
        _dirty = scenario.Dirty;
        _undo.Clear();
        _redo.Clear();
        _blockRow = scenario.Document.Blocks.Count > 0 ? 0 : -1;
        _selected = -1;
        _picked.Clear();
        _suppressBlocks = true;
        _loop.Checked = scenario.Document.Loop;
        _pressKeys.Checked = scenario.Document.PressKeys;
        _suppressBlocks = false;
        ApplyPreviewMap(scenario.Document);
        RefreshTitle();
        RefreshLibraryTree();
        RefreshView();
        _status.Text = $"Сценарий «{scenario.Name}» внутри цепочки «{_chainRecord.Name}».";
    }

    private void ExitChainDrillDown()
    {
        if (!_editingScenarioFromChain || _chainRecord is null)
            return;
        if (_dirty && _scenarioRecord is not null && !ConfirmLeaveScenario())
            return;

        RememberScenarioDocument();
        _editingScenarioFromChain = false;
        _scenarioRecord = null;
        _routePath = null;
        _dirty = false;
        _blockRow = -1;
        _selected = -1;
        _picked.Clear();
        _suppressBlocks = true;
        _loop.Checked = _chainRecord.Document.Loop;
        _suppressBlocks = false;
        RefreshTitle();
        RefreshLibraryTree();
        RefreshView();
        _status.Text = $"Цепочка «{_chainRecord.Name}».";
    }

    private ScenarioRecord? ResolveChainScenario(ChainStep step)
        => _library.FindScenarioByPath(step.ScenarioFile);

    private ChainStepInfo DescribeChainStep(ChainStep step)
    {
        ScenarioRecord? record = ResolveChainScenario(step);
        if (record is null)
        {
            string missing = string.IsNullOrWhiteSpace(step.ScenarioFile)
                ? "нет файла"
                : Path.GetFileNameWithoutExtension(step.ScenarioFile) + " · нет файла";
            return new ChainStepInfo(missing, "Сценарий удалён или недоступен", Missing: true);
        }

        int blocks = Scenario.Walk(record.Document.Blocks).Count();
        string? map = PreviewMapPath(record.Document);
        string mapLabel = string.IsNullOrWhiteSpace(map)
            ? "без карты"
            : Path.GetFileNameWithoutExtension(map);
        return new ChainStepInfo(record.Name, $"{blocks} блоков · {mapLabel}", Missing: false);
    }

    private void OnChainStepSelected(ChainStep? step)
    {
        _chainStep = step;
        RefreshView();
    }

    private void InsertChainScenario(object? sender, ChainDiagramDrop drop)
    {
        if (_chainRecord is null || PlaybackLocked)
            return;

        ScenarioRecord? scenario = _library.FindScenarioByPath(drop.ScenarioFile);
        if (scenario?.FilePath is null)
        {
            _status.Text = "Сценарий из библиотеки не найден.";
            return;
        }

        var step = new ChainStep { ScenarioFile = scenario.FilePath };
        int index = Math.Clamp(drop.Index, 0, _chainRecord.Document.Steps.Count);
        _chainRecord.Document.Steps.Insert(index, step);
        _chainStep = step;
        TouchChain();
        RefreshView();
        _status.Text = $"В цепочку добавлен «{scenario.Name}».";
    }

    private void RelocateChainStep(object? sender, ChainDiagramMove move)
    {
        if (_chainRecord is null || PlaybackLocked)
            return;

        List<ChainStep> steps = _chainRecord.Document.Steps;
        int current = steps.IndexOf(move.Step);
        if (current < 0)
            return;

        int index = Math.Clamp(move.Index, 0, steps.Count);
        if (index == current || index == current + 1)
            return;

        steps.RemoveAt(current);
        if (current < index)
            index--;
        index = Math.Clamp(index, 0, steps.Count);
        steps.Insert(index, move.Step);
        _chainStep = move.Step;
        TouchChain();
        RefreshView();
    }

    private void ShowChainStepMenu(ChainStep step, Point location)
    {
        var menu = DarkMenu();
        menu.Items.Add(MenuItem("Открыть сценарий", (_, _) => OpenChainStep(step)));
        menu.Items.Add(MenuItem("Удалить шаг", (_, _) => RemoveChainStep(step)));
        menu.Show(_diagram, location);
    }

    private void RemoveChainStep(ChainStep? step = null)
    {
        if (_chainRecord is null || PlaybackLocked)
            return;
        step ??= _chainStep;
        if (step is null)
            return;

        _chainRecord.Document.Steps.Remove(step);
        if (ReferenceEquals(_chainStep, step))
            _chainStep = _chainRecord.Document.Steps.FirstOrDefault();
        TouchChain();
        RefreshView();
    }

    private void EditChainStepPause(ChainStep step)
    {
        if (_chainRecord is null || PlaybackLocked)
            return;

        string? text = PromptText("Пауза после шага, мс", step.PauseAfterMilliseconds.ToString(System.Globalization.CultureInfo.CurrentCulture));
        if (text is null)
            return;
        if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out int milliseconds))
        {
            _status.Text = "Нужно целое число миллисекунд.";
            return;
        }

        milliseconds = Math.Clamp(milliseconds, 0, 120_000);
        if (milliseconds == step.PauseAfterMilliseconds)
            return;
        step.PauseAfterMilliseconds = milliseconds;
        TouchChain();
        RefreshView();
    }

    private void TouchChain()
    {
        if (_chainRecord is null)
            return;
        _chainDirty = true;
        _chainRecord.Dirty = true;
        RefreshLibraryTree();
        RefreshTitle();
    }

    private void SaveChain()
    {
        if (_chainRecord is null)
            return;

        _chainRecord.Document.Loop = _loop.Checked;
        string path = _chainRecord.FilePath ?? MapLibrary.NewChainFilePath(_chainRecord.Name);
        try
        {
            ChainFile.Save(path, _chainRecord.Document);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Не удалось сохранить цепочку", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _chainRecord.FilePath = path;
        _chainRecord.Dirty = false;
        _chainDirty = false;
        SaveLibrary();
        RefreshLibraryTree();
        RefreshTitle();
        _status.Text = $"Цепочка сохранена: {_chainRecord.Name}.";
    }

    private bool ConfirmLeaveDocument()
    {
        if (_editingScenarioFromChain || _scenarioRecord is not null)
        {
            if (!ConfirmLeaveScenario())
                return false;
        }

        return ConfirmLeaveChain();
    }

    private bool ConfirmLeaveChain()
    {
        if (!_chainDirty || _chainRecord is null)
            return true;

        DialogResult answer = MessageBox.Show(
            this,
            $"Сохранить цепочку «{_chainRecord.Name}»?",
            "Конструктор сценариев",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel)
            return false;
        if (answer == DialogResult.Yes)
        {
            SaveChain();
            return !_chainDirty;
        }

        if (!string.IsNullOrWhiteSpace(_chainRecord.FilePath) && File.Exists(_chainRecord.FilePath))
        {
            try
            {
                _chainRecord.Document = ChainFile.Load(_chainRecord.FilePath);
            }
            catch
            {
                // оставляем текущий документ
            }
        }

        _chainRecord.Dirty = false;
        _chainDirty = false;
        return true;
    }

    private void RememberChainDocument()
    {
        if (_chainRecord is null)
            return;
        _chainRecord.Document.Loop = ViewingChainOverview ? _loop.Checked : _chainRecord.Document.Loop;
        _chainRecord.Dirty = _chainDirty;
    }

    private void RebuildChainView()
    {
        UpdateBreadcrumbs();
        UpdatePaletteForMode();
        if (!ViewingChainOverview || _chainRecord is null)
            return;

        _diagram.ShowChain(
            _chainRecord.Document,
            _chainStep,
            _runningChainStep,
            _chainRecord.Name,
            DescribeChainStep);
    }

    private void StartChain()
    {
        if (_chainRecord is null)
        {
            _status.Text = "Нет цепочки для запуска.";
            return;
        }

        if (_editingScenarioFromChain)
        {
            StartScenario();
            return;
        }

        if (_following)
            return;

        if (_timer.Enabled || _paused)
        {
            _status.Text = "Сначала остановите прогон точки.";
            return;
        }

        if (_chainRecord.Document.Steps.Count == 0)
        {
            _status.Text = "Цепочка пуста. Перетащите сценарии на схему.";
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
            return;
        }

        RememberChainDocument();
        _chainRecord.Document.Loop = _loop.Checked;
        config.PressKeys = _pressKeys.Checked;
        config.Restart.Enabled = _autoRestart.Checked;
        PersistFollowUiFlags();

        ScenarioChain running = _chainRecord.Document.Clone();
        running.Loop = _loop.Checked;
        int generation = ++_followGeneration;
        _followCts = new CancellationTokenSource();
        var token = _followCts.Token;
        var follower = new CharacterFollower();
        FileLog.Trace("run", $"mode=chain name={_chainRecord.Name} steps={running.Steps.Count} loop={running.Loop}");
        BeginRunHistory(wholeScenario: true, looping: running.Loop);
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
        _runningBlock = null;
        _selectionRun = false;
        UpdateButtons();
        RefreshView();

        bool pressKeys = _pressKeys.Checked;
        string chainName = _chainRecord.Name;
        List<ChainStep> steps = running.Steps.ToList();

        var thread = new Thread(() =>
        {
            try
            {
                do
                {
                    for (int i = 0; i < steps.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        ChainStep step = steps[i];
                        Post(() =>
                        {
                            _runningChainStep = step;
                            RefreshView();
                        });

                        Scenario? scenario = null;
                        string stepName = Path.GetFileNameWithoutExtension(step.ScenarioFile);
                        ScenarioRecord? live = ResolveChainScenario(step);
                        if (live is not null)
                        {
                            stepName = live.Name;
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(live.FilePath) && File.Exists(live.FilePath) && !live.Dirty)
                                    scenario = ScenarioFile.Load(live.FilePath).Clone();
                                else
                                    scenario = live.Document.Clone();
                            }
                            catch
                            {
                                scenario = live.Document.Clone();
                            }
                        }

                        if (scenario is null)
                        {
                            FileLog.Trace("run", $"chain step skip missing={step.ScenarioFile}");
                            Post(() => _status.Text = $"Шаг {i + 1}: нет файла «{stepName}», пропуск.");
                            continue;
                        }

                        scenario.Loop = false;
                        scenario.PressKeys = pressKeys;
                        if (scenario.Blocks.Count == 0)
                        {
                            Post(() => _status.Text = $"Шаг {i + 1}: сценарий «{stepName}» пуст, пропуск.");
                            FileLog.Trace("run", $"chain step skip empty={stepName}");
                            continue;
                        }

                        string label = stepName;
                        Post(() => _status.Text = $"Цепочка «{chainName}»: шаг {i + 1}/{steps.Count} — {label}");
                        FileLog.Trace("run", $"chain step={i + 1}/{steps.Count} name={label}");
                        FileLog.RunStarted(config, scenario, wholeScenario: true, label);
                        follower.Run(config, scenario, token);

                        if (i < steps.Count - 1 && step.PauseAfterMilliseconds > 0)
                        {
                            Post(() => _status.Text = $"Пауза между шагами: {step.PauseAfterMilliseconds} мс");
                            if (!WaitChainPause(step.PauseAfterMilliseconds, token))
                                break;
                        }
                    }
                }
                while (running.Loop && !token.IsCancellationRequested);
            }
            catch (OperationCanceledException)
            {
                FileLog.Trace("run", "chain stop cancelled");
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Post(() => _status.Text = exception.Message);
            }
            finally
            {
                FileLog.Trace("run", token.IsCancellationRequested ? "chain stop cancelled" : "chain stop finished");
                _following = false;
                Post(() =>
                {
                    if (generation != _followGeneration)
                        return;
                    _runningChainStep = null;
                    FinishRunHistory(token.IsCancellationRequested);
                    ClearRunProgress();
                    UpdateButtons();
                    RefreshView();
                    _status.Text = token.IsCancellationRequested
                        ? "Цепочка остановлена."
                        : "Цепочка завершена.";
                });
            }
        })
        {
            IsBackground = true,
            Name = "ScenarioChain",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static bool WaitChainPause(int milliseconds, CancellationToken token)
    {
        if (milliseconds <= 0)
            return true;
        return !token.WaitHandle.WaitOne(milliseconds);
    }

}
