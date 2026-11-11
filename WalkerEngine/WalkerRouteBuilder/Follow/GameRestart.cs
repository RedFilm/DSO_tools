using System.Diagnostics;
using WalkerEngine;

namespace WalkerRouteBuilder;

/// <summary>
/// Полный цикл: убить клиент → запустить лаунчер → логин A/B → сервер → персонаж → ждать миникарту.
/// </summary>
internal static class GameRestart
{
    public static bool TryRecover(
        FollowConfig config,
        Action<string> report,
        CancellationToken token)
    {
        RestartConfig restart = config.Restart;
        if (string.IsNullOrWhiteSpace(restart.ExecutablePath))
        {
            report("Не указан restart.executablePath — путь к лаунчеру/клиенту.");
            return false;
        }

        if (ResolveLaunchPath(restart.ExecutablePath) is null)
        {
            report($"Файл запуска не найден: {restart.ExecutablePath} (проверьте .lnk/.exe).");
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.GameWindowTitleContains))
        {
            report("Не указан gameWindowTitleContains — не к чему привязать окно.");
            return false;
        }

        for (int attempt = 1; attempt <= restart.MaxAttempts && !token.IsCancellationRequested; attempt++)
        {
            report($"Перезапуск игры {attempt}/{restart.MaxAttempts}…");
            FileLog.Trace("restart", $"attempt={attempt}/{restart.MaxAttempts}");
            MouseClicker.Release();
            try
            {
                if (RunOnce(config, report, token))
                {
                    report("Игра снова в мире (миникарта и магазин на месте).");
                    FileLog.Trace("restart", "ok");
                    return true;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                FileLog.Trace("restart", $"error {exception.Message}");
                report($"Сбой перезапуска: {exception.Message}");
            }

            if (token.IsCancellationRequested)
                break;
            report($"Попытка {attempt} не удалась — закрываю процесс и пробую снова.");
            KillGame(config);
            if (token.WaitHandle.WaitOne(1_500))
                break;
        }

        report("Не удалось перезапустить игру.");
        FileLog.Trace("restart", "failed");
        return false;
    }

    private static bool RunOnce(FollowConfig config, Action<string> report, CancellationToken token)
    {
        RestartConfig restart = config.Restart;
        KillGame(config);
        if (token.WaitHandle.WaitOne(800))
            return false;

        if (!StartExecutable(restart, report))
            return false;

        if (!WaitForWindow(config.GameWindowTitleContains, restart.StepTimeoutMilliseconds, token, report, "лаунчер"))
            return false;

        PlaceLauncher(config);
        if (token.WaitHandle.WaitOne(400))
            return false;

        if (!HandleLauncherLogin(config, report, token))
            return false;

        if (!WaitForCharacterSelect(config, report, token))
            return false;

        PlaceGameClient(config);
        if (token.WaitHandle.WaitOne(Math.Max(200, config.FocusSettleMilliseconds)))
            return false;

        if (!SelectCharacterAndStart(config, report, token))
            return false;

        return WaitForWorldReady(config, report, token);
    }

    private static void KillGame(FollowConfig config)
    {
        // Имя из ярлыка (.lnk) вроде «Drakensang Online (64Bit)» — не процесс; без явного processName
        // гасим только по заголовку окна.
        string? process = string.IsNullOrWhiteSpace(config.Restart.ProcessName)
            ? null
            : config.Restart.ProcessName.Trim();
        GameWindowFocus.KillByTitleOrProcess(config.GameWindowTitleContains, process);
        FileLog.Trace("restart", $"killed title={config.GameWindowTitleContains} process={process ?? "-"}");
    }

    private static bool StartExecutable(RestartConfig restart, Action<string> report)
    {
        string? path = ResolveLaunchPath(restart.ExecutablePath);
        if (path is null)
        {
            report($"Файл не найден: {restart.ExecutablePath}");
            FileLog.Trace("restart", $"start-missing {restart.ExecutablePath}");
            return false;
        }

        string args = restart.ExecutableArguments?.Trim() ?? "";
        var start = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
            UseShellExecute = true,
        };
        Process.Start(start);
        FileLog.Trace("restart", $"started {path} args={args}");
        report(string.IsNullOrWhiteSpace(args)
            ? $"Запущен: {Path.GetFileName(path)}"
            : $"Запущен: {Path.GetFileName(path)} {args}");
        return true;
    }

    /// <summary>Ищет .lnk/.exe, если в конфиге путь без расширения (ярлык из меню Пуск).</summary>
    internal static string? ResolveLaunchPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        string path = configuredPath.Trim().Trim('"');
        if (File.Exists(path))
            return path;

        foreach (string ext in new[] { ".lnk", ".exe", ".bat", ".cmd" })
        {
            string candidate = path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? path : path + ext;
            if (File.Exists(candidate))
                return candidate;
        }

        string? dir = Path.GetDirectoryName(path);
        string name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name) || !Directory.Exists(dir))
            return null;

        foreach (string file in Directory.EnumerateFiles(dir))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private static void PlaceLauncher(FollowConfig config)
    {
        WindowLayoutConfig place = config.Restart.LauncherWindow;
        if (!GameWindowFocus.TryPlace(
                config.GameWindowTitleContains,
                place.ClientX,
                place.ClientY,
                place.ClientWidth,
                place.ClientHeight,
                PreferLauncherWindow,
                out Rectangle client))
        {
            FileLog.Trace("restart", "launcher place failed");
            return;
        }

        FileLog.Trace("restart", $"launcher placed {client.X},{client.Y} {client.Width}x{client.Height}");
    }

    private static void PlaceGameClient(FollowConfig config)
    {
        if (!GameWindowFocus.TryPlace(
                config.GameWindowTitleContains,
                config.Window.ClientX,
                config.Window.ClientY,
                config.Window.ClientWidth,
                config.Window.ClientHeight,
                PreferGameWindow,
                out Rectangle client))
        {
            FileLog.Trace("restart", "game place failed");
            return;
        }

        FileLog.Trace("restart", $"game placed {client.X},{client.Y} {client.Width}x{client.Height}");
    }

    /// <summary>Лаунчер: зелёная кнопка Вход/Играть, без синего магазина.</summary>
    private static bool PreferLauncherWindow(IntPtr hwnd, Rectangle client)
    {
        _ = hwnd;
        try
        {
            ColorImage frame = ScreenMapSource.Capture(client);
            if (LauncherUiFinder.HasShopIcon(frame))
                return false;
            return LauncherUiFinder.TryFindGreenButton(frame, out LauncherUiFinder.GreenButtonKind kind, out int gx, out int gy)
                   && kind != LauncherUiFinder.GreenButtonKind.None
                   && gx > 0
                   && gy > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Клиент: синий магазин (выбор персонажа или мир).</summary>
    private static bool PreferGameWindow(IntPtr hwnd, Rectangle client)
    {
        _ = hwnd;
        try
        {
            ColorImage frame = ScreenMapSource.Capture(client);
            return LauncherUiFinder.HasShopIcon(frame);
        }
        catch
        {
            return false;
        }
    }

    private static bool HandleLauncherLogin(FollowConfig config, Action<string> report, CancellationToken token)
    {
        RestartConfig restart = config.Restart;
        long deadline = Environment.TickCount64 + restart.StepTimeoutMilliseconds;
        while (Environment.TickCount64 < deadline && !token.IsCancellationRequested)
        {
            if (!TryCaptureClient(config, out Rectangle client, out ColorImage? frame) || frame is null)
            {
                if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                    return false;
                continue;
            }

            if (!LauncherUiFinder.TryFindGreenButton(frame, out LauncherUiFinder.GreenButtonKind kind, out int gx, out int gy))
            {
                report("Жду зелёную кнопку лаунчера…");
                if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                    return false;
                continue;
            }

            if (kind == LauncherUiFinder.GreenButtonKind.Play)
            {
                report("Сценарий Б: уже вошли — жму зелёную «Играть».");
                ClickClient(config, client, gx, gy, restart);
                return WaitUntilGreenGoneOrLoading(config, report, token);
            }

            report("Сценарий А: форма входа — логин и пароль.");
            ClickRelative(config, client, restart.UsernameX, restart.UsernameY, restart);
            if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
                return false;
            KeyPresser.ClearField();
            KeyPresser.TypeText(restart.Username);
            if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
                return false;

            ClickRelative(config, client, restart.PasswordX, restart.PasswordY, restart);
            if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
                return false;
            KeyPresser.ClearField();
            KeyPresser.TypeText(restart.Password);
            if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
                return false;

            if (TryCaptureClient(config, out client, out ColorImage? again) && again is not null
                && LauncherUiFinder.TryFindGreenButton(again, out _, out gx, out gy))
            {
                ClickClient(config, client, gx, gy, restart);
            }

            if (!WaitForServerList(config, report, token))
                return false;
            return ClickConfiguredServer(config, report, token);
        }

        report("Таймаут: зелёная кнопка лаунчера не появилась.");
        return false;
    }

    private static bool WaitUntilGreenGoneOrLoading(FollowConfig config, Action<string> report, CancellationToken token)
    {
        RestartConfig restart = config.Restart;
        long deadline = Environment.TickCount64 + restart.StepTimeoutMilliseconds;
        while (Environment.TickCount64 < deadline && !token.IsCancellationRequested)
        {
            if (!TryCaptureClient(config, out _, out ColorImage? frame) || frame is null)
            {
                if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                    return false;
                continue;
            }

            bool green = LauncherUiFinder.TryFindGreenButton(frame, out _, out _, out _);
            bool loading = EntranceFinder.HasLoadingBar(frame);
            if (!green || loading)
            {
                report(loading ? "Загрузка после «Играть»…" : "Лаунчер ушёл с зелёной кнопки.");
                return true;
            }

            if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                return false;
        }

        return true;
    }

    private static bool WaitForServerList(FollowConfig config, Action<string> report, CancellationToken token)
    {
        report("Жду список серверов…");
        RestartConfig restart = config.Restart;
        long deadline = Environment.TickCount64 + restart.StepTimeoutMilliseconds;
        while (Environment.TickCount64 < deadline && !token.IsCancellationRequested)
        {
            if (TryCaptureClient(config, out _, out ColorImage? frame) && frame is not null)
            {
                bool loginGreen = LauncherUiFinder.TryFindGreenButton(frame, out var kind, out _, out _)
                                  && kind == LauncherUiFinder.GreenButtonKind.Login;
                if (!loginGreen)
                    return true;
            }

            if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                return false;
        }

        report("Таймаут списка серверов.");
        return false;
    }

    private static bool ClickConfiguredServer(FollowConfig config, Action<string> report, CancellationToken token)
    {
        RestartConfig restart = config.Restart;
        RestartClickTarget? target = restart.Servers.FirstOrDefault(item =>
            string.Equals(item.Name, restart.Server, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            report($"Сервер «{restart.Server}» не найден в restart.servers.");
            return false;
        }

        if (!GameWindowFocus.TryFocus(config.GameWindowTitleContains, out Rectangle client))
            return false;

        report($"Выбираю сервер «{target.Name}».");
        ClickRelative(config, client, target.X, target.Y, restart);
        if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
            return false;
        ClickRelative(config, client, Math.Min(0.95, target.X + 0.05), target.Y, restart);
        if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
            return false;
        return !token.IsCancellationRequested;
    }

    private static bool WaitForCharacterSelect(FollowConfig config, Action<string> report, CancellationToken token)
    {
        report("Жду экран персонажей (синий магазин)…");
        RestartConfig restart = config.Restart;
        long deadline = Environment.TickCount64 + restart.StepTimeoutMilliseconds;
        while (Environment.TickCount64 < deadline && !token.IsCancellationRequested)
        {
            // Не ресайзим лаунчер под 1024×722 — ищем окно с магазином.
            if (TryCaptureGameClient(config, out _, out ColorImage? frame) && frame is not null)
            {
                if (LauncherUiFinder.HasShopIcon(frame))
                {
                    PlaceGameClient(config);
                    report("Экран персонажей готов.");
                    return true;
                }

                if (EntranceFinder.HasLoadingBar(frame))
                    report("Загрузка перед выбором персонажа…");
            }
            else
            {
                report("Жду окно клиента (магазин)…");
            }

            if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                return false;
        }

        report("Таймаут экрана персонажей.");
        return false;
    }

    private static bool SelectCharacterAndStart(FollowConfig config, Action<string> report, CancellationToken token)
    {
        RestartConfig restart = config.Restart;
        PlaceGameClient(config);
        if (!TryFocusGameClient(config, out Rectangle client))
            return false;

        report($"Клик по слоту персонажа ({restart.CharacterX:0.##}, {restart.CharacterY:0.##}).");
        ClickRelative(config, client, restart.CharacterX, restart.CharacterY, restart);
        if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
            return false;

        // Повторный фокус на клиент — лаунчер мог перехватить окно.
        PlaceGameClient(config);
        if (!TryFocusGameClient(config, out client))
            return false;

        report("Жму «Начать игру».");
        ClickRelative(config, client, restart.StartGameX, restart.StartGameY, restart);
        if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
            return false;

        // Повтор «Начать игру», если первый клик не сработал.
        if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
            return false;
        if (TryCaptureGameClient(config, out client, out ColorImage? stillSelect) && stillSelect is not null
            && LauncherUiFinder.HasShopIcon(stillSelect))
        {
            report("Всё ещё выбор персонажа — повторный клик «Начать игру».");
            ClickRelative(config, client, restart.StartGameX, restart.StartGameY, restart);
            if (token.WaitHandle.WaitOne(restart.ClickDelayMilliseconds))
                return false;
        }

        return !token.IsCancellationRequested;
    }

    private static bool WaitForWorldReady(FollowConfig config, Action<string> report, CancellationToken token)
    {
        report("Жду вход в мир (миникарта + магазин)…");
        RestartConfig restart = config.Restart;
        long deadline = Environment.TickCount64 + restart.StepTimeoutMilliseconds;
        while (Environment.TickCount64 < deadline && !token.IsCancellationRequested)
        {
            PlaceGameClient(config);
            if (!TryFocusGameClient(config, out Rectangle client))
            {
                if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                    return false;
                continue;
            }

            try
            {
                ColorImage window = ScreenMapSource.Capture(config.ViewRectangle(client));
                ColorImage map = ScreenMapSource.Capture(config.MapRectangle(client));
                bool shop = LauncherUiFinder.HasShopIcon(window);
                bool loading = EntranceFinder.HasLoadingBar(window);
                MapScan scan = MapScanner.Scan(map);
                bool ready = shop && scan.HasMap && scan.PlayerFound && !loading;
                if (ready)
                    return true;

                report(loading
                    ? "Загрузка мира…"
                    : $"Жду мир: map={scan.HasMap} player={scan.PlayerFound} shop={shop}");
            }
            catch (Exception exception)
            {
                FileLog.Trace("restart", $"world-wait {exception.Message}");
            }

            if (token.WaitHandle.WaitOne(restart.PollMilliseconds))
                return false;
        }

        report("Таймаут входа в мир.");
        return false;
    }

    private static bool WaitForWindow(string title, int timeoutMs, CancellationToken token, Action<string> report, string label)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !token.IsCancellationRequested)
        {
            if (GameWindowFocus.TryFindWindow(title, out _))
                return true;
            report($"Жду окно ({label})…");
            if (token.WaitHandle.WaitOne(400))
                return false;
        }

        report($"Окно «{title}» не появилось.");
        return false;
    }

    private static bool TryCaptureClient(FollowConfig config, out Rectangle client, out ColorImage? frame)
        => TryCapturePrefer(config, PreferLauncherWindow, out client, out frame)
           || TryCapturePrefer(config, null, out client, out frame);

    private static bool TryCaptureGameClient(FollowConfig config, out Rectangle client, out ColorImage? frame)
        => TryCapturePrefer(config, PreferGameWindow, out client, out frame);

    private static bool TryCapturePrefer(
        FollowConfig config,
        Func<IntPtr, Rectangle, bool>? prefer,
        out Rectangle client,
        out ColorImage? frame)
    {
        frame = null;
        client = Rectangle.Empty;
        if (!GameWindowFocus.TryFocus(config.GameWindowTitleContains, prefer, out client))
            return false;
        try
        {
            frame = ScreenMapSource.Capture(client);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ClickRelative(FollowConfig config, Rectangle _, double nx, double ny, RestartConfig restart)
    {
        if (!TryFocusGameClient(config, out Rectangle client) && !TryFocusAnyClient(config, out client))
            return;
        int x = client.Left + (int)Math.Round(client.Width * nx);
        int y = client.Top + (int)Math.Round(client.Height * ny);
        ClickScreen(config, client, x, y, restart);
    }

    private static void ClickClient(FollowConfig config, Rectangle captureClient, int localX, int localY, RestartConfig restart)
    {
        // Клики лаунчера — любое подходящее окно; игровые клики предпочитают магазин.
        if (!TryFocusAnyClient(config, out Rectangle client))
            return;
        double nx = captureClient.Width > 0 ? localX / (double)captureClient.Width : 0.5;
        double ny = captureClient.Height > 0 ? localY / (double)captureClient.Height : 0.5;
        int x = client.Left + (int)Math.Round(client.Width * Math.Clamp(nx, 0, 1));
        int y = client.Top + (int)Math.Round(client.Height * Math.Clamp(ny, 0, 1));
        ClickScreen(config, client, x, y, restart);
    }

    private static bool TryFocusGameClient(FollowConfig config, out Rectangle client)
    {
        client = Rectangle.Empty;
        if (GameWindowFocus.ListWindows(config.GameWindowTitleContains).Count == 0)
        {
            FileLog.Trace("restart", "click skipped — нет окна");
            return false;
        }

        return GameWindowFocus.TryFocus(config.GameWindowTitleContains, PreferGameWindow, out client)
               && client.Width >= 8 && client.Height >= 8;
    }

    private static bool TryFocusAnyClient(FollowConfig config, out Rectangle client)
    {
        client = Rectangle.Empty;
        if (GameWindowFocus.ListWindows(config.GameWindowTitleContains).Count == 0)
        {
            FileLog.Trace("restart", "click skipped — нет окна");
            return false;
        }

        GameWindowFocus.TryFocusViaTitleBar(config.GameWindowTitleContains, config.ClickHoldMilliseconds);
        return GameWindowFocus.TryFocus(config.GameWindowTitleContains, out client) && client.Width >= 8 && client.Height >= 8;
    }

    private static void ClickScreen(FollowConfig config, Rectangle client, int x, int y, RestartConfig restart)
    {
        Point point = MouseClicker.Clamp(new Point(x, y), client, Math.Max(2, config.ClickMarginPixels));
        if (!client.Contains(point))
        {
            FileLog.Trace("restart", $"click skipped outside client {point.X},{point.Y} vs {client}");
            return;
        }

        MouseClicker.LeftClick(point.X, point.Y, Math.Max(40, config.ClickHoldMilliseconds));
        FileLog.Trace("restart", $"click {point.X},{point.Y}");
        Thread.Sleep(Math.Max(300, restart.ClickDelayMilliseconds));
    }
}
