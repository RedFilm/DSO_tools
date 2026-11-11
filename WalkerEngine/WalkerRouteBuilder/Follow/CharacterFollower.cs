using System.Globalization;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal sealed class CharacterFollower
{
    private const double SameSkullPixels = 28;
    private const double RedRetargetPixels = 4;
    private const int StuckExcludePixels = 5;

    private const int ChestStandPixels = 8;

    /// <summary>Радиус, в котором ищем остаток значка, закрытого зелёной точкой.</summary>
    private const int ChestCoverRadius = 16;

    /// <summary>Столько синих пикселей ещё значит, что сундук на месте. Соседняя дверь одна даёт меньше.</summary>
    private const int ChestCoverPixels = 24;

    private const double ChestArrivalPixels = 2;

    private const int SweepStepDegrees = 15;

    /// <summary>
    /// За один тик unstick маркер не может улететь дальше этого.
    /// Больший скачок — ложное срабатывание детектора (чужой зелёный пиксель), не сдвиг персонажа.
    /// </summary>
    private const double MaxUnstickJumpPixels = 64;

    private readonly AntagonistTracker _reds = new();
    private ColorImage? _redFrame;
    private List<AntagonistMark> _redConfirmed = [];
    private int _redRawCount;
    private readonly List<LapRecord> _laps = [];
    private int _lap;
    private long _runStarted;
    private long _lapStarted;
    private bool _looping;
    private int _lapChests;
    private int _lapReds;
    private int _lapSkipped;
    private bool _halt;
    private bool _doorActive;
    private bool _restartBusy;
    private bool _restarted;
    private long _minimapLostSince;

    public event Action<string>? Status;

    /// <summary>Счётчик кругов и тайминги текущего запуска сценария.</summary>
    public event Action<RunProgress>? Progress;

    public void Run(FollowConfig config, Scenario scenario, CancellationToken token)
    {
        try
        {
            _reds.Reset();
            _redFrame = null;
            _redConfirmed = [];
            _laps.Clear();
            _lap = 0;
            _lapChests = 0;
            _lapReds = 0;
            _lapSkipped = 0;
            _halt = false;
            _doorActive = false;
            _restartBusy = false;
            _restarted = false;
            _minimapLostSince = 0;
            _looping = scenario.Loop;
            _runStarted = Environment.TickCount64;
            _lapStarted = _runStarted;
            bool pinWindow = config.HasMapInClient && !string.IsNullOrWhiteSpace(config.GameWindowTitleContains);
            WaitForGame(config.StartDelayMilliseconds, pinWindow, token);
            if (token.IsCancellationRequested)
                return;
            if (!EnsureGameReady(config, pinWindow, token))
                return;

            do
            {
                _lap++;
                _lapChests = 0;
                _lapReds = 0;
                _lapSkipped = 0;
                _lapStarted = Environment.TickCount64;
                EmitProgress();
                FileLog.Trace("lap", $"start n={_lap} loop={_looping}");
                while (!token.IsCancellationRequested && !_halt)
                {
                    _restarted = false;
                    RunBlocks(config, scenario, scenario.Blocks, pinWindow, token);
                    if (!_restarted || token.IsCancellationRequested || _halt)
                        break;
                    Report("После перезапуска игры круг сценария начинается сначала.");
                    FileLog.Trace("lap", $"replay after restart n={_lap}");
                }

                if (token.IsCancellationRequested || _halt)
                    break;

                TimeSpan finished = TimeSpan.FromMilliseconds(Math.Max(0, Environment.TickCount64 - _lapStarted));
                var record = new LapRecord(finished, _lapChests, _lapReds, _lapSkipped);
                _laps.Add(record);
                FileLog.Trace("lap", $"done n={_lap} ms={(long)finished.TotalMilliseconds} chests={record.ChestsOpened} reds={record.RedsKilled} skipped={record.Skipped} total={Environment.TickCount64 - _runStarted}ms");
                _lapChests = 0;
                _lapReds = 0;
                _lapSkipped = 0;
                EmitProgress();
            }
            while (scenario.Loop && !token.IsCancellationRequested);

            EmitProgress();
        }
        finally
        {
            MouseClicker.Release();
        }
    }

    private void NoteChestOpened()
    {
        _lapChests++;
        EmitProgress();
    }

    private void NoteRedKilled()
    {
        _lapReds++;
        EmitProgress();
    }

    private void NoteSkipped()
    {
        _lapSkipped++;
        EmitProgress();
    }

    private void EmitProgress()
    {
        if (Progress is null || _runStarted == 0)
            return;

        long now = Environment.TickCount64;
        Progress.Invoke(new RunProgress(
            Lap: Math.Max(1, _lap),
            Looping: _looping,
            LapElapsed: TimeSpan.FromMilliseconds(Math.Max(0, now - _lapStarted)),
            TotalElapsed: TimeSpan.FromMilliseconds(Math.Max(0, now - _runStarted)),
            ChestsOpened: _lapChests,
            RedsKilled: _lapReds,
            Skipped: _lapSkipped,
            LastLap: _laps.Count > 0 ? _laps[^1] : null,
            Laps: _laps.ToArray()));
    }

    private void RunBlocks(
        FollowConfig config,
        Scenario scenario,
        IReadOnlyList<ScenarioBlock> blocks,
        bool pinWindow,
        CancellationToken token)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            if (token.IsCancellationRequested || _halt)
                return;

            ScenarioBlock block = blocks[i];
            FileLog.Trace("block", $"{i + 1}/{blocks.Count} {block.Title}");
            switch (block)
            {
                case FollowBlock follow:
                    RunFollow(config, scenario, follow, pinWindow, token);
                    break;
                case LockBlock locked:
                    RunLock(config, scenario, locked, pinWindow, token);
                    break;
                case DoorBlock door:
                    _doorActive = true;
                    _minimapLostSince = 0;
                    try
                    {
                        RunDoor(config, door, pinWindow, token);
                    }
                    finally
                    {
                        _doorActive = false;
                        _minimapLostSince = 0;
                    }
                    break;
                case OpenChestBlock chest:
                    RunOpenChest(config, chest, pinWindow, token);
                    break;
                case ClickQuestMarkBlock quest:
                    RunClickQuestMark(config, quest, pinWindow, token);
                    break;
                case AcceptDialogBlock dialog:
                    RunAcceptDialog(config, dialog, pinWindow, token);
                    break;
                case MountHorsesBlock mount:
                    RunMountHorses(config, mount, pinWindow, token);
                    break;
                case SequenceBlock sequence:
                    RunSequence(config, sequence, token);
                    break;
                case RepeatBlock repeat:
                    RunRepeat(config, scenario, repeat, pinWindow, token);
                    break;
                case UseMapBlock useMap:
                    RunUseMap(scenario, useMap);
                    break;
            }

            if (_restarted || token.IsCancellationRequested || _halt)
                return;
            if (i + 1 >= blocks.Count)
                return;
            if (!Wait(block.PauseAfterMilliseconds, left =>
                    $"Пауза между блоками {(left / 1000.0).ToString("0.0", CultureInfo.CurrentCulture)} с.", token))
                return;
        }
    }

    private void RunRepeat(
        FollowConfig config,
        Scenario scenario,
        RepeatBlock block,
        bool pinWindow,
        CancellationToken token)
    {
        if (block.Children.Count == 0)
            return;

        // Красный часто появляется уже после подхода: ждём первое подтверждение
        // (с таймаутом), иначе блок либо мгновенно скипается, либо висит вечно.
        if (block.Condition == RepeatCondition.WhileRed
            && !WaitUntilRed(config, block, pinWindow, token))
            return;

        for (int pass = 0; pass < 24 && !token.IsCancellationRequested; pass++)
        {
            FileLog.Trace("repeat", $"pass={pass + 1}/24 condition={block.Condition} children={block.Children.Count}");
            RunBlocks(config, scenario, block.Children, pinWindow, token);
            bool holds = !token.IsCancellationRequested && RepeatHolds(config, block, pinWindow, token);
            FileLog.Trace("repeat", $"pass={pass + 1} holds={holds}");
            if (!holds)
                return;
        }

        Report("Повтор остановлен: условие всё ещё выполняется.");
    }

    /// <summary>
    /// Ждёт подтверждённый красный. По таймауту / отмене — выход без детей (сценарий идёт дальше).
    /// </summary>
    private bool WaitUntilRed(FollowConfig config, RepeatBlock block, bool pinWindow, CancellationToken token)
    {
        int limit = Math.Clamp(block.WaitMilliseconds, 0, 120_000);
        if (limit <= 0)
        {
            bool now = RedVisible(config, pinWindow, token);
            FileLog.Trace("repeat", $"wait-red skip-wait visible={now}");
            if (!now)
                NoteSkipped();
            return now;
        }

        long deadline = Environment.TickCount64 + limit;
        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            long left = deadline - started;
            if (left <= 0)
            {
                string missed = limit % 1000 == 0
                    ? $"Красного нет {limit / 1000} с. Пропускаю повтор."
                    : $"Красного нет {limit} мс. Пропускаю повтор.";
                FileLog.Trace("repeat", $"wait-red timeout={limit}ms");
                NoteSkipped();
                Report(missed);
                return false;
            }

            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan)
                    || frame is null || scan is null)
                    return false;

                int reds = ConfirmedReds(frame, scan).Count;
                FileLog.Trace("repeat", $"wait-red confirmed={reds} raw={_redRawCount} left={left}ms hasMap={scan.HasMap} player={scan.PlayerFound}");
                if (reds > 0)
                {
                    const string ready = "Красный на карте — начинаю повтор.";
                    Publish(config, client, ready, Shot(scan, frame, null, null, null, client, $"repeat=ready reds={reds} raw={_redRawCount}"));
                    Report(ready);
                    return true;
                }

                string span = left >= 1000
                    ? $"{(left / 1000.0).ToString("0.0", CultureInfo.CurrentCulture)} с"
                    : $"{left} мс";
                string hold = _redRawCount > 0
                    ? $"Жду подтверждения красного (сырых {_redRawCount}, ещё {span})."
                    : $"Жду красный на карте для повтора (ещё {span}).";
                Publish(config, client, hold, Shot(scan, frame, null, null, null, client, $"repeat=wait raw={_redRawCount} left={left}ms"));
                Report(hold);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitRemainder(config, started, token))
                return false;
        }

        return false;
    }

    private void RunFollow(
        FollowConfig config,
        Scenario scenario,
        FollowBlock block,
        bool pinWindow,
        CancellationToken token)
    {
        if (block.Anchors.Count < 2)
        {
            Report($"{block.Title}: нужны хотя бы две точки.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(block.MapFile) && File.Exists(block.MapFile))
            scenario.MapFile = Path.GetFullPath(block.MapFile);

        if (!Wait(block.DelayMilliseconds, left =>
                $"{block.Title}: пауза {(left / 1000.0).ToString("0.0", CultureInfo.CurrentCulture)} с. Клавиши выключены.", token))
            return;

        if (block.Travel == TravelMode.Shortest)
        {
            for (int leg = 1; leg < block.Anchors.Count && !token.IsCancellationRequested; leg++)
                RunLeg(config, scenario, block, leg, pinWindow, token, shortest: true);
            return;
        }

        RunLeg(config, scenario, block, leg: -1, pinWindow, token, shortest: false);
    }

    private void RunLeg(
        FollowConfig config,
        Scenario scenario,
        FollowBlock block,
        int leg,
        bool pinWindow,
        CancellationToken token,
        bool shortest)
    {
        RoutePilot? pilot = null;
        List<MapPoint>? path = null;
        MapPoint? trackedRed = null;
        var keys = new List<ScheduledKey>();
        var blocked = new List<MapPoint>();
        int segment = int.MinValue;
        MapPoint stillAt = default;
        long stillSince = 0;
        bool stillReady = false;
        long missingSince = 0;
        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return;

                if (!scan!.HasMap || !scan.PlayerFound)
                {
                    string hidden = $"{block.Title}: карта или персонаж не видны.";
                    Publish(config, client, hidden, Shot(scan, frame, null, trackedRed, path, client, $"mode={(shortest ? "short" : "vectors")} leg={leg}"));
                    Report(hidden);
                }
                else
                {
                    var position = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                    WatchStill(config, scan.Bounds, position, ref stillAt, ref stillSince, ref stillReady);
                    bool retargeted = false;
                    string? waiting = null;
                    bool missing = false;
                    AnchorSource? live = LiveAnchor(block, leg, shortest);
                    if (live is AnchorSource kind)
                    {
                        retargeted = TryFollowRed(
                            config, scenario, block, leg, shortest, frame!, scan, position, kind, blocked,
                            ref path, ref trackedRed, ref pilot, ref segment, out waiting, out missing);
                    }

                    Anchor? sought = WaitedGoal(block, leg, shortest);
                    bool chasing = path is not null && trackedRed is not null;
                    if (sought is not null && missing)
                    {
                        // В пути — свой короткий лимит (не Skip и не «до появления»).
                        // До пути — обычный GoalWait якоря.
                        bool elapsed = chasing
                            ? LostWhileChasingElapsed(block.ChaseLostMilliseconds, ref missingSince)
                            : sought.Wait != GoalWait.UntilAppears
                                && MissingGoalElapsed(sought, ref missingSince);
                        if (elapsed)
                        {
                            string gone = chasing
                                ? LostWhileChasingText(sought, block.ChaseLostMilliseconds)
                                : MissingGoalText(sought);
                            long goneFor = missingSince == 0 ? 0 : Environment.TickCount64 - missingSince;
                            Publish(config, client, gone, Shot(scan, frame, position, trackedRed, path, client,
                                $"mode={(shortest ? "short" : "vectors")} leg={leg} missing={goneFor}ms chase={chasing}"));
                            NoteSkipped();
                            Report(gone);
                            return;
                        }
                    }
                    else
                    {
                        missingSince = 0;
                    }

                    if (live is AnchorSource goal)
                    {
                        if (pilot is null || path is null)
                        {
                            NoPathRecovery recovery = RecoverNoPath(
                                config, scenario, block, leg, shortest, pinWindow, token, frame!, scan, position,
                                waiting, blocked, ref path, ref pilot, ref segment, ref trackedRed,
                                ref stillAt, ref stillSince, ref stillReady);
                            if (recovery == NoPathRecovery.Stop)
                                return;
                            if (recovery == NoPathRecovery.Continue)
                            {
                                if (!WaitMove(config, started, token))
                                    return;
                                continue;
                            }

                            string hold = waiting ?? $"{block.Title}: {WaitFor(goal)}";
                            Publish(config, client, hold, Shot(scan, frame, position, trackedRed, path, client, $"mode={(shortest ? "short" : "vectors")} leg={leg}"));
                            Report(hold);
                            if (waiting is not null && waiting.StartsWith("В сценарии нет", StringComparison.Ordinal))
                                return;
                        }
                    }
                    else if (pilot is null || path is null)
                    {
                        if (!TryBuildPath(config, scenario, block, leg, shortest, frame!, scan, position, null, null, blocked, out path, out waiting))
                        {
                            NoPathRecovery recovery = RecoverNoPath(
                                config, scenario, block, leg, shortest, pinWindow, token, frame!, scan, position,
                                waiting, blocked, ref path, ref pilot, ref segment, ref trackedRed,
                                ref stillAt, ref stillSince, ref stillReady);
                            if (recovery == NoPathRecovery.Stop)
                                return;
                            if (recovery == NoPathRecovery.Continue)
                            {
                                if (!WaitMove(config, started, token))
                                    return;
                                continue;
                            }

                            string hold = waiting ?? $"{block.Title}: жду точку.";
                            Publish(config, client, hold, Shot(scan, frame, position, trackedRed, path, client, $"mode={(shortest ? "short" : "vectors")} leg={leg}"));
                            Report(hold);
                            if (waiting is not null && waiting.StartsWith("В сценарии нет", StringComparison.Ordinal))
                                return;
                        }
                        else
                        {
                            pilot = new RoutePilot(Tuning(config, block, leg, shortest));
                            segment = int.MinValue;
                        }
                    }

                    if (pilot is not null && path is not null)
                    {
                        var command = pilot.Advance(true, position, scan.Bounds, path);
                        bool recovered = false;
                        if (config.StuckRecoveryMilliseconds > 0
                            && stillReady
                            && !command.Arrived
                            && Environment.TickCount64 - stillSince >= config.StuckRecoveryMilliseconds)
                        {
                            recovered = true;
                            UnstickResult result = Unstick(
                                config, scenario, block, leg, shortest, pinWindow, token, command.HeadingDegrees,
                                stillAt, blocked, ref path, ref pilot, ref segment, ref trackedRed);
                            if (result == UnstickResult.Stop)
                                return;
                            stillAt = position;
                            stillSince = Environment.TickCount64;
                            stillReady = true;
                        }
                        else if (command.Click)
                            Click(config, command, client);

                        if (recovered)
                        {
                            if (!WaitMove(config, started, token))
                                return;
                            continue;
                        }

                        string keysNote = " Клавиши выключены.";
                        if (scenario.PressKeys)
                        {
                            int keySegment = shortest ? leg - 1 : command.SegmentIndex;
                            if (keySegment != segment)
                            {
                                segment = keySegment;
                                keys = Schedule(KeysOf(block, keySegment));
                            }

                            string pressed = PressDue(config, keys, client, token);
                            keysNote = keys.Count == 0
                                ? " Клавиши не заданы."
                                : pressed.Length == 0 ? "" : $" Нажато: {pressed}.";
                        }

                        string shift = !retargeted
                            ? ""
                            : live switch
                            {
                                AnchorSource.Chest => " Сундук сместился, путь обновлён.",
                                AnchorSource.QuestQuestion => " Знак вопроса сместился, путь обновлён.",
                                AnchorSource.QuestExclaim => " Знак восклицания сместился, путь обновлён.",
                                _ => " Красный сместился, путь обновлён.",
                            };
                        string text = $"{block.Title}. {Describe(command)}{keysNote}{shift}";
                        Publish(config, client, text, Shot(scan, frame, position, trackedRed, path, client,
                            $"mode={(shortest ? "short" : "vectors")} leg={leg} remain={Num(command.RemainingPixels)} cross={Num(command.CrossTrackPixels)} " +
                            $"click={command.Click} off={Num(command.OffsetX)},{Num(command.OffsetY)} heading={Num(command.HeadingDegrees)} " +
                            $"arrived={command.Arrived} stuck={command.Stuck} correcting={command.Correcting} seg={command.SegmentIndex}"));
                        Report(text);
                        if (command.Arrived)
                            return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitMove(config, started, token))
                return;
        }
    }

    private void RunDoor(FollowConfig config, DoorBlock block, bool pinWindow, CancellationToken token)
    {
        const double approach = 16;
        const int knockLimit = 12_000;
        const int enterAgain = 800;
        // Пауза после появления Enter: окно ещё дорисовывается после клика по двери.
        const int enterSettle = 500;
        // Пауза после клика по сложности, прежде чем жать Enter.
        const int difficultySettle = 350;
        const int loadLimit = 45_000;
        const int returnLimit = 45_000;
        // Карта и персонаж должны держаться без полосы загрузки — иначе следующий блок стартует слишком рано.
        const int readyHold = 900;
        const int retryLimit = 3;
        const int retrySettle = 800;

        const double stick = 28;

        int difficulty = DoorDifficulties.Clamp(block.Difficulty);
        RoutePilot? pilot = null;
        List<MapPoint>? path = null;
        MapPoint? aimed = null;
        MapPoint? trackedDoor = null;
        var blocked = new List<MapPoint>();
        MapPoint stillAt = default;
        long stillSince = 0;
        bool stillReady = false;
        double? aimHeading = null;
        long knockSince = 0;
        long enteredAt = 0;
        long enterReadyAt = 0;
        long difficultyAt = 0;
        long readySince = 0;
        long retryAt = 0;
        int retries = 0;
        bool knocked = false;
        bool difficultyPicked = false;
        bool entered = false;
        bool sawGap = false;
        bool sawLoading = false;
        while (!token.IsCancellationRequested && !_halt)
        {
            long started = Environment.TickCount64;
            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return;

                Rectangle view = config.ViewRectangle(client);
                ColorImage? window = null;
                try
                {
                    window = ScreenMapSource.Capture(view);
                }
                catch (Exception exception)
                {
                    FileLog.Trace("door", $"view {exception.Message}");
                }

                Rectangle map = config.MapRectangle(client);
                var excluded = new MapBounds(map.Left - view.Left, map.Top - view.Top, map.Right - view.Left - 1, map.Bottom - view.Top - 1);
                bool loading = window is not null && EntranceFinder.HasLoadingBar(window);
                bool hasMap = scan!.HasMap && scan.PlayerFound;
                int buttonX = 0;
                int buttonY = 0;
                bool button = window is not null && EntranceFinder.TryFindButton(window, excluded, out buttonX, out buttonY);
                int retryX = 0;
                int retryY = 0;
                bool retry = window is not null && EntranceFinder.TryFindRetryButton(window, out retryX, out retryY);

                if (entered)
                {
                    if (retry)
                    {
                        if (retries >= retryLimit)
                        {
                            const string fail = "Соединение не установилось после 3 попыток. Сценарий остановлен.";
                            Publish(config, client, fail, Shot(scan, frame, null, null, null, client, $"retry={retries}/{retryLimit} at={retryX},{retryY}"));
                            Report(fail);
                            FileLog.Trace("run", "halt door-retry-exhausted");
                            _halt = true;
                            return;
                        }

                        if (Environment.TickCount64 - retryAt >= retrySettle)
                        {
                            retries++;
                            ClickScreen(config, view, retryX, retryY, $"retry={retries}/{retryLimit}");
                            retryAt = Environment.TickCount64;
                            enteredAt = retryAt;
                            sawGap = false;
                            sawLoading = false;
                            readySince = 0;
                            string text = $"Нет соединения. Новая попытка {retries}/{retryLimit}.";
                            Publish(config, client, text, Shot(scan, frame, null, null, null, client, $"retry={retries}/{retryLimit} at={retryX},{retryY}"));
                            Report(text);
                        }
                        else
                        {
                            string waitRetry = $"Жду перед новой попыткой… {retries}/{retryLimit}.";
                            Publish(config, client, waitRetry, Shot(scan, frame, null, null, null, client, $"retry settle={Environment.TickCount64 - retryAt}ms at={retryX},{retryY}"));
                            Report(waitRetry);
                        }

                        if (!WaitMove(config, started, token))
                            return;
                        continue;
                    }

                    if (loading)
                        sawLoading = true;
                    // Переход: полоса загрузки (серая/зелёная) или карта пропала.
                    if (loading || !hasMap)
                    {
                        sawGap = true;
                        readySince = 0;
                    }
                    else if (sawGap)
                    {
                        if (readySince == 0)
                            readySince = Environment.TickCount64;
                        if (Environment.TickCount64 - readySince >= readyHold)
                        {
                            const string done = "Новая локация открыта. Шаг входа закончен.";
                            Publish(config, client, done, Shot(scan, frame, null, null, null, client, $"door=done loadingSeen={sawLoading} retries={retries}"));
                            Report(done);
                            return;
                        }
                    }

                    if (!sawGap && button && Environment.TickCount64 - enteredAt >= enterAgain)
                    {
                        ClickScreen(config, view, buttonX, buttonY);
                        enteredAt = Environment.TickCount64;
                        const string again = "Окно входа ещё открыто. Нажимаю Enter.";
                        Publish(config, client, again, Shot(scan, frame, null, null, null, client, $"enter={buttonX},{buttonY}"));
                        Report(again);
                    }
                    else if (Environment.TickCount64 - enteredAt >= (sawGap ? returnLimit : loadLimit))
                    {
                        string fail = sawGap
                            ? "Новая карта не появилась. Шаг входа закончен."
                            : "После Enter локация не сменилась. Шаг входа закончен.";
                        Publish(config, client, fail, Shot(scan, frame, null, null, null, client, $"door gap={sawGap} loading={loading} loadingSeen={sawLoading} retries={retries}"));
                        Report(fail);
                        return;
                    }
                    else
                    {
                        string text = loading || (sawGap && !hasMap)
                            ? "Жду прогрузку локации (полоса / нет карты)."
                            : sawGap
                                ? $"Жду стабильную карту… {(Environment.TickCount64 - readySince) / 1000.0:0.0} с."
                                : "Жду загрузку локации.";
                        Publish(config, client, text, Shot(scan, frame, null, null, null, client, $"door gap={sawGap} loading={loading} map={hasMap} ready={readySince}"));
                        Report(text);
                    }
                }
                else if (button && knocked)
                {
                    if (enterReadyAt == 0)
                        enterReadyAt = Environment.TickCount64;
                    if (!difficultyPicked)
                    {
                        if (Environment.TickCount64 - enterReadyAt < enterSettle)
                        {
                            string settle = $"Окно входа открыто. Жду перед выбором сложности ({DoorDifficulties.Name(difficulty)}).";
                            Publish(config, client, settle, Shot(scan, frame, null, null, null, client, $"enter settle={Environment.TickCount64 - enterReadyAt}ms at={buttonX},{buttonY}"));
                            Report(settle);
                        }
                        else
                        {
                            EntranceFinder.DifficultyPoint(buttonX, buttonY, difficulty, out int difficultyX, out int difficultyY);
                            ClickScreen(config, view, difficultyX, difficultyY, $"difficulty={DoorDifficulties.Name(difficulty)}");
                            difficultyPicked = true;
                            difficultyAt = Environment.TickCount64;
                            string text = $"Окно входа открыто. Выбираю сложность: {DoorDifficulties.Name(difficulty)}.";
                            Publish(config, client, text, Shot(scan, frame, null, null, null, client, $"difficulty={difficulty} at={difficultyX},{difficultyY} enter={buttonX},{buttonY}"));
                            Report(text);
                        }
                    }
                    else if (Environment.TickCount64 - difficultyAt < difficultySettle)
                    {
                        string settle = $"Сложность {DoorDifficulties.Name(difficulty)} выбрана. Жду перед Enter.";
                        Publish(config, client, settle, Shot(scan, frame, null, null, null, client, $"difficulty settle={Environment.TickCount64 - difficultyAt}ms"));
                        Report(settle);
                    }
                    else
                    {
                        ClickScreen(config, view, buttonX, buttonY);
                        entered = true;
                        enteredAt = Environment.TickCount64;
                        const string text = "Окно входа открыто. Нажимаю Enter.";
                        Publish(config, client, text, Shot(scan, frame, null, null, null, client, $"enter={buttonX},{buttonY}"));
                        Report(text);
                    }
                }
                else if (!hasMap)
                {
                    string hidden = $"{block.Title}: карта или персонаж не видны.";
                    Publish(config, client, hidden, Shot(scan, frame, null, null, null, client, "door"));
                    Report(hidden);
                }
                else
                {
                    var position = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                    DoorMark? seen = trackedDoor is MapPoint held
                        ? MatchingDoor(frame!, scan, held)
                        : NearestDoor(frame!, scan, position);
                    bool covered = false;
                    if (seen is DoorMark visible)
                        trackedDoor = visible.Point;
                    else if (trackedDoor is MapPoint remembered && PathCourse.Distance(scan.Bounds, position, remembered) <= stick)
                        covered = true;
                    else
                    {
                        trackedDoor = null;
                        path = null;
                        pilot = null;
                        aimed = null;
                        blocked.Clear();
                        knockSince = 0;
                        if (NearestDoor(frame!, scan, position) is DoorMark other)
                            trackedDoor = other.Point;
                    }

                    if (trackedDoor is not MapPoint goalPoint)
                    {
                        const string wait = "Жду дверь на карте.";
                        Publish(config, client, wait, Shot(scan, frame, position, null, path, client, "doors=0"));
                        Report(wait);
                    }
                    else
                    {
                        WatchStill(config, scan.Bounds, position, ref stillAt, ref stillSince, ref stillReady);
                        double distance = PathCourse.Distance(scan.Bounds, position, goalPoint);
                        bool close = covered || distance <= approach;
                        if (!close)
                        {
                            knockSince = 0;
                            bool shifted = aimed is not MapPoint known || PathCourse.Distance(scan.Bounds, known, goalPoint) > 10;
                            if (shifted || path is null || pilot is null)
                            {
                                if (MapPathfinder.TryFind(scan, frame!, position, goalPoint, out List<MapPoint> found, config.WallClearancePixels, blocked, StuckExcludePixels) && found.Count >= 2)
                                {
                                    path = found;
                                    aimed = goalPoint;
                                    pilot = new RoutePilot(config.ToTuning(approach));
                                }
                                else
                                {
                                    path = null;
                                    pilot = null;
                                    aimed = null;
                                    if (distance > approach
                                        && config.StuckRecoveryMilliseconds > 0
                                        && stillReady
                                        && Environment.TickCount64 - stillSince >= config.StuckRecoveryMilliseconds)
                                    {
                                        double heading = HeadingToward(config, scan.Bounds, position, goalPoint);
                                        UnstickResult recovery = UnstickToGoal(
                                            config, pinWindow, token, heading, stillAt, goalPoint, approach, 0,
                                            blocked, ref path, ref pilot);
                                        if (recovery == UnstickResult.Stop)
                                            return;
                                        if (path is not null)
                                            aimed = goalPoint;
                                        stillAt = position;
                                        stillSince = Environment.TickCount64;
                                        stillReady = true;
                                        if (!WaitRemainder(config, started, token))
                                            return;
                                        continue;
                                    }
                                }
                            }

                            if (!close && pilot is not null && path is not null)
                            {
                                PilotCommand command = pilot.Advance(true, position, scan.Bounds, path);
                                if (ArrivedNearGoal(command, distance, approach))
                                    close = true;
                                else if (command.Arrived)
                                {
                                    // Пилот «на финише», а маркер далеко — путь от ложного прыжка, строим заново.
                                    path = null;
                                    pilot = null;
                                    aimed = null;
                                }
                                else if (config.StuckRecoveryMilliseconds > 0
                                    && stillReady
                                    && Environment.TickCount64 - stillSince >= config.StuckRecoveryMilliseconds)
                                {
                                    UnstickResult recovery = UnstickToGoal(
                                        config, pinWindow, token, command.HeadingDegrees, stillAt, goalPoint, approach, 0,
                                        blocked, ref path, ref pilot);
                                    if (recovery == UnstickResult.Stop)
                                        return;
                                    if (path is not null)
                                        aimed = goalPoint;
                                    stillAt = position;
                                    stillSince = Environment.TickCount64;
                                    stillReady = true;
                                    if (!WaitMove(config, started, token))
                                        return;
                                    continue;
                                }
                                else
                                {
                                    if (command.Click)
                                        Click(config, command, client);
                                    string text = $"{block.Title}. {Describe(command)} Дверь в {distance:0} px.";
                                    Publish(config, client, text, Shot(scan, frame, position, goalPoint, path, client, $"door dist={Num(distance)}"));
                                    Report(text);
                                }
                            }
                            else if (!close)
                            {
                                if (TryAim(config, scan.Bounds, position, goalPoint, "К двери напрямую.", out PilotCommand direct))
                                {
                                    Click(config, direct, client);
                                    const string walk = "К двери нет пути по полу. Иду напрямую.";
                                    Publish(config, client, walk, Shot(scan, frame, position, goalPoint, null, client, $"door dist={Num(distance)} direct=True"));
                                    Report(walk);
                                }
                                else
                                {
                                    const string noPath = "К двери не нашёл проход по полу.";
                                    Publish(config, client, noPath, Shot(scan, frame, position, goalPoint, null, client, $"door dist={Num(distance)} direct=False"));
                                    Report(noPath);
                                }
                            }
                        }

                        if (close)
                        {
                            if (knockSince == 0)
                                knockSince = Environment.TickCount64;
                            if (Environment.TickCount64 - knockSince >= knockLimit)
                            {
                                const string missed = "Окно входа не открылось. Шаг входа закончен.";
                                Publish(config, client, missed, Shot(scan, frame, position, goalPoint, path, client, $"door dist={Num(distance)}"));
                                Report(missed);
                                return;
                            }

                            bool lit = false;
                            bool steppedCloser = false;
                            if (distance > approach)
                            {
                                if (TryAim(config, scan.Bounds, position, goalPoint, "Ближе к двери.", out PilotCommand closer))
                                {
                                    Click(config, closer, client);
                                    steppedCloser = true;
                                }
                            }
                            else
                            {
                                lit = TryClickPortal(config, client, scan.Bounds, position, goalPoint, ref aimHeading, token)
                                    || TryClickLitChest(config, client, scan.Bounds, position, goalPoint, ref aimHeading, token, "Клик по подсветке двери.", toEdge: true);
                                if (token.IsCancellationRequested)
                                    return;
                                if (lit)
                                {
                                    knocked = true;
                                    enterReadyAt = 0;
                                }
                            }

                            string text = lit
                                ? $"У двери, {distance:0} px. Подсветка есть, кликаю."
                                : steppedCloser
                                    ? $"У двери, {distance:0} px. Подсветка далеко, подхожу."
                                    : $"У двери, {distance:0} px. Ищу подсветку двери.";
                            Publish(config, client, text, Shot(scan, frame, position, goalPoint, path, client, $"door dist={Num(distance)} hover={lit} closer={steppedCloser}"));
                            Report(text);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitMove(config, started, token))
                return;
        }
    }

    private static DoorMark? NearestDoor(ColorImage frame, MapScan scan, MapPoint position)
    {
        DoorMark? best = null;
        double distance = double.MaxValue;
        foreach (DoorMark mark in DoorFinder.FindAll(frame, scan.Bounds))
        {
            double current = PathCourse.Distance(scan.Bounds, position, mark.Point);
            if (current >= distance)
                continue;
            distance = current;
            best = mark;
        }

        return best;
    }

    private static DoorMark? MatchingDoor(ColorImage frame, MapScan scan, MapPoint tracked)
    {
        DoorMark? best = null;
        double distance = double.MaxValue;
        foreach (DoorMark mark in DoorFinder.FindAll(frame, scan.Bounds))
        {
            double current = PathCourse.Distance(scan.Bounds, tracked, mark.Point);
            if (current > SameSkullPixels || current >= distance)
                continue;
            distance = current;
            best = mark;
        }

        return best;
    }

    private void RunOpenChest(FollowConfig config, OpenChestBlock block, bool pinWindow, CancellationToken token)
    {
        const double approach = 12;
        const int knockLimit = 25_000;
        int waitMs = Math.Clamp(block.WaitMilliseconds, 0, 120_000);

        RoutePilot? pilot = null;
        List<MapPoint>? path = null;
        MapPoint? tracked = null;
        MapPoint? aimed = null;
        var blocked = new List<MapPoint>();
        MapPoint stillAt = default;
        long stillSince = 0;
        bool stillReady = false;
        long goneSince = 0;
        long absentSince = 0;
        long knockSince = 0;
        double? aimHeading = null;
        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return;

                if (!scan!.HasMap || !scan.PlayerFound)
                {
                    string hidden = $"{block.Title}: карта или персонаж не видны.";
                    Publish(config, client, hidden, Shot(scan, frame, null, tracked, path, client, "chest"));
                    Report(hidden);
                }
                else
                {
                    var position = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                    ChestMark? seen = tracked is MapPoint held
                        ? MatchingChest(frame!, scan, held)
                        : NearestChest(frame!, scan, position);
                    int remain = tracked is MapPoint remembered
                        ? ChestFinder.CountNear(frame!, scan.Bounds, remembered, ChestCoverRadius)
                        : 0;
                    bool covered = seen is null && tracked is not null && remain >= ChestCoverPixels;
                    if (seen is null && !covered)
                    {
                        if (tracked is null)
                        {
                            path = null;
                            pilot = null;
                            aimed = null;
                            blocked.Clear();
                            if (absentSince == 0)
                                absentSince = Environment.TickCount64;
                            if (Environment.TickCount64 - absentSince >= waitMs)
                            {
                                string none = waitMs <= 0
                                    ? "Сундука нет. Шаг закончен."
                                    : waitMs % 1000 == 0
                                        ? $"Сундука нет {waitMs / 1000} с. Шаг закончен."
                                        : $"Сундука нет {waitMs} мс. Шаг закончен.";
                                Publish(config, client, none, Shot(scan, frame, position, null, path, client, $"chests=0 wait={waitMs}"));
                                NoteSkipped();
                                Report(none);
                                return;
                            }

                            string wait = waitMs <= 0
                                ? "Жду сундук на карте."
                                : $"Жду сундук на карте (до {waitMs / 1000.0:0.#} с).";
                            Publish(config, client, wait, Shot(scan, frame, position, null, path, client,
                                $"chests=0 elapsed={Environment.TickCount64 - absentSince}ms limit={waitMs}"));
                            Report(wait);
                        }
                        else
                        {
                            if (goneSince == 0)
                                goneSince = Environment.TickCount64;
                            if (Environment.TickCount64 - goneSince >= waitMs)
                            {
                                const string opened = "Сундук открыт. Шаг закончен.";
                                Publish(config, client, opened, Shot(scan, frame, position, tracked, path, client, $"gone={Environment.TickCount64 - goneSince}ms remain={remain} wait={waitMs}"));
                                NoteChestOpened();
                                Report(opened);
                                return;
                            }

                            string hiddenChest = "Сундук скрылся, жду подтверждения.";
                            Publish(config, client, hiddenChest, Shot(scan, frame, position, tracked, path, client, $"gone={Environment.TickCount64 - goneSince}ms remain={remain} wait={waitMs}"));
                            Report(hiddenChest);
                        }
                    }
                    else
                    {
                        goneSince = 0;
                        absentSince = 0;
                        if (seen is ChestMark visible)
                            tracked = visible.Point;
                        MapPoint goalPoint = tracked!.Value;
                        WatchStill(config, scan.Bounds, position, ref stillAt, ref stillSince, ref stillReady);
                        double distance = PathCourse.Distance(scan.Bounds, position, goalPoint);
                        bool close = covered || distance <= approach;
                        if (!close)
                        {
                            knockSince = 0;
                            bool shifted = aimed is not MapPoint known || PathCourse.Distance(scan.Bounds, known, goalPoint) > 10;
                            if (shifted || path is null || pilot is null)
                            {
                                if (MapPathfinder.TryFind(scan, frame!, position, goalPoint, out List<MapPoint> found, config.WallClearancePixels, blocked, StuckExcludePixels, ChestStandPixels) && found.Count >= 2)
                                {
                                    path = found;
                                    aimed = goalPoint;
                                    pilot = new RoutePilot(config.ToTuning(approach));
                                }
                                else
                                {
                                    path = null;
                                    pilot = null;
                                    aimed = null;
                                    if (distance > 48
                                        && config.StuckRecoveryMilliseconds > 0
                                        && stillReady
                                        && Environment.TickCount64 - stillSince >= config.StuckRecoveryMilliseconds)
                                    {
                                        double heading = HeadingToward(config, scan.Bounds, position, goalPoint);
                                        UnstickResult recovery = UnstickToGoal(
                                            config, pinWindow, token, heading, stillAt, goalPoint, approach, ChestStandPixels,
                                            blocked, ref path, ref pilot);
                                        if (recovery == UnstickResult.Stop)
                                            return;
                                        if (path is not null)
                                            aimed = goalPoint;
                                        stillAt = position;
                                        stillSince = Environment.TickCount64;
                                        stillReady = true;
                                        if (!WaitRemainder(config, started, token))
                                            return;
                                        continue;
                                    }

                                    if (distance <= 48)
                                        close = true;
                                }
                            }

                            if (!close && pilot is not null && path is not null)
                            {
                                PilotCommand command = pilot.Advance(true, position, scan.Bounds, path);
                                if (ArrivedNearGoal(command, distance, approach))
                                    close = true;
                                else if (command.Arrived)
                                {
                                    path = null;
                                    pilot = null;
                                    aimed = null;
                                }
                                else if (config.StuckRecoveryMilliseconds > 0
                                    && stillReady
                                    && Environment.TickCount64 - stillSince >= config.StuckRecoveryMilliseconds)
                                {
                                    UnstickResult recovery = UnstickToGoal(
                                        config, pinWindow, token, command.HeadingDegrees, stillAt, goalPoint, approach, ChestStandPixels,
                                        blocked, ref path, ref pilot);
                                    if (recovery == UnstickResult.Stop)
                                        return;
                                    if (path is not null)
                                        aimed = goalPoint;
                                    stillAt = position;
                                    stillSince = Environment.TickCount64;
                                    stillReady = true;
                                    if (!WaitMove(config, started, token))
                                        return;
                                    continue;
                                }
                                else
                                {
                                    if (command.Click)
                                        Click(config, command, client);
                                    string text = $"{block.Title}. {Describe(command)} Сундук в {distance:0} px.";
                                    Publish(config, client, text, Shot(scan, frame, position, goalPoint, path, client, $"chest dist={Num(distance)}"));
                                    Report(text);
                                }
                            }
                            else if (!close)
                            {
                                const string noPath = "К сундуку не нашёл проход по полу.";
                                Publish(config, client, noPath, Shot(scan, frame, position, goalPoint, null, client, $"chest dist={Num(distance)}"));
                                Report(noPath);
                            }
                        }

                        if (close)
                        {
                            if (knockSince == 0)
                                knockSince = Environment.TickCount64;
                            if (Environment.TickCount64 - knockSince >= knockLimit)
                            {
                                const string missed = "Сундук не открылся. Шаг закончен.";
                                Publish(config, client, missed, Shot(scan, frame, position, goalPoint, path, client, $"chest dist={Num(distance)} remain={remain}"));
                                NoteSkipped();
                                Report(missed);
                                return;
                            }

                            bool lit = TryClickLitChest(config, client, scan.Bounds, position, goalPoint, ref aimHeading, token, "Клик по подсветке сундука.");
                            if (token.IsCancellationRequested)
                                return;

                            bool steppedCloser = false;
                            if (!lit && distance > 3
                                && TryAim(config, scan.Bounds, position, goalPoint, "Ближе к сундуку.", out PilotCommand closer))
                            {
                                Click(config, closer, client);
                                steppedCloser = true;
                            }

                            string text = lit
                                ? $"У сундука, {distance:0} px. Подсветка есть, кликаю."
                                : steppedCloser
                                    ? $"У сундука, {distance:0} px. Подсветка далеко, подхожу."
                                    : $"У сундука, {distance:0} px. Ищу подсветку сундука.";
                            Publish(config, client, text, Shot(scan, frame, position, goalPoint, path, client, $"chest dist={Num(distance)} remain={remain} hover={lit} closer={steppedCloser}"));
                            Report(text);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitMove(config, started, token))
                return;
        }
    }

    private void RunClickQuestMark(FollowConfig config, ClickQuestMarkBlock block, bool pinWindow, CancellationToken token)
    {
        const double approach = 12;
        const int knockLimit = 25_000;
        QuestMarkKind kind = ToQuestKind(block.Mark);
        string markName = kind == QuestMarkKind.Exclaim ? "восклицания" : "вопроса";

        RoutePilot? pilot = null;
        List<MapPoint>? path = null;
        MapPoint? tracked = null;
        MapPoint? aimed = null;
        double? aimHeading = null;
        long knockSince = 0;
        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return;

                if (!scan!.HasMap || !scan.PlayerFound)
                {
                    string hidden = $"{block.Title}: карта или персонаж не видны.";
                    Publish(config, client, hidden, Shot(scan, frame, null, tracked, path, client, "questMark"));
                    Report(hidden);
                }
                else
                {
                    var position = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                    var view = config.ViewRectangle(client);
                    var map = config.MapRectangle(client);
                    var excluded = new MapBounds(map.Left - view.Left, map.Top - view.Top, map.Right - view.Left - 1, map.Bottom - view.Top - 1);
                    ColorImage window;
                    try
                    {
                        window = ScreenMapSource.Capture(view);
                    }
                    catch (Exception exception)
                    {
                        FileLog.Trace("quest", $"view {exception.Message}");
                        return;
                    }

                    if (DialogAcceptFinder.TryFind(window, excluded, out _, out _))
                    {
                        const string opened = "Диалог открыт. Шаг закончен.";
                        Publish(config, client, opened, Shot(scan, frame, position, tracked, path, client, "dialog=open"));
                        Report(opened);
                        return;
                    }

                    QuestMark? seen = tracked is MapPoint held
                        ? MatchingQuest(frame!, scan, held, kind)
                        : NearestQuest(frame!, scan, position, kind);
                    if (seen is null)
                    {
                        if (tracked is not null)
                        {
                            string gone = $"Знак {markName} пропал. Шаг закончен.";
                            Publish(config, client, gone, Shot(scan, frame, position, tracked, path, client, "mark=gone"));
                            Report(gone);
                            return;
                        }

                        string wait = $"Жду знак {markName} на карте.";
                        Publish(config, client, wait, Shot(scan, frame, position, null, path, client, "marks=0"));
                        Report(wait);
                    }
                    else
                    {
                        tracked = seen.Value.Point;
                        MapPoint goalPoint = tracked.Value;
                        double distance = PathCourse.Distance(scan.Bounds, position, goalPoint);
                        bool close = distance <= approach;
                        if (!close)
                        {
                            knockSince = 0;
                            bool shifted = aimed is not MapPoint known || PathCourse.Distance(scan.Bounds, known, goalPoint) > 10;
                            if (shifted || path is null || pilot is null)
                            {
                                if (MapPathfinder.TryFind(scan, frame!, position, goalPoint, out List<MapPoint> found, config.WallClearancePixels) && found.Count >= 2)
                                {
                                    path = found;
                                    aimed = goalPoint;
                                    pilot = new RoutePilot(config.ToTuning(approach));
                                }
                                else
                                {
                                    path = null;
                                    pilot = null;
                                    aimed = null;
                                    if (distance <= 48)
                                        close = true;
                                }
                            }

                            if (!close && pilot is not null && path is not null)
                            {
                                PilotCommand command = pilot.Advance(true, position, scan.Bounds, path);
                                if (ArrivedNearGoal(command, distance, approach))
                                    close = true;
                                else if (command.Arrived)
                                {
                                    path = null;
                                    pilot = null;
                                    aimed = null;
                                }
                                else
                                {
                                    if (command.Click)
                                        Click(config, command, client);
                                    string text = $"{block.Title}. {Describe(command)} Знак в {distance:0} px.";
                                    Publish(config, client, text, Shot(scan, frame, position, goalPoint, path, client, $"quest dist={Num(distance)}"));
                                    Report(text);
                                }
                            }
                            else if (!close)
                            {
                                string blocked = $"К знаку {markName} не нашёл проход по полу.";
                                Publish(config, client, blocked, Shot(scan, frame, position, goalPoint, null, client, $"quest dist={Num(distance)}"));
                                Report(blocked);
                            }
                        }

                        if (close)
                        {
                            if (knockSince == 0)
                                knockSince = Environment.TickCount64;
                            if (Environment.TickCount64 - knockSince >= knockLimit)
                            {
                                string missed = $"Знак {markName} не открылся. Шаг закончен.";
                                Publish(config, client, missed, Shot(scan, frame, position, goalPoint, path, client, $"quest dist={Num(distance)}"));
                                Report(missed);
                                return;
                            }

                            bool lit = TryClickLitChest(config, client, scan.Bounds, position, goalPoint, ref aimHeading, token, $"Клик по знаку {markName}.");
                            if (token.IsCancellationRequested)
                                return;

                            bool steppedCloser = false;
                            if (!lit && distance > 3
                                && TryAim(config, scan.Bounds, position, goalPoint, $"Ближе к знаку {markName}.", out PilotCommand closer))
                            {
                                Click(config, closer, client);
                                steppedCloser = true;
                            }

                            string text = lit
                                ? $"У знака {markName}, {distance:0} px. Подсветка есть, кликаю."
                                : steppedCloser
                                    ? $"У знака {markName}, {distance:0} px. Подсветка далеко, подхожу."
                                    : $"У знака {markName}, {distance:0} px. Ищу подсветку.";
                            Publish(config, client, text, Shot(scan, frame, position, goalPoint, path, client, $"quest dist={Num(distance)} hover={lit} closer={steppedCloser}"));
                            Report(text);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitMove(config, started, token))
                return;
        }
    }

    private void RunUseMap(Scenario scenario, UseMapBlock block)
    {
        if (string.IsNullOrWhiteSpace(block.MapFile))
        {
            Report("Сменить карту: файл не выбран.");
            return;
        }

        string full = Path.GetFullPath(block.MapFile);
        if (!File.Exists(full))
        {
            Report("Сменить карту: файл не найден — " + Path.GetFileName(full));
            return;
        }

        scenario.EnsureMap(full);
        scenario.MapFile = full;
        string text = "Активная карта: " + Path.GetFileNameWithoutExtension(full) + ".";
        FileLog.Trace("map", $"active={full}");
        Report(text);
    }

    private void RunMountHorses(FollowConfig config, MountHorsesBlock block, bool pinWindow, CancellationToken token)
    {
        const int goneLimit = 350;
        int interval = Math.Clamp(block.KeyIntervalMilliseconds, 200, 10_000);
        long nextPressAt = 0;
        long goneSince = 0;
        bool sawBar = false;
        bool focusArmed = false;

        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return;

                var view = config.ViewRectangle(client);
                var map = config.MapRectangle(client);
                var excluded = new MapBounds(map.Left - view.Left, map.Top - view.Top, map.Right - view.Left - 1, map.Bottom - view.Top - 1);
                ColorImage window;
                try
                {
                    window = ScreenMapSource.Capture(view);
                }
                catch (Exception exception)
                {
                    FileLog.Trace("mount", $"view {exception.Message}");
                    return;
                }

                bool visible = MountCastFinder.IsVisible(window, excluded);
                if (!sawBar)
                {
                    if (visible)
                    {
                        sawBar = true;
                        goneSince = 0;
                        const string text = "Полоска посадки появилась, жду исчезновения.";
                        FileLog.Trace("mount", "bar=appeared");
                        Publish(config, client, text, Shot(scan!, frame, null, null, null, client, "mount=appeared"));
                        Report(text);
                    }
                    else
                    {
                        long now = Environment.TickCount64;
                        if (now >= nextPressAt)
                        {
                            if (TapMountKey(config, client, token, ref focusArmed))
                                FileLog.Trace("mount", "key=O");
                            else
                            {
                                focusArmed = false;
                                Report("По кОням: не удалось взять окно в фокус, повторю.");
                            }

                            nextPressAt = Environment.TickCount64 + interval;
                        }

                        string wait = $"По кОням: жму O каждые {interval} мс, жду полоску.";
                        Publish(config, client, wait, Shot(scan!, frame, null, null, null, client, "mount=wait-bar"));
                        Report(wait);
                    }
                }
                else if (visible)
                {
                    goneSince = 0;
                    const string casting = "Идёт посадка, полоска ещё на экране.";
                    Publish(config, client, casting, Shot(scan!, frame, null, null, null, client, "mount=casting"));
                    Report(casting);
                }
                else
                {
                    if (goneSince == 0)
                        goneSince = Environment.TickCount64;
                    if (Environment.TickCount64 - goneSince >= goneLimit)
                    {
                        const string done = "Посадка завершена. Шаг закончен.";
                        FileLog.Trace("mount", "bar=gone");
                        Publish(config, client, done, Shot(scan!, frame, null, null, null, client, "mount=gone"));
                        Report(done);
                        return;
                    }

                    const string closing = "Полоска пропала, подтверждаю.";
                    Publish(config, client, closing, Shot(scan!, frame, null, null, null, client, "mount=closing"));
                    Report(closing);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitRemainder(config, started, token))
                return;
        }
    }

    // Красная полоса HP на скрине 1024×722 (logs/.../014947): ~377–495 × 665–680.
    private const double MountFocusHpX = 436.0 / 1024.0;
    private const double MountFocusHpY = 672.0 / 722.0;

    /// <summary>
    /// Фокус кликом по красной полосе HP (непрозрачный HUD). Миникарта прозрачна для кликов.
    /// </summary>
    private bool TapMountKey(FollowConfig config, Rectangle? client, CancellationToken token, ref bool focusArmed)
    {
        if (token.IsCancellationRequested)
            return false;

        if (string.IsNullOrWhiteSpace(config.GameWindowTitleContains))
        {
            FileLog.Trace("mount", "no-title");
            return false;
        }

        Rectangle area;
        if (client is Rectangle known && known.Width >= 8 && known.Height >= 8)
            area = known;
        else if (!GameWindowFocus.TryFocus(config.GameWindowTitleContains, out area))
        {
            FileLog.Trace("mount", "no-client");
            return false;
        }

        var view = config.ViewRectangle(area);
        bool focused = GameWindowFocus.TryActivate(config.GameWindowTitleContains, out _);
        if (!focused || !focusArmed)
        {
            int clickX = view.Left + (int)Math.Round(view.Width * MountFocusHpX);
            int clickY = view.Top + (int)Math.Round(view.Height * MountFocusHpY);
            var hit = MouseClicker.Clamp(new Point(clickX, clickY), view, Math.Max(2, config.ClickMarginPixels / 4));
            FileLog.Trace("mount", $"focus-hp screen={hit.X},{hit.Y} local={hit.X - view.Left},{hit.Y - view.Top} view={view.Width}x{view.Height}");
            try
            {
                MouseClicker.LeftClick(hit.X, hit.Y, Math.Max(40, config.ClickHoldMilliseconds));
            }
            catch (Exception exception)
            {
                FileLog.Trace("mount", $"focus-hp fail {exception.Message}");
                return false;
            }

            if (config.FocusSettleMilliseconds > 0 && token.WaitHandle.WaitOne(config.FocusSettleMilliseconds))
                return false;

            focused = GameWindowFocus.TryActivate(config.GameWindowTitleContains, out _);
            focusArmed = focused;
            if (!focused)
            {
                FileLog.Trace("mount", "focus-failed after hp");
                return false;
            }
        }

        try
        {
            KeyPresser.TapHardware(Keys.O, 80);
            return true;
        }
        catch (Exception exception)
        {
            FileLog.Trace("mount", $"key fail {exception.Message}");
            focusArmed = false;
            return false;
        }
    }

    private void RunAcceptDialog(FollowConfig config, AcceptDialogBlock block, bool pinWindow, CancellationToken token)
    {
        const int waitLimit = 20_000;
        const int goneLimit = 400;
        long startedAt = Environment.TickCount64;
        long goneSince = 0;
        bool clicked = false;
        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return;

                var view = config.ViewRectangle(client);
                var map = config.MapRectangle(client);
                var excluded = new MapBounds(map.Left - view.Left, map.Top - view.Top, map.Right - view.Left - 1, map.Bottom - view.Top - 1);
                ColorImage window;
                try
                {
                    window = ScreenMapSource.Capture(view);
                }
                catch (Exception exception)
                {
                    FileLog.Trace("dialog", $"view {exception.Message}");
                    return;
                }

                bool found = DialogAcceptFinder.TryFind(window, excluded, out int localX, out int localY);
                if (found)
                {
                    goneSince = 0;
                    int margin = Math.Max(0, config.ClickMarginPixels);
                    var hit = MouseClicker.Clamp(new Point(view.X + localX, view.Y + localY), view, margin);
                    FileLog.Trace("click", $"screen={hit.X},{hit.Y} reason=Клик по галочке диалога.");
                    MouseClicker.LeftClick(hit.X, hit.Y, Math.Max(0, config.ClickHoldMilliseconds));
                    clicked = true;
                    const string text = "Галочка диалога найдена, нажимаю.";
                    Publish(config, client, text, Shot(scan!, frame, null, null, null, client, $"dialog=click x={localX} y={localY}"));
                    Report(text);
                }
                else if (clicked)
                {
                    if (goneSince == 0)
                        goneSince = Environment.TickCount64;
                    if (Environment.TickCount64 - goneSince >= goneLimit)
                    {
                        const string done = "Диалог закрыт. Шаг закончен.";
                        Publish(config, client, done, Shot(scan!, frame, null, null, null, client, "dialog=gone"));
                        Report(done);
                        return;
                    }

                    const string closing = "Диалог закрывается.";
                    Publish(config, client, closing, Shot(scan!, frame, null, null, null, client, "dialog=closing"));
                    Report(closing);
                }
                else if (Environment.TickCount64 - startedAt >= waitLimit)
                {
                    const string missed = "Диалог не появился. Шаг закончен.";
                    Publish(config, client, missed, Shot(scan!, frame, null, null, null, client, "dialog=timeout"));
                    Report(missed);
                    return;
                }
                else
                {
                    const string wait = "Жду кнопку принятия диалога.";
                    Publish(config, client, wait, Shot(scan!, frame, null, null, null, client, "dialog=wait"));
                    Report(wait);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitRemainder(config, started, token))
                return;
        }
    }

    private static QuestMarkKind ToQuestKind(AnchorSource source)
        => source == AnchorSource.QuestExclaim ? QuestMarkKind.Exclaim : QuestMarkKind.Question;

    private static QuestMark? MatchingQuest(ColorImage frame, MapScan scan, MapPoint locked, QuestMarkKind kind)
    {
        QuestMark? best = null;
        double distance = SameSkullPixels;
        foreach (QuestMark mark in QuestMarkFinder.FindAll(frame, scan.Bounds, kind))
        {
            double current = PathCourse.Distance(scan.Bounds, locked, mark.Point);
            if (current > SameSkullPixels || current >= distance)
                continue;
            distance = current;
            best = mark;
        }

        return best;
    }

    private static QuestMark? NearestQuest(ColorImage frame, MapScan scan, MapPoint player, QuestMarkKind kind)
    {
        QuestMark? best = null;
        double distance = double.MaxValue;
        foreach (QuestMark mark in QuestMarkFinder.FindAll(frame, scan.Bounds, kind))
        {
            double current = PathCourse.Distance(scan.Bounds, player, mark.Point);
            if (current >= distance)
                continue;
            distance = current;
            best = mark;
        }

        return best;
    }

    private static ChestMark? NearestChest(ColorImage frame, MapScan scan, MapPoint position)
    {
        ChestMark? best = null;
        double distance = double.MaxValue;
        foreach (ChestMark mark in ChestFinder.FindAll(frame, scan.Bounds))
        {
            double current = PathCourse.Distance(scan.Bounds, position, mark.Point);
            if (current >= distance)
                continue;
            distance = current;
            best = mark;
        }

        return best;
    }

    private static ChestMark? MatchingChest(ColorImage frame, MapScan scan, MapPoint tracked)
    {
        ChestMark? best = null;
        double distance = double.MaxValue;
        foreach (ChestMark mark in ChestFinder.FindAll(frame, scan.Bounds))
        {
            double current = PathCourse.Distance(scan.Bounds, tracked, mark.Point);
            if (current > SameSkullPixels || current >= distance)
                continue;
            distance = current;
            best = mark;
        }

        return best;
    }

    private static readonly int[] ChestHoverAngles = [0, -14, 14, -28, 28, -42, 42];

    /// <summary>Веер до края поля: сундук на экране часто дальше, чем 11–12 px на миникарте.</summary>
    private static readonly int[] ChestHoverDistances =
        [40, 70, 100, 140, 180, 220, 280, 340, 400, 460, 520, 580];

    private static readonly int[] DoorHoverAngles = [0, -10, 10, -20, 20, -32, 32];

    private static readonly int[] DoorHoverDistances = [80, 120, 160, 200, 240, 280, 320, 360, 400, 440, 480, 520, 560, 600, 640];

    private const double PortalAimRadians = 40 * Math.PI / 180;

    /// <summary>
    /// Кликает в центр бирюзового свечения, если оно лежит в направлении двери.
    /// Свечение видно без наведения, в том числе у края экрана.
    /// </summary>
    private bool TryClickPortal(
        FollowConfig config,
        Rectangle? client,
        MapBounds bounds,
        MapPoint player,
        MapPoint door,
        ref double? aimHeading,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return false;
        if (!TryAimVector(config, bounds, player, door, out double x, out double y))
        {
            if (aimHeading is not double known)
                return false;
            double knownRadians = known * Math.PI / 180.0;
            x = Math.Cos(knownRadians);
            y = Math.Sin(knownRadians);
        }
        else
        {
            aimHeading = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        }

        var view = config.ViewRectangle(client);
        ColorImage window;
        try
        {
            window = ScreenMapSource.Capture(view);
        }
        catch (Exception exception)
        {
            FileLog.Trace("door", $"view {exception.Message}");
            return false;
        }

        var map = config.MapRectangle(client);
        var excluded = new MapBounds(map.Left - view.Left, map.Top - view.Top, map.Right - view.Left - 1, map.Bottom - view.Top - 1);
        var anchor = config.CharacterAnchor(client);
        int anchorX = anchor.X - view.X;
        int anchorY = anchor.Y - view.Y;
        double doorAngle = Math.Atan2(y, x);
        PortalMark? best = null;
        double bestDelta = PortalAimRadians;
        foreach (PortalMark mark in PortalFinder.FindAll(window, excluded))
        {
            double vx = mark.X - anchorX;
            double vy = mark.Y - anchorY;
            if (vx * vx + vy * vy < 40 * 40)
                continue;
            double delta = Math.Abs(Math.Atan2(Math.Sin(Math.Atan2(vy, vx) - doorAngle), Math.Cos(Math.Atan2(vy, vx) - doorAngle)));
            if (delta > bestDelta)
                continue;
            bestDelta = delta;
            best = mark;
        }

        if (best is not PortalMark portal)
            return false;

        int margin = Math.Max(0, config.ClickMarginPixels);
        var hit = MouseClicker.Clamp(new Point(view.X + portal.X, view.Y + portal.Y), view, margin);
        if (map.Contains(hit))
            return false;

        FileLog.Trace("click", $"screen={hit.X},{hit.Y} pixels={portal.Pixels} reason=Клик по порталу.");
        MouseClicker.LeftClick(hit.X, hit.Y, Math.Max(0, config.ClickHoldMilliseconds));
        return true;
    }

    /// <summary>
    /// Водит курсор веером вокруг направления на цель и кликает только по серой подсветке.
    /// Промах по земле персонажа не сдвигает. Для двери веер тянется до края экрана.
    /// </summary>
    private bool TryClickLitChest(
        FollowConfig config,
        Rectangle? client,
        MapBounds bounds,
        MapPoint player,
        MapPoint chest,
        ref double? aimHeading,
        CancellationToken token,
        string reason,
        bool toEdge = false)
    {
        if (!TryAimVector(config, bounds, player, chest, out double x, out double y))
        {
            if (aimHeading is not double known)
                return false;
            double knownRadians = known * Math.PI / 180.0;
            x = Math.Cos(knownRadians);
            y = Math.Sin(knownRadians);
        }
        else
        {
            aimHeading = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        }

        var view = config.ViewRectangle(client);
        var map = config.MapRectangle(client);
        int margin = Math.Max(0, config.ClickMarginPixels);
        var anchor = config.CharacterAnchor(client);
        int[] angles = toEdge ? DoorHoverAngles : ChestHoverAngles;
        int[] distances = toEdge ? DoorHoverDistances : ChestHoverDistances;
        int minX = view.Left + margin;
        int maxX = view.Right - margin - 1;
        int minY = view.Top + 70;
        int maxY = view.Bottom - 130;
        foreach (int degrees in angles)
        {
            double turn = degrees * Math.PI / 180.0;
            double cos = Math.Cos(turn);
            double sin = Math.Sin(turn);
            double rx = x * cos - y * sin;
            double ry = x * sin + y * cos;
            foreach (int distance in distances)
            {
                if (token.IsCancellationRequested)
                    return false;

                int rawX = anchor.X + (int)Math.Round(rx * distance);
                int rawY = anchor.Y + (int)Math.Round(ry * distance);
                if (rawX < minX || rawX > maxX || rawY < minY || rawY > maxY)
                    break;

                var point = MouseClicker.Clamp(new Point(rawX, rawY), view, margin);
                if (map.Contains(point))
                    continue;

                int localX = point.X - view.X;
                int localY = point.Y - view.Y;
                if (localY < 70 || localY > view.Height - 130)
                    continue;

                MouseClicker.Move(point.X, point.Y);
                if (token.WaitHandle.WaitOne(100))
                    return false;

                ColorImage window;
                try
                {
                    window = ScreenMapSource.Capture(view);
                }
                catch (Exception exception)
                {
                    FileLog.Trace("chest", $"view {exception.Message}");
                    return false;
                }

                var play = new MapBounds(margin, 70, Math.Max(margin, view.Width - margin - 1), Math.Max(70, view.Height - 130));
                var excluded = new MapBounds(map.Left - view.Left, map.Top - view.Top, map.Right - view.Left - 1, map.Bottom - view.Top - 1);
                if (!ChestHover.TryCenter(window, localX, localY, play, excluded, out int centerX, out int centerY))
                    continue;
                if (centerX < play.Left || centerX > play.Right || centerY < play.Top || centerY > play.Bottom)
                    continue;

                var hit = MouseClicker.Clamp(new Point(view.X + centerX, view.Y + centerY), view, margin);
                if (map.Contains(hit) || hit.Y < minY || hit.Y > maxY)
                    continue;

                FileLog.Trace("click", $"screen={hit.X},{hit.Y} turn={degrees} dist={distance} reason={reason}");
                MouseClicker.LeftClick(hit.X, hit.Y, Math.Max(0, config.ClickHoldMilliseconds));
                return true;
            }
        }

        FileLog.Trace("chest", "подсветка не найдена");
        return false;
    }

    private static bool TryAim(FollowConfig config, MapBounds bounds, MapPoint player, MapPoint target, string reason, out PilotCommand command)
    {
        if (!TryAimVector(config, bounds, player, chest: target, out double x, out double y))
        {
            command = default;
            return false;
        }

        double heading = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        double distance = Math.Max(40, config.ClickDistancePixels);
        var (x0, y0) = bounds.ToPixel(player);
        var (x1, y1) = bounds.ToPixel(target);
        command = new PilotCommand(
            true,
            x * distance,
            y * distance,
            heading,
            0,
            Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)),
            false,
            false,
            false,
            reason);
        return true;
    }

    private static bool TryAimVector(FollowConfig config, MapBounds bounds, MapPoint player, MapPoint chest, out double x, out double y)
    {
        var (x0, y0) = bounds.ToPixel(player);
        var (x1, y1) = bounds.ToPixel(chest);
        double dx = x1 - x0;
        double dy = y1 - y0;
        double radians = config.RotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        x = dx * cos - dy * sin;
        y = dx * sin + dy * cos;
        if (config.InvertX)
            x = -x;
        if (config.InvertY)
            y = -y;
        double length = Math.Sqrt(x * x + y * y);
        if (length < 1)
            return false;

        x /= length;
        y /= length;
        return true;
    }

    private static void ClickScreen(FollowConfig config, Rectangle view, int localX, int localY, string reason = "Enter")
    {
        var point = MouseClicker.Clamp(
            new Point(view.X + localX, view.Y + localY),
            view,
            Math.Max(0, config.ClickMarginPixels));
        FileLog.Trace("click", $"screen={point.X},{point.Y} local={localX},{localY} reason={reason}");
        MouseClicker.LeftClick(point.X, point.Y, Math.Max(0, config.ClickHoldMilliseconds));
    }

    private void RunLock(
        FollowConfig config,
        Scenario scenario,
        LockBlock block,
        bool pinWindow,
        CancellationToken token)
    {
        List<ScheduledKey> keys = scenario.PressKeys ? Schedule(block.Keys) : [];
        if (scenario.PressKeys)
            ArmKeys(keys);

        MapPoint? locked = null;
        double? facedDegrees = null;
        long lastSeen = Environment.TickCount64;
        long absentSince = 0;
        long outsideSince = 0;
        bool everInside = false;
        int waitMs = Math.Clamp(block.WaitMilliseconds, 0, 120_000);
        // Если Follow сорвался, не крутимся лицом к далёкой цели вечно.
        int farLimit = Math.Clamp(Math.Max(waitMs, 8_000), 1_000, 120_000);
        while (!token.IsCancellationRequested)
        {
            long started = Environment.TickCount64;
            try
            {
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return;

                MapPoint? seen = null;
                AnchorSource goalKind = block.Target == AnchorSource.Chest ? AnchorSource.Chest : AnchorSource.Skull;
                if (scan!.HasMap && scan.PlayerFound)
                {
                    var position = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                    List<IconSpot> icons = Icons(goalKind, frame!, scan);
                    seen = locked is MapPoint held
                        ? MatchingIcon(scan.Bounds, icons, held)
                        : NearestIcon(scan.Bounds, icons, position);
                    if (seen is MapPoint skull && locked is null)
                        locked = skull;
                }

                if (seen is MapPoint target)
                {
                    lastSeen = Environment.TickCount64;
                    absentSince = 0;
                    locked = target;
                    var player = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                    bool inside = block.RadiusPixels <= 0
                        || PathCourse.Distance(scan.Bounds, player, target) <= block.RadiusPixels;
                    if (inside)
                    {
                        everInside = true;
                        outsideSince = 0;
                    }
                    else if (!everInside)
                    {
                        if (outsideSince == 0)
                            outsideSince = Environment.TickCount64;
                        if (Environment.TickCount64 - outsideSince >= farLimit)
                        {
                            string far = goalKind == AnchorSource.Chest
                                ? $"Сундук дальше {block.RadiusPixels} px слишком долго. Шаг фиксации закончен."
                                : $"Красный дальше {block.RadiusPixels} px слишком долго. Шаг фиксации закончен.";
                            Publish(config, client, far, Shot(scan, frame, player, target, null, client,
                                $"target={goalKind} dist={Num(PathCourse.Distance(scan.Bounds, player, target))} outside={Environment.TickCount64 - outsideSince}ms limit={farLimit}"));
                            NoteSkipped();
                            Report(far);
                            return;
                        }
                    }

                    bool turned = FaceTarget(config, scan.Bounds, player, target, client, ref facedDegrees);
                    string face = turned ? " Разворот лицом к цели." : " Лицом к цели.";
                    string name = goalKind == AnchorSource.Chest ? "Сундук" : "Красный";
                    string text;
                    if (!inside)
                        text = $"{name} дальше {block.RadiusPixels} px.{face} Клавиши ждут, пока цель войдёт в радиус.";
                    else if (!scenario.PressKeys)
                        text = "У цели." + face + " Клавиши выключены.";
                    else
                    {
                        string pressed = PressDue(config, keys, client, token);
                        string keysNote = keys.Count == 0
                            ? " Клавиши не заданы."
                            : pressed.Length == 0 ? "" : $" Нажато: {pressed}.";
                        text = "У цели." + face + keysNote;
                    }

                    Publish(config, client, text, Shot(scan, frame, player, target, null, client,
                        $"target={goalKind} dist={Num(PathCourse.Distance(scan.Bounds, player, target))} radius={block.RadiusPixels} inside={inside} turned={turned} faced={(facedDegrees is double faced ? Num(faced) : "-")}"));
                    Report(text);
                }
                else if (locked is not null && Environment.TickCount64 - lastSeen >= waitMs)
                {
                    const string gone = "Цель пропала. Шаг фиксации закончен.";
                    Publish(config, client, gone, Shot(scan, frame, null, locked, null, client, $"gone after={Environment.TickCount64 - lastSeen}ms wait={waitMs}"));
                    if (goalKind == AnchorSource.Chest)
                        NoteChestOpened();
                    else
                        NoteRedKilled();
                    Report(gone);
                    return;
                }
                else if (locked is null && scan.HasMap && scan.PlayerFound
                    && absentSince != 0
                    && Environment.TickCount64 - absentSince >= waitMs)
                {
                    string none = block.Target == AnchorSource.Chest
                        ? "Сундука нет. Шаг фиксации закончен."
                        : "Красной цели нет. Шаг фиксации закончен.";
                    Publish(config, client, none, Shot(scan, frame, null, null, null, client, $"none after={Environment.TickCount64 - absentSince}ms wait={waitMs}"));
                    NoteSkipped();
                    Report(none);
                    return;
                }
                else
                {
                    if (locked is null && scan.HasMap && scan.PlayerFound && absentSince == 0)
                        absentSince = Environment.TickCount64;
                    string text = locked is null
                        ? goalKind == AnchorSource.Chest
                            ? $"Жду сундук для фиксации (до {waitMs / 1000.0:0.#} с)."
                            : $"Жду красный для фиксации (до {waitMs / 1000.0:0.#} с)."
                        : "Цель скрылась, жду подтверждения.";
                    string extra = locked is null
                        ? $"wait elapsed={Environment.TickCount64 - absentSince}ms limit={waitMs}"
                        : $"hidden for={Environment.TickCount64 - lastSeen}ms limit={waitMs}";
                    MapPoint? player = scan.HasMap && scan.PlayerFound
                        ? scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY)
                        : null;
                    Publish(config, client, text, Shot(scan, frame, player, locked, null, client, extra));
                    Report(text);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                Report(exception.Message);
            }

            if (!WaitRemainder(config, started, token))
                return;
        }
    }

    private void RunSequence(FollowConfig config, SequenceBlock block, CancellationToken token)
    {
        if (block.Clicks.Count == 0)
        {
            Report("Последовательность пуста.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.GameWindowTitleContains)
            || !GameWindowFocus.TryFocus(config.GameWindowTitleContains, out Rectangle client))
        {
            Report("Окно игры не найдено. Последовательность пропущена.");
            return;
        }

        int count = block.Clicks.Count;
        for (int i = 0; i < count; i++)
        {
            ScreenClick click = block.Clicks[i];
            if (GameWindowFocus.TryFocus(config.GameWindowTitleContains, out Rectangle live))
                client = live;

            var view = config.ViewRectangle(client);
            var point = MouseClicker.Clamp(
                new Point(client.X + click.X, client.Y + click.Y),
                view,
                Math.Max(0, config.ClickMarginPixels));
            string seqData =
                $"{i + 1}/{count} name={click.Name ?? "-"} client={client.X},{client.Y} {client.Width}x{client.Height} " +
                $"local={click.X},{click.Y} screen={point.X},{point.Y} after={click.PauseMilliseconds} hold={Math.Max(0, click.HoldMilliseconds)}";
            try
            {
                MouseClicker.LeftClick(point.X, point.Y, Math.Max(0, click.HoldMilliseconds));
                string text = $"Нажатие {ClickTitle(click, i, count)}: {click.X}, {click.Y}.";
                Publish(config, client, text, seqData);
                Report(text);
            }
            catch (Exception exception)
            {
                FileLog.Trace("error", exception.ToString());
                string text = $"Нажатие {ClickTitle(click, i, count)} не отправилось: {exception.Message}";
                Publish(config, client, text, seqData);
                Report(text);
            }

            if (!Wait(click.PauseMilliseconds, left =>
                    $"Пауза после нажатия {ClickTitle(click, i, count)}: {(left / 1000.0).ToString("0.0", CultureInfo.CurrentCulture)} с.", token))
                return;
        }
    }

    private bool RepeatHolds(FollowConfig config, RepeatBlock block, bool pinWindow, CancellationToken token)
        => block.Condition switch
        {
            RepeatCondition.WhileRed => RedVisible(config, pinWindow, token),
            _ => RedVisible(config, pinWindow, token),
        };

    private bool RedVisible(FollowConfig config, bool pinWindow, CancellationToken token)
    {
        if (!TryCapture(config, pinWindow, token, out _, out ColorImage? frame, out MapScan? scan)
            || frame is null || scan is null)
            return false;
        int reds = ConfirmedReds(frame, scan).Count;
        FileLog.Trace("repeat", $"visible={reds > 0} reds={reds} hasMap={scan.HasMap} player={scan.PlayerFound}");
        return reds > 0;
    }

    private bool TryFollowRed(
        FollowConfig config,
        Scenario scenario,
        FollowBlock block,
        int leg,
        bool shortest,
        ColorImage frame,
        MapScan scan,
        MapPoint position,
        AnchorSource kind,
        List<MapPoint> blocked,
        ref List<MapPoint>? path,
        ref MapPoint? tracked,
        ref RoutePilot? pilot,
        ref int segment,
        out string? waiting,
        out bool missing)
    {
        waiting = null;
        missing = false;
        List<IconSpot> icons = Icons(kind, frame, scan);
        // В пути сначала ищем «свою» точку у tracked; чужой ближайший — только перехват.
        MapPoint? live = tracked is MapPoint previous
            ? MatchingIcon(scan.Bounds, icons, previous) ?? NearestIcon(scan.Bounds, icons, position)
            : NearestIcon(scan.Bounds, icons, position);
        if (live is not MapPoint red)
        {
            missing = true;
            if (path is not null && tracked is not null)
            {
                // Путь оставляем: бежим ещё ChaseLostMilliseconds, потом сдаёмся.
                FileLog.Trace("live", $"chase-miss raw={_redRawCount} tracked={Px(scan.Bounds, tracked.Value)} player={Px(scan.Bounds, position)}");
                return false;
            }

            waiting = WaitForLive(kind);
            if (kind == AnchorSource.Skull)
                FileLog.Trace("live", $"wait confirmed=0 raw={_redRawCount} player={Px(scan.Bounds, position)}");
            return false;
        }

        bool moved = tracked is not MapPoint known
            || PathCourse.Distance(scan.Bounds, known, red) >= RedRetargetPixels;
        if (path is not null && pilot is not null && !moved)
            return false;

        bool updating = path is not null && pilot is not null;
        string label = kind switch
        {
            AnchorSource.Chest => "chest",
            AnchorSource.QuestQuestion => "quest?",
            AnchorSource.QuestExclaim => "quest!",
            _ => "red",
        };
        if (!TryBuildPath(config, scenario, block, leg, shortest, frame, scan, position, red, kind, blocked, out List<MapPoint>? rebuilt, out waiting))
        {
            FileLog.Trace("path", $"fail update={updating} player={Px(scan.Bounds, position)} {label}={Px(scan.Bounds, red)} shift={(tracked is MapPoint knownPoint ? Num(PathCourse.Distance(scan.Bounds, knownPoint, red)) : "-")} reason={waiting ?? "-"}");
            return false;
        }

        FileLog.Trace("path", $"ok update={updating} pts={rebuilt!.Count} player={Px(scan.Bounds, position)} {label}={Px(scan.Bounds, red)} end={Px(scan.Bounds, rebuilt[^1])} shift={(tracked is MapPoint from ? Num(PathCourse.Distance(scan.Bounds, from, red)) : "-")}");

        path = rebuilt;
        tracked = red;
        if (pilot is null)
            pilot = new RoutePilot(Tuning(config, block, leg, shortest));
        else
            pilot.Reset();
        segment = int.MinValue;
        return updating;
    }

    private static string WaitFor(AnchorSource source)
        => source switch
        {
            AnchorSource.Chest => "жду сундук на карте.",
            AnchorSource.QuestQuestion => "жду знак вопроса на карте.",
            AnchorSource.QuestExclaim => "жду знак восклицания на карте.",
            _ => "жду красный на карте.",
        };

    private static string WaitForLive(AnchorSource source)
        => source switch
        {
            AnchorSource.Chest => "Жду сундук на карте.",
            AnchorSource.QuestQuestion => "Жду знак вопроса на карте.",
            AnchorSource.QuestExclaim => "Жду знак восклицания на карте.",
            _ => "Жду красный на карте.",
        };

    private static AnchorSource? LiveAnchor(FollowBlock block, int leg, bool shortest)
        => LiveGoal(block, leg, shortest)?.Source;

    private static Anchor? WaitedGoal(FollowBlock block, int leg, bool shortest)
    {
        if (!shortest)
            return LiveGoal(block, leg, shortest);
        return leg >= 0 && leg < block.Anchors.Count && IsLive(block.Anchors[leg].Source)
            ? block.Anchors[leg]
            : null;
    }

    private static Anchor? LiveGoal(FollowBlock block, int leg, bool shortest)
    {
        if (shortest)
        {
            if (leg >= 0 && leg < block.Anchors.Count && IsLive(block.Anchors[leg].Source))
                return block.Anchors[leg];
            if (leg > 0 && IsLive(block.Anchors[leg - 1].Source))
                return block.Anchors[leg - 1];
            return null;
        }

        foreach (Anchor anchor in block.Anchors)
        {
            if (IsLive(anchor.Source))
                return anchor;
        }

        return null;
    }

    private static bool MissingGoalElapsed(Anchor goal, ref long missingSince)
    {
        if (goal.Wait == GoalWait.Skip)
            return true;

        int limit = Math.Clamp(goal.WaitMilliseconds, 0, 120_000);
        if (limit <= 0)
            return true;
        if (missingSince == 0)
            missingSince = Environment.TickCount64;
        return Environment.TickCount64 - missingSince >= limit;
    }

    private static bool LostWhileChasingElapsed(int chaseLostMilliseconds, ref long missingSince)
    {
        int limit = Math.Clamp(chaseLostMilliseconds, 0, 120_000);
        if (limit <= 0)
            return false;
        if (missingSince == 0)
            missingSince = Environment.TickCount64;
        return Environment.TickCount64 - missingSince >= limit;
    }

    private static string MissingGoalText(Anchor goal)
    {
        string name = goal.Source switch
        {
            AnchorSource.Chest => "Сундука",
            AnchorSource.QuestQuestion => "Знака вопроса",
            AnchorSource.QuestExclaim => "Знака восклицания",
            _ => "Красного",
        };
        if (goal.Wait == GoalWait.Skip || goal.WaitMilliseconds <= 0)
            return $"{name} нет. Перехожу к следующему шагу.";

        int ms = Math.Clamp(goal.WaitMilliseconds, 0, 120_000);
        string span = ms % 1000 == 0 ? $"{ms / 1000} с" : $"{ms} мс";
        return $"{name} нет {span}. Перехожу к следующему шагу.";
    }

    private static string LostWhileChasingText(Anchor goal, int chaseLostMilliseconds)
    {
        string name = goal.Source switch
        {
            AnchorSource.Chest => "Сундук",
            AnchorSource.QuestQuestion => "Знак вопроса",
            AnchorSource.QuestExclaim => "Знак восклицания",
            _ => "Красный",
        };
        int ms = Math.Clamp(chaseLostMilliseconds, 0, 120_000);
        string span = ms % 1000 == 0 ? $"{ms / 1000} с" : $"{ms} мс";
        return $"{name} пропал с карты ({span}). Перехожу к следующему шагу.";
    }

    private static bool IsLive(AnchorSource source)
        => source is AnchorSource.Skull or AnchorSource.Chest
            or AnchorSource.QuestQuestion or AnchorSource.QuestExclaim;

    private static PilotTuning Tuning(FollowConfig config, FollowBlock block, int leg, bool shortest)
        => ChestFinish(block, leg, shortest) ? config.ToTuning(ChestArrivalPixels) : config.ToTuning();

    private static bool ChestFinish(FollowBlock block, int leg, bool shortest)
    {
        if (shortest)
            return leg > 0 && leg < block.Anchors.Count && block.Anchors[leg].Source == AnchorSource.Chest;
        return block.Anchors.Count > 0 && block.Anchors[^1].Source == AnchorSource.Chest;
    }

    private bool TryBuildPath(
        FollowConfig config,
        Scenario scenario,
        FollowBlock block,
        int leg,
        bool shortest,
        ColorImage frame,
        MapScan scan,
        MapPoint position,
        MapPoint? preferredRed,
        AnchorSource? liveKind,
        List<MapPoint> blocked,
        out List<MapPoint>? path,
        out string? waiting)
    {
        path = null;
        waiting = null;
        if (!shortest)
        {
            var resolved = new List<MapPoint>(block.Anchors.Count);
            for (int i = 0; i < block.Anchors.Count; i++)
            {
                if (!TryResolve(scenario, block.Anchors[i], frame, scan, position, preferredRed, liveKind, out MapPoint point, out string? reason))
                {
                    waiting = reason;
                    return false;
                }

                resolved.Add(point);
            }

            path = resolved;
            return true;
        }

        if (!TryResolve(scenario, block.Anchors[leg - 1], frame, scan, position, preferredRed, liveKind, out MapPoint from, out string? fromReason))
        {
            waiting = fromReason;
            return false;
        }

        if (!TryResolve(scenario, block.Anchors[leg], frame, scan, position, preferredRed, liveKind, out MapPoint to, out string? toReason))
        {
            waiting = toReason;
            return false;
        }

        if (PathCourse.Distance(scan.Bounds, from, to) < 2)
        {
            path = [from, to];
            return true;
        }

        int stand = block.Anchors[leg].Source == AnchorSource.Chest ? ChestStandPixels : 0;
        if (!MapPathfinder.TryFind(scan, frame, from, to, out List<MapPoint> found, config.WallClearancePixels, blocked, StuckExcludePixels, stand) || found.Count < 2)
        {
            waiting = $"{block.Title}: не нашёл проход по полу.";
            return false;
        }

        path = found;
        return true;
    }

    private bool TryResolve(
        Scenario scenario,
        Anchor anchor,
        ColorImage frame,
        MapScan scan,
        MapPoint player,
        MapPoint? preferredRed,
        AnchorSource? liveKind,
        out MapPoint point,
        out string? waiting)
    {
        waiting = null;
        switch (anchor.Source)
        {
            case AnchorSource.Fixed:
                point = anchor.Position;
                return true;
            case AnchorSource.Player:
                point = player;
                return true;
            case AnchorSource.RouteStart:
                if (scenario.FirstFixedPoint() is MapPoint start)
                {
                    point = start;
                    return true;
                }

                point = default;
                waiting = "В сценарии нет нарисованной первой точки.";
                return false;
            case AnchorSource.Skull:
                return ResolveIcon(AnchorSource.Skull, "Жду красный на карте.", frame, scan, player, preferredRed, liveKind, out point, out waiting);
            case AnchorSource.Chest:
                return ResolveIcon(AnchorSource.Chest, "Жду сундук на карте.", frame, scan, player, preferredRed, liveKind, out point, out waiting);
            case AnchorSource.QuestQuestion:
                return ResolveIcon(AnchorSource.QuestQuestion, "Жду знак вопроса на карте.", frame, scan, player, preferredRed, liveKind, out point, out waiting);
            case AnchorSource.QuestExclaim:
                return ResolveIcon(AnchorSource.QuestExclaim, "Жду знак восклицания на карте.", frame, scan, player, preferredRed, liveKind, out point, out waiting);
            default:
                point = default;
                waiting = "Точка маршрута не задана.";
                return false;
        }
    }

    private bool ResolveIcon(
        AnchorSource source,
        string missing,
        ColorImage frame,
        MapScan scan,
        MapPoint player,
        MapPoint? preferred,
        AnchorSource? liveKind,
        out MapPoint point,
        out string? waiting)
    {
        MapPoint? nearest = liveKind == source ? preferred : null;
        nearest ??= NearestIcon(scan.Bounds, Icons(source, frame, scan), player);
        if (nearest is MapPoint icon)
        {
            point = icon;
            waiting = null;
            return true;
        }

        point = default;
        waiting = missing;
        return false;
    }

    private readonly record struct IconSpot(MapPoint Point);

    private List<IconSpot> Icons(AnchorSource source, ColorImage frame, MapScan scan)
    {
        var spots = new List<IconSpot>();
        if (!scan.HasMap)
            return spots;
        switch (source)
        {
            case AnchorSource.Chest:
                foreach (ChestMark mark in ChestFinder.FindAll(frame, scan.Bounds))
                    spots.Add(new IconSpot(mark.Point));
                break;
            case AnchorSource.QuestQuestion:
                foreach (QuestMark mark in QuestMarkFinder.FindAll(frame, scan.Bounds, QuestMarkKind.Question))
                    spots.Add(new IconSpot(mark.Point));
                break;
            case AnchorSource.QuestExclaim:
                foreach (QuestMark mark in QuestMarkFinder.FindAll(frame, scan.Bounds, QuestMarkKind.Exclaim))
                    spots.Add(new IconSpot(mark.Point));
                break;
            default:
                foreach (AntagonistMark mark in ConfirmedReds(frame, scan))
                    spots.Add(new IconSpot(mark.Point));
                break;
        }

        return spots;
    }

    private List<AntagonistMark> ConfirmedReds(ColorImage frame, MapScan scan)
    {
        if (!scan.HasMap)
        {
            _reds.Reset();
            _redFrame = null;
            _redConfirmed = [];
            _redRawCount = 0;
            return [];
        }

        // Один Update на кадр: Shot/Reds и Icons вызываются из одного прохода.
        if (ReferenceEquals(_redFrame, frame))
            return _redConfirmed;

        List<AntagonistMark> raw = AntagonistFinder.FindAll(frame, scan);
        _redRawCount = raw.Count;
        _redConfirmed = _reds.Update(scan, raw);
        _redFrame = frame;
        return _redConfirmed;
    }

    private static MapPoint? MatchingIcon(MapBounds bounds, List<IconSpot> icons, MapPoint locked)
    {
        foreach (IconSpot mark in icons)
        {
            if (PathCourse.Distance(bounds, locked, mark.Point) <= SameSkullPixels)
                return mark.Point;
        }

        return null;
    }

    private static MapPoint? NearestIcon(MapBounds bounds, List<IconSpot> icons, MapPoint player)
    {
        MapPoint? best = null;
        double bestDistance = double.MaxValue;
        foreach (IconSpot mark in icons)
        {
            double distance = PathCourse.Distance(bounds, player, mark.Point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = mark.Point;
            }
        }

        return best;
    }

    private static IReadOnlyList<RouteKey> KeysOf(FollowBlock block, int segment)
        => segment >= 0 && segment + 1 < block.Anchors.Count ? block.Anchors[segment + 1].Keys : [];


    private bool TryCapture(
        FollowConfig config,
        bool pinWindow,
        CancellationToken token,
        out Rectangle? client,
        out ColorImage? frame,
        out MapScan? scan)
    {
        client = null;
        frame = null;
        scan = null;
        if (pinWindow)
        {
            if (!GameWindowFocus.TryFocus(config.GameWindowTitleContains, out Rectangle live))
            {
                FileLog.Trace("window", $"focus-failed title={config.GameWindowTitleContains}");
                if (WatchMinimapLoss(config, token, inWorld: false, client: null))
                    return false;
                Report("Окно игры не найдено. Проверьте gameWindowTitleContains.");
                return false;
            }

            client = live;
        }

        if (token.IsCancellationRequested)
            return false;

        frame = ScreenMapSource.Capture(config.MapRectangle(client));
        scan = MapScanner.Scan(frame);
        bool mapOk = scan.HasMap && scan.PlayerFound;
        bool shopOk = _doorActive || HasShopIcon(config, client);
        bool inWorld = mapOk && shopOk;
        if (WatchMinimapLoss(config, token, inWorld, client))
            return false;
        // Ложная «карта» без магазина (лаунчер/чужое окно) — ждём, не кликаем.
        if (!inWorld && !_doorActive)
            scan = MapScanner.None(frame.Width, frame.Height);

        return true;
    }

    /// <summary>
    /// Копит время вне игрового мира; при пороге запускает <see cref="GameRestart"/>.
    /// «В мире» = миникарта + маркер игрока + синий магазин (кроме блока двери).
    /// Во время «Войти в дверь» и полосы загрузки таймер не идёт.
    /// </summary>
    /// <returns>true — нужно прервать текущий блок (_restarted или _halt).</returns>
    private bool WatchMinimapLoss(FollowConfig config, CancellationToken token, bool inWorld, Rectangle? client)
    {
        if (!config.Restart.IsArmed || _restartBusy || token.IsCancellationRequested)
            return _restarted || _halt;

        if (_doorActive)
        {
            _minimapLostSince = 0;
            return false;
        }

        if (!inWorld && client is Rectangle area)
        {
            try
            {
                ColorImage window = ScreenMapSource.Capture(config.ViewRectangle(area));
                if (EntranceFinder.HasLoadingBar(window))
                {
                    _minimapLostSince = 0;
                    return false;
                }
            }
            catch
            {
                // нет кадра — считаем как потерю ниже
            }
        }

        if (inWorld)
        {
            _minimapLostSince = 0;
            return false;
        }

        long now = Environment.TickCount64;
        if (_minimapLostSince == 0)
            _minimapLostSince = now;

        long lostFor = now - _minimapLostSince;
        if (lostFor < config.Restart.MinimapLossMilliseconds)
            return false;

        return TriggerGameRestart(config, token, $"Мир не виден {lostFor} мс (лимит {config.Restart.MinimapLossMilliseconds})");
    }

    private bool TriggerGameRestart(FollowConfig config, CancellationToken token, string reason)
    {
        _restartBusy = true;
        try
        {
            Report(reason + " — перезапуск игры.");
            FileLog.Trace("restart", $"trigger {reason}");
            MouseClicker.Release();
            if (GameRestart.TryRecover(config, Report, token))
            {
                _restarted = true;
                _minimapLostSince = 0;
                if (config.HasMapInClient)
                    PlaceWindow(config, token);
                return true;
            }

            _halt = true;
            Report("Перезапуск не удался — сценарий остановлен.");
            return true;
        }
        finally
        {
            _restartBusy = false;
        }
    }

    /// <summary>
    /// Перед сценарием: окно + мир (магазин и миникарта). Если игры нет и автоперезапуск включён — поднимаем клиент.
    /// </summary>
    private bool EnsureGameReady(FollowConfig config, bool pinWindow, CancellationToken token)
    {
        if (!pinWindow)
        {
            Report("gameWindowTitleContains не задан — ведение без привязки к окну.");
            return true;
        }

        int attempts = config.Restart.IsArmed ? Math.Max(1, config.Restart.MaxAttempts) : 1;
        for (int attempt = 1; attempt <= attempts && !token.IsCancellationRequested; attempt++)
        {
            if (PlaceWindow(config, token) && IsInGameWorld(config, out _))
            {
                Report("Игровой мир готов (миникарта и магазин).");
                return true;
            }

            if (!config.Restart.IsArmed)
            {
                Report("Окно игры / мир не готовы. Включите автоперезапуск или запустите игру.");
                FileLog.Trace("window", "ensure-failed no-restart");
                return false;
            }

            Report($"Мир не готов ({attempt}/{attempts}) — запуск/перезапуск игры…");
            if (!GameRestart.TryRecover(config, Report, token))
            {
                _halt = true;
                return false;
            }

            _minimapLostSince = 0;
        }

        Report("Не удалось подготовить игровой мир.");
        return false;
    }

    private bool IsInGameWorld(FollowConfig config, out Rectangle client)
    {
        client = Rectangle.Empty;
        if (!GameWindowFocus.TryFocus(config.GameWindowTitleContains, out client))
            return false;
        return ProbeWorld(config, client).InWorld;
    }

    private static bool HasShopIcon(FollowConfig config, Rectangle? client)
    {
        if (client is not Rectangle area)
            return false;
        try
        {
            ColorImage window = ScreenMapSource.Capture(config.ViewRectangle(area));
            return LauncherUiFinder.HasShopIcon(window);
        }
        catch
        {
            return false;
        }
    }

    private static (bool InWorld, bool Loading, bool Shop, bool Map) ProbeWorld(FollowConfig config, Rectangle client)
    {
        try
        {
            ColorImage window = ScreenMapSource.Capture(config.ViewRectangle(client));
            ColorImage map = ScreenMapSource.Capture(config.MapRectangle(client));
            bool loading = EntranceFinder.HasLoadingBar(window);
            bool shop = LauncherUiFinder.HasShopIcon(window);
            MapScan scan = MapScanner.Scan(map);
            bool mapOk = scan.HasMap && scan.PlayerFound;
            return (shop && mapOk && !loading, loading, shop, mapOk);
        }
        catch
        {
            return (false, false, false, false);
        }
    }

    private bool WaitRemainder(FollowConfig config, long started, CancellationToken token)
        => WaitRemainder(config, started, token, config.CaptureIntervalMilliseconds);

    /// <summary>Пауза между кадрами движения: в 2 раза чаще, чем обычный captureInterval.</summary>
    private bool WaitMove(FollowConfig config, long started, CancellationToken token)
        => WaitRemainder(config, started, token, Math.Max(50, config.CaptureIntervalMilliseconds / 2));

    private bool WaitRemainder(FollowConfig config, long started, CancellationToken token, int intervalMilliseconds)
    {
        int spent = (int)(Environment.TickCount64 - started);
        int wait = Math.Max(0, intervalMilliseconds - spent);
        return wait <= 0 || !token.WaitHandle.WaitOne(wait);
    }

    private static void ArmKeys(List<ScheduledKey> keys)
    {
        long now = Environment.TickCount64;
        foreach (ScheduledKey key in keys)
            key.NextTick = now;
    }

    private bool Wait(int milliseconds, Func<int, string> message, CancellationToken token)
    {
        int left = Math.Max(0, milliseconds);
        if (left > 0)
            FileLog.Trace("wait", $"ms={left} {message(left)}");
        while (left > 0 && !token.IsCancellationRequested)
        {
            Report(message(left));
            int slice = Math.Min(200, left);
            if (token.WaitHandle.WaitOne(slice))
                return false;
            left -= slice;
        }

        return !token.IsCancellationRequested;
    }

    private bool PlaceWindow(FollowConfig config, CancellationToken token)
    {
        if (!GameWindowFocus.TryPlace(
                config.GameWindowTitleContains,
                config.Window.ClientX,
                config.Window.ClientY,
                config.Window.ClientWidth,
                config.Window.ClientHeight,
                out Rectangle client))
        {
            FileLog.Trace("window", $"place-failed title={config.GameWindowTitleContains}");
            Report("Окно игры не найдено. Проверьте gameWindowTitleContains.");
            return false;
        }

        FileLog.Trace("window", $"placed client={client.X},{client.Y} {client.Width}x{client.Height} map={config.MapInClient.X},{config.MapInClient.Y} {config.MapInClient.Width}x{config.MapInClient.Height}");
        Report(
            $"Окно поставлено: поле {client.Width}×{client.Height} в ({client.X}, {client.Y}), " +
            $"карта со смещением {config.MapInClient.X},{config.MapInClient.Y}.");
        return config.FocusSettleMilliseconds <= 0 || !token.WaitHandle.WaitOne(config.FocusSettleMilliseconds);
    }

    private static List<ScheduledKey> Schedule(IReadOnlyList<RouteKey> keys)
    {
        long now = Environment.TickCount64;
        var schedule = new List<ScheduledKey>();
        foreach (RouteKey item in keys)
        {
            if (item.Key == Keys.None || item.Key is Keys.F6 or Keys.F7 or Keys.F9)
                continue;
            Keys key = item.Key;
            if (key is Keys.F6 or Keys.F7 or Keys.F9)
                continue;

            schedule.Add(new ScheduledKey
            {
                Key = key,
                Interval = Math.Clamp(item.Interval, 50, 120_000),
                NextTick = now,
            });
        }

        return schedule;
    }

    private string PressDue(FollowConfig config, List<ScheduledKey> keys, Rectangle? client, CancellationToken token)
    {
        long now = Environment.TickCount64;
        var due = new List<ScheduledKey>();
        foreach (ScheduledKey key in keys)
        {
            if (!token.IsCancellationRequested && now >= key.NextTick)
                due.Add(key);
        }

        if (due.Count == 0 || token.WaitHandle.WaitOne(60))
            return "";

        IntPtr window = IntPtr.Zero;
        bool focused = string.IsNullOrWhiteSpace(config.GameWindowTitleContains);
        if (!focused)
            focused = GameWindowFocus.TryActivate(config.GameWindowTitleContains, out window);
        if (!focused && client is Rectangle area && window != IntPtr.Zero)
            window = GameWindowFocus.TargetAt(window, new Point(area.Left + area.Width / 2, area.Top + area.Height / 2));

        var pressed = new List<string>();
        foreach (ScheduledKey key in due)
        {
            if (token.IsCancellationRequested)
                break;

            try
            {
                if (focused)
                    KeyPresser.Tap(key.Key, 100);
                else
                    KeyPresser.Post(window, key.Key, 100);
                key.NextTick = Environment.TickCount64 + key.Interval;
                pressed.Add(FormatKey(key.Key));
            }
            catch (Exception exception)
            {
                key.NextTick = Environment.TickCount64 + key.Interval;
                pressed.Add(exception.Message);
            }
        }

        if (!focused && pressed.Count > 0)
            pressed.Add("окно игры не в фокусе");
        return string.Join(", ", pressed);
    }

    private static string FormatKey(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9)
            return ((int)(key - Keys.D0)).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return "Num" + (int)(key - Keys.NumPad0);
        return key.ToString();
    }

    private static bool FaceTarget(
        FollowConfig config,
        MapBounds bounds,
        MapPoint player,
        MapPoint target,
        Rectangle? client,
        ref double? facedDegrees)
    {
        var (x0, y0) = bounds.ToPixel(player);
        var (x1, y1) = bounds.ToPixel(target);
        double dx = x1 - x0;
        double dy = y1 - y0;
        double radians = config.RotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double x = dx * cos - dy * sin;
        double y = dx * sin + dy * cos;
        if (config.InvertX)
            x = -x;
        if (config.InvertY)
            y = -y;

        double length = Math.Sqrt(x * x + y * y);
        if (length < 1)
            return false;

        double heading = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        if (facedDegrees is double previous && HeadingDelta(previous, heading) < 20)
            return false;

        const double faceDistance = 70;
        var command = new PilotCommand(
            true,
            x / length * faceDistance,
            y / length * faceDistance,
            heading,
            0,
            length,
            false,
            false,
            false,
            "Разворот к цели.");
        Click(config, command, client);
        facedDegrees = heading;
        return true;
    }

    private static double HeadingDelta(double from, double to)
    {
        double delta = Math.Abs(to - from) % 360;
        return delta > 180 ? 360 - delta : delta;
    }

    private enum NoPathRecovery
    {
        NotYet,
        Continue,
        Stop,
    }

    /// <summary>
    /// Прохода нет, и точка стоит дольше настройки. Тогда вывод из застревания
    /// всё равно запускается: первое движение мыши — прочь от цели.
    /// </summary>
    private NoPathRecovery RecoverNoPath(
        FollowConfig config,
        Scenario scenario,
        FollowBlock block,
        int leg,
        bool shortest,
        bool pinWindow,
        CancellationToken token,
        ColorImage frame,
        MapScan scan,
        MapPoint position,
        string? waiting,
        List<MapPoint> blocked,
        ref List<MapPoint>? path,
        ref RoutePilot? pilot,
        ref int segment,
        ref MapPoint? trackedRed,
        ref MapPoint stillAt,
        ref long stillSince,
        ref bool stillReady)
    {
        if (config.StuckRecoveryMilliseconds <= 0 || !stillReady)
            return NoPathRecovery.NotYet;
        if (waiting is null || !waiting.Contains("не нашёл проход по полу", StringComparison.Ordinal))
            return NoPathRecovery.NotYet;
        if (Environment.TickCount64 - stillSince < config.StuckRecoveryMilliseconds)
            return NoPathRecovery.NotYet;

        double heading = GoalHeading(config, scenario, block, leg, frame, scan, position, trackedRed);
        FileLog.Trace("unstick", $"no-path heading={Num(heading)} player={Px(scan.Bounds, position)}");
        UnstickResult result = Unstick(
            config, scenario, block, leg, shortest, pinWindow, token, heading,
            stillAt, blocked, ref path, ref pilot, ref segment, ref trackedRed);
        if (result == UnstickResult.Stop)
            return NoPathRecovery.Stop;

        stillAt = position;
        stillSince = Environment.TickCount64;
        stillReady = true;
        return NoPathRecovery.Continue;
    }

    private double GoalHeading(
        FollowConfig config,
        Scenario scenario,
        FollowBlock block,
        int leg,
        ColorImage frame,
        MapScan scan,
        MapPoint position,
        MapPoint? preferredRed)
    {
        if (leg <= 0 || leg >= block.Anchors.Count)
            return 0;
        if (!TryResolve(scenario, block.Anchors[leg], frame, scan, position, preferredRed, LiveAnchor(block, leg, true), out MapPoint goal, out _))
            return 0;

        var (x0, y0) = scan.Bounds.ToPixel(position);
        var (x1, y1) = scan.Bounds.ToPixel(goal);
        double dx = x1 - x0;
        double dy = y1 - y0;
        double radians = config.RotationDegrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double x = dx * cos - dy * sin;
        double y = dx * sin + dy * cos;
        if (config.InvertX)
            x = -x;
        if (config.InvertY)
            y = -y;
        if (Math.Abs(x) < 0.001 && Math.Abs(y) < 0.001)
            return 0;

        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private enum UnstickResult
    {
        Moved,
        HeldStill,
        Stop,
    }

    private static void WatchStill(
        FollowConfig config,
        MapBounds bounds,
        MapPoint position,
        ref MapPoint stillAt,
        ref long stillSince,
        ref bool stillReady)
    {
        double limit = Math.Max(0, config.StuckMovePixels);
        if (!stillReady || PathCourse.Distance(bounds, stillAt, position) > limit)
        {
            stillAt = position;
            stillSince = Environment.TickCount64;
            stillReady = true;
        }
    }

    /// <summary>
    /// По кругу ставятся такие же клики, как при обычном шаге. Сначала в сторону,
    /// противоположную застреванию. Кнопка не удерживается.
    /// Сдвиг точки на кратчайшем маршруте — повод построить путь заново без места застревания.
    /// На нарисованном маршруте после сдвига ведение продолжается по тем же точкам.
    /// Полный оборот без сдвига — нажать клавиши из настройки.
    /// </summary>
    private UnstickResult Unstick(
        FollowConfig config,
        Scenario scenario,
        FollowBlock block,
        int leg,
        bool shortest,
        bool pinWindow,
        CancellationToken token,
        double startHeading,
        MapPoint stuckAt,
        List<MapPoint> blocked,
        ref List<MapPoint>? path,
        ref RoutePilot? pilot,
        ref int segment,
        ref MapPoint? trackedRed)
    {
        double escapeHeading = (startHeading + 180) % 360;
        if (escapeHeading < 0)
            escapeHeading += 360;
        FileLog.Trace("unstick", $"start player={Pt(stuckAt)} heading={Num(startHeading)} escape={Num(escapeHeading)} shift={config.StuckRecoveryShiftPixels} blocked={blocked.Count} wait={config.StuckRecoveryMilliseconds}");
        try
        {
            for (int turned = 0; turned < 360; turned += SweepStepDegrees)
            {
                if (token.IsCancellationRequested)
                    return UnstickResult.Stop;
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return UnstickResult.Stop;

                double heading = (escapeHeading + turned) % 360;
                if (heading < 0)
                    heading += 360;
                ClickHeading(config, client, heading);

                string text = $"Застрял. Клик, направление {heading.ToString("0", CultureInfo.CurrentCulture)}°.";
                Report(text);
                FileLog.Trace("unstick", $"angle={Num(heading)}");
                int dwell = Math.Max(200, config.CaptureIntervalMilliseconds);
                if (token.WaitHandle.WaitOne(dwell))
                    return UnstickResult.Stop;
                if (!TryCapture(config, pinWindow, token, out client, out frame, out scan))
                    return UnstickResult.Stop;
                if (!scan!.HasMap || !scan.PlayerFound)
                    continue;

                var position = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                double moved = PathCourse.Distance(scan.Bounds, stuckAt, position);
                Publish(config, client, text, Shot(scan, frame, position, trackedRed, path, client, $"unstick angle={Num(heading)} moved={Num(moved)}"));
                if (!IsPlausibleUnstickMove(moved, config))
                {
                    if (moved > MaxUnstickJumpPixels)
                        FileLog.Trace("unstick", $"reject-jump moved={Num(moved)} max={MaxUnstickJumpPixels}");
                    continue;
                }

                // Векторы: путь тот же — не пересоздаём пилот (иначе TryProject прыгает на ближайший
                // сегмент впереди и пропускает кусок нарисованного маршрута).
                if (!shortest)
                {
                    FileLog.Trace("unstick", $"moved={Num(moved)} at={Px(scan.Bounds, position)} mode=vectors keep-pilot seg={segment}");
                    if (path is not null && pilot is not null)
                    {
                        pilot.ClearMotion();
                        const string kept = "Сдвинулся. Продолжаю нарисованный маршрут.";
                        Publish(config, client, kept, Shot(scan, frame, position, trackedRed, path, client, $"unstick keep pts={path.Count} seg={segment}"));
                        Report(kept);
                        FileLog.Trace("unstick", $"vectors keep pts={path.Count} seg={segment}");
                        return UnstickResult.Moved;
                    }

                    if (!TryBuildPath(config, scenario, block, leg, shortest: false, frame!, scan, position, trackedRed, LiveAnchor(block, leg, shortest: false), blocked, out List<MapPoint>? vectorsPath, out string? vectorsWait)
                        || vectorsPath is null)
                    {
                        path = null;
                        pilot = null;
                        segment = int.MinValue;
                        string failed = vectorsWait ?? "Обход места застревания не найден.";
                        Publish(config, client, failed, Shot(scan, frame, position, trackedRed, null, client, "unstick vectors-fail"));
                        Report(failed);
                        FileLog.Trace("unstick", $"vectors replan fail {failed}");
                        return UnstickResult.Moved;
                    }

                    path = vectorsPath;
                    pilot = new RoutePilot(Tuning(config, block, leg, shortest: false));
                    int resumeAt = segment >= 0 ? segment + 1 : 0;
                    if (segment >= 0)
                        pilot.ResumeAtCheckpoint(resumeAt);
                    segment = int.MinValue;
                    const string restored = "Сдвинулся. Продолжаю нарисованный маршрут.";
                    Publish(config, client, restored, Shot(scan, frame, position, trackedRed, path, client, $"unstick resume pts={path.Count} checkpoint={resumeAt}"));
                    Report(restored);
                    FileLog.Trace("unstick", $"vectors resume pts={path.Count} checkpoint={resumeAt}");
                    return UnstickResult.Moved;
                }

                blocked.Add(stuckAt);
                FileLog.Trace("unstick", $"moved={Num(moved)} at={Px(scan.Bounds, position)} mode=short exclude={Px(scan.Bounds, stuckAt)} radius={StuckExcludePixels}");
                if (!TryBuildPath(config, scenario, block, leg, shortest: true, frame!, scan, position, trackedRed, LiveAnchor(block, leg, shortest: true), blocked, out List<MapPoint>? rebuilt, out string? waiting))
                {
                    path = null;
                    pilot = null;
                    segment = int.MinValue;
                    string failed = waiting ?? "Обход места застревания не найден.";
                    Publish(config, client, failed, Shot(scan, frame, position, trackedRed, null, client, $"unstick fail blocked={blocked.Count}"));
                    Report(failed);
                    FileLog.Trace("unstick", $"replan fail {failed}");
                    return UnstickResult.Moved;
                }

                path = rebuilt;
                pilot = new RoutePilot(Tuning(config, block, leg, shortest: true));
                segment = int.MinValue;
                string built = $"Сдвинулся. Путь построен в обход места застревания, таких мест {blocked.Count}.";
                Publish(config, client, built, Shot(scan, frame, position, trackedRed, path, client, $"unstick pts={path!.Count} blocked={blocked.Count}"));
                Report(built);
                FileLog.Trace("unstick", $"replan ok pts={path.Count} end={Px(scan.Bounds, path[^1])}");
                return UnstickResult.Moved;
            }
        }
        finally
        {
            MouseClicker.Release();
        }

        string stuck = PressRecoveryKeys(config, pinWindow, token);
        Report(stuck);
        FileLog.Trace("unstick", "full circle");
        return UnstickResult.HeldStill;
    }

    /// <summary>
    /// Тот же вывод из застревания, что у «Следовать маршруту», но к одной цели на карте
    /// (сундук, дверь): после сдвига путь строится заново с обходом места застревания.
    /// </summary>
    private static bool IsPlausibleUnstickMove(double moved, FollowConfig config)
        => moved >= config.StuckRecoveryShiftPixels && moved <= MaxUnstickJumpPixels;

    /// <summary>Пилот может остаться «на финише» после ложного прыжка маркера — требуем реальную близость.</summary>
    private static bool ArrivedNearGoal(PilotCommand command, double distance, double approach)
        => command.Arrived && distance <= Math.Max(approach * 3, 36);

    private UnstickResult UnstickToGoal(
        FollowConfig config,
        bool pinWindow,
        CancellationToken token,
        double startHeading,
        MapPoint stuckAt,
        MapPoint goal,
        double approach,
        int standRadius,
        List<MapPoint> blocked,
        ref List<MapPoint>? path,
        ref RoutePilot? pilot)
    {
        double escapeHeading = (startHeading + 180) % 360;
        if (escapeHeading < 0)
            escapeHeading += 360;
        FileLog.Trace("unstick", $"goal-start player={Pt(stuckAt)} heading={Num(startHeading)} escape={Num(escapeHeading)} shift={config.StuckRecoveryShiftPixels} blocked={blocked.Count} wait={config.StuckRecoveryMilliseconds}");
        try
        {
            for (int turned = 0; turned < 360; turned += SweepStepDegrees)
            {
                if (token.IsCancellationRequested)
                    return UnstickResult.Stop;
                if (!TryCapture(config, pinWindow, token, out Rectangle? client, out ColorImage? frame, out MapScan? scan))
                    return UnstickResult.Stop;

                double heading = (escapeHeading + turned) % 360;
                if (heading < 0)
                    heading += 360;
                ClickHeading(config, client, heading);

                string text = $"Застрял. Клик, направление {heading.ToString("0", CultureInfo.CurrentCulture)}°.";
                Report(text);
                FileLog.Trace("unstick", $"goal-angle={Num(heading)}");
                int dwell = Math.Max(200, config.CaptureIntervalMilliseconds);
                if (token.WaitHandle.WaitOne(dwell))
                    return UnstickResult.Stop;
                if (!TryCapture(config, pinWindow, token, out client, out frame, out scan))
                    return UnstickResult.Stop;
                if (!scan!.HasMap || !scan.PlayerFound)
                    continue;

                var position = scan.Bounds.ToNormalized(scan.PlayerX, scan.PlayerY);
                double moved = PathCourse.Distance(scan.Bounds, stuckAt, position);
                Publish(config, client, text, Shot(scan, frame, position, goal, path, client, $"unstick-goal angle={Num(heading)} moved={Num(moved)}"));
                if (!IsPlausibleUnstickMove(moved, config))
                {
                    if (moved > MaxUnstickJumpPixels)
                        FileLog.Trace("unstick", $"goal-reject-jump moved={Num(moved)} max={MaxUnstickJumpPixels}");
                    continue;
                }

                blocked.Add(stuckAt);
                FileLog.Trace("unstick", $"goal-moved={Num(moved)} at={Px(scan.Bounds, position)} exclude={Px(scan.Bounds, stuckAt)} radius={StuckExcludePixels} blocked={blocked.Count}");
                if (!MapPathfinder.TryFind(scan, frame!, position, goal, out List<MapPoint> rebuilt, config.WallClearancePixels, blocked, StuckExcludePixels, standRadius)
                    || rebuilt.Count < 2)
                {
                    path = null;
                    pilot = null;
                    const string failed = "Обход места застревания не найден.";
                    Publish(config, client, failed, Shot(scan, frame, position, goal, null, client, $"unstick-goal fail blocked={blocked.Count}"));
                    Report(failed);
                    FileLog.Trace("unstick", $"goal-replan fail blocked={blocked.Count}");
                    return UnstickResult.Moved;
                }

                path = rebuilt;
                pilot = new RoutePilot(config.ToTuning(approach));
                string built = $"Сдвинулся. Путь к цели построен в обход места застревания, таких мест {blocked.Count}.";
                Publish(config, client, built, Shot(scan, frame, position, goal, path, client, $"unstick-goal pts={path.Count} blocked={blocked.Count}"));
                Report(built);
                FileLog.Trace("unstick", $"goal-replan ok pts={path.Count} end={Px(scan.Bounds, path[^1])}");
                return UnstickResult.Moved;
            }
        }
        finally
        {
            MouseClicker.Release();
        }

        string stuck = PressRecoveryKeys(config, pinWindow, token);
        Report(stuck);
        FileLog.Trace("unstick", "goal-full circle");
        return UnstickResult.HeldStill;
    }

    private static double HeadingToward(FollowConfig config, MapBounds bounds, MapPoint from, MapPoint to)
    {
        if (!TryAimVector(config, bounds, from, to, out double x, out double y))
            return 0;
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private static void ClickHeading(FollowConfig config, Rectangle? client, double headingDegrees)
    {
        double radians = headingDegrees * Math.PI / 180.0;
        double distance = Math.Max(40, config.ClickDistancePixels);
        var command = new PilotCommand(
            true,
            Math.Cos(radians) * distance,
            Math.Sin(radians) * distance,
            headingDegrees,
            0,
            0,
            false,
            true,
            false,
            "Вывод из застревания.");
        Click(config, command, client);
    }

    private string PressRecoveryKeys(FollowConfig config, bool pinWindow, CancellationToken token)
    {
        var keys = new List<Keys>();
        foreach (string name in config.StuckRecoveryKeys)
        {
            if (FollowConfig.TryRecoveryKey(name, out Keys key) && key is not (Keys.F6 or Keys.F7 or Keys.F9))
                keys.Add(key);
        }

        if (keys.Count == 0)
            return "Застрял наглухо. Клавиши выхода не заданы.";

        if (!TryCapture(config, pinWindow, token, out Rectangle? client, out _, out _))
            return "Застрял наглухо. Окно игры не найдено.";

        IntPtr window = IntPtr.Zero;
        bool focused = string.IsNullOrWhiteSpace(config.GameWindowTitleContains);
        if (!focused)
            focused = GameWindowFocus.TryActivate(config.GameWindowTitleContains, out window);
        if (!focused && client is Rectangle area && window != IntPtr.Zero)
            window = GameWindowFocus.TargetAt(window, new Point(area.Left + area.Width / 2, area.Top + area.Height / 2));

        var pressed = new List<string>();
        foreach (Keys key in keys)
        {
            if (token.IsCancellationRequested)
                break;
            try
            {
                if (focused)
                    KeyPresser.Tap(key, 100);
                else
                    KeyPresser.Post(window, key, 100);
                pressed.Add(FormatKey(key));
                FileLog.Trace("unstick", $"key={FormatKey(key)} focused={focused}");
            }
            catch (Exception exception)
            {
                pressed.Add(exception.Message);
                FileLog.Trace("unstick", $"key-fail {exception.Message}");
            }

            if (token.WaitHandle.WaitOne(200))
                break;
        }

        string list = string.Join(", ", pressed);
        return focused
            ? $"Застрял наглухо. Нажато: {list}."
            : $"Застрял наглухо. Нажато: {list}. Окно игры не в фокусе.";
    }

    private static void Click(FollowConfig config, PilotCommand command, Rectangle? client)
    {
        var anchor = config.CharacterAnchor(client);
        var view = config.ViewRectangle(client);
        var click = MouseClicker.Clamp(
            new Point(anchor.X + (int)Math.Round(command.OffsetX), anchor.Y + (int)Math.Round(command.OffsetY)),
            view,
            Math.Max(0, config.ClickMarginPixels));
        FileLog.Trace("click",
            $"screen={click.X},{click.Y} anchor={anchor.X},{anchor.Y} view={view.X},{view.Y} {view.Width}x{view.Height} " +
            $"off={Num(command.OffsetX)},{Num(command.OffsetY)} heading={Num(command.HeadingDegrees)} hold={Math.Max(0, config.ClickHoldMilliseconds)} " +
            $"client={(client is Rectangle area ? $"{area.X},{area.Y} {area.Width}x{area.Height}" : "-")} reason={command.Reason}");
        MouseClicker.LeftClick(click.X, click.Y, Math.Max(0, config.ClickHoldMilliseconds));
    }

    private void WaitForGame(int delayMilliseconds, bool pinWindow, CancellationToken token)
    {
        FileLog.Trace("wait", $"startDelay={Math.Max(0, delayMilliseconds)} pin={pinWindow}");
        int left = Math.Max(0, delayMilliseconds);
        while (left > 0 && !token.IsCancellationRequested)
        {
            string prefix = pinWindow ? "Окно игры будет выставлено" : "Переключитесь на игру. Ведение начнётся";
            Report($"{prefix} через {(left / 1000.0).ToString("0.0", CultureInfo.CurrentCulture)} с.");
            int slice = Math.Min(200, left);
            if (token.WaitHandle.WaitOne(slice))
                return;
            left -= slice;
        }
    }

    private string Shot(
        MapScan scan,
        ColorImage? frame,
        MapPoint? position,
        MapPoint? tracked,
        List<MapPoint>? path,
        Rectangle? client,
        string extra)
    {
        string player = position is MapPoint point
            ? $"player={Px(scan.Bounds, point)} n={Pt(point)} dots={scan.PlayerPixels}"
            : $"playerFound={scan.PlayerFound} raw={Num(scan.PlayerX)},{Num(scan.PlayerY)} dots={scan.PlayerPixels}";
        string reds = frame is not null && scan.HasMap ? Reds(frame, scan, position) : "reds=-";
        string chests = frame is not null && scan.HasMap ? Chests(frame, scan, position) : "chests=-";
        string route = path is { Count: > 0 } routePoints && position is MapPoint from
            ? $"path={routePoints.Count} end={Px(scan.Bounds, routePoints[^1])} distEnd={Num(PathCourse.Distance(scan.Bounds, from, routePoints[^1]))}"
            : "path=0";
        string target = tracked is MapPoint red ? Px(scan.Bounds, red) : "-";
        string window = client is Rectangle area ? $"{area.X},{area.Y} {area.Width}x{area.Height}" : "-";
        return $"{player} bounds={scan.Bounds.Left},{scan.Bounds.Top} {scan.Bounds.Width}x{scan.Bounds.Height} map={scan.HasMap} client={window} tracked={target} {reds} {chests} {route} {extra}";
    }

    private static string Px(MapBounds bounds, MapPoint point)
    {
        var (x, y) = bounds.ToPixel(point);
        return $"{Num(x)},{Num(y)}";
    }

    private static string Pt(MapPoint point) => $"{Num(point.X)},{Num(point.Y)}";

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private string Reds(ColorImage frame, MapScan scan, MapPoint? player)
    {
        if (!scan.HasMap)
            return "reds=-";

        var marks = ConfirmedReds(frame, scan);
        if (marks.Count == 0)
            return _redRawCount == 0 ? "reds=0" : $"reds=0 raw={_redRawCount}";

        string list = string.Join(" ", marks.Take(4).Select(mark =>
        {
            string distance = player is MapPoint from
                ? $"/d{Num(PathCourse.Distance(scan.Bounds, from, mark.Point))}"
                : "";
            return $"{Px(scan.Bounds, mark.Point)}/{mark.Pixels}px{distance}";
        }));
        string extra = marks.Count > 4 ? $" +{marks.Count - 4}" : "";
        string pending = _redRawCount > marks.Count ? $" raw={_redRawCount}" : "";
        return $"reds={marks.Count}{pending} [{list}{extra}]";
    }

    private static string Chests(ColorImage frame, MapScan scan, MapPoint? player)
    {
        if (!scan.HasMap)
            return "chests=-";

        var marks = ChestFinder.FindAll(frame, scan.Bounds);
        if (marks.Count == 0)
            return "chests=0";

        string list = string.Join(" ", marks.Take(4).Select(mark =>
        {
            string distance = player is MapPoint from
                ? $"/d{Num(PathCourse.Distance(scan.Bounds, from, mark.Point))}"
                : "";
            return $"{Px(scan.Bounds, mark.Point)}/{mark.Pixels}px{distance}";
        }));
        string extra = marks.Count > 4 ? $" +{marks.Count - 4}" : "";
        return $"chests={marks.Count} [{list}{extra}]";
    }

    private static string Describe(PilotCommand command)
    {
        if (!command.Click && !command.Arrived)
            return command.Reason;

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{command.Reason} Осталось {command.RemainingPixels:0} px, боковое {command.CrossTrackPixels:0} px, клик {command.HeadingDegrees:0}°.");
    }

    private static string ClickTitle(ScreenClick click, int index, int count)
    {
        string place = $"{index + 1} из {count}";
        return string.IsNullOrWhiteSpace(click.Name) ? place : $"{place}, {click.Name.Trim()}";
    }

    private static void Publish(FollowConfig config, Rectangle? client, string text, string data)
    {
        if (!FileLog.TextEnabled && !FileLog.ShotsEnabled)
            return;

        int number = FileLog.Frame(text, data);
        if (!FileLog.ShotsEnabled)
            return;

        Bitmap? work = ScreenMapSource.Copy(config.ViewRectangle(client));
        if (work is not null)
            FileLog.KeepShot(number, work);
    }

    private void Report(string text)
    {
        EmitProgress();
        if (!text.StartsWith("Пауза", StringComparison.Ordinal) && !text.Contains(" через ", StringComparison.Ordinal))
            FileLog.Write(text);
        Status?.Invoke(text);
    }

    private sealed class ScheduledKey
    {
        public Keys Key { get; init; }

        public int Interval { get; init; }

        public long NextTick { get; set; }
    }
}

/// <summary>Итог одного круга сценария.</summary>
internal readonly record struct LapRecord(
    TimeSpan Duration,
    int ChestsOpened,
    int RedsKilled,
    int Skipped);

/// <summary>Прогресс кругов запущенного сценария.</summary>
internal readonly record struct RunProgress(
    int Lap,
    bool Looping,
    TimeSpan LapElapsed,
    TimeSpan TotalElapsed,
    int ChestsOpened,
    int RedsKilled,
    int Skipped,
    LapRecord? LastLap,
    IReadOnlyList<LapRecord> Laps);
