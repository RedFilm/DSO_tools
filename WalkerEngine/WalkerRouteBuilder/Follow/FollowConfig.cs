using System.Text.Json;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal sealed class FollowConfig
{
    public string Notes { get; set; } =
        "Окно игры при старте ставится так, чтобы его внутреннее поле было clientWidth на clientHeight в точке clientX, clientY. " +
        "mapInClient — миникарта внутри этого поля: по эталону отступ 30,69 и размер 360x320. " +
        "gameWindowTitleContains — часть заголовка окна. " +
        "Персонаж считается от центра внутреннего поля окна: offsetFromCenterX = 0, отрицательный offsetFromCenterY поднимает точку выше центра. " +
        "absoluteX и absoluteY, если заданы оба, заменяют этот центр.";

    public int CaptureIntervalMilliseconds { get; set; } = 250;

    public int StartDelayMilliseconds { get; set; } = 3000;

    public int FocusSettleMilliseconds { get; set; } = 80;

    public string GameWindowTitleContains { get; set; } = "";

    public WindowLayoutConfig Window { get; set; } = new();

    public RegionConfig MapInClient { get; set; } = new() { X = 30, Y = 69, Width = 360, Height = 320 };

    public RegionConfig MapRegion { get; set; } = new();

    public ViewConfig View { get; set; } = new() { UsePrimaryScreen = true, Width = 1920, Height = 1080 };

    public CharacterConfig Character { get; set; } = new() { OffsetFromCenterX = 0, OffsetFromCenterY = -40 };

    public string RouteFile { get; set; } = "";

    public double LookAheadPixels { get; set; } = 80;

    public double RejoinLookAheadPixels { get; set; } = 28;

    public double CorrectionDistancePixels { get; set; } = 16;

    public double ArrivalDistancePixels { get; set; } = 10;

    public double StuckMovePixels { get; set; } = 2;

    public int StuckSampleCount { get; set; } = 3;

    /// <summary>Сколько стоять без сдвига, прежде чем зажать клик и обвести направление. 0 — не делать этого.</summary>
    public int StuckRecoveryMilliseconds { get; set; } = 2000;

    /// <summary>На сколько пикселей карты точка должна сдвинуться во время вывода, чтобы маршрут строился заново.</summary>
    public int StuckRecoveryShiftPixels { get; set; } = 8;

    /// <summary>Клавиши по порядку, если за полный оборот точка так и не сдвинулась. Имена как у Keys, цифра 1 — это D1.</summary>
    public List<string> StuckRecoveryKeys { get; set; } = [];

    public double HeadingToleranceDegrees { get; set; } = 55;

    public double ClickDistancePixels { get; set; } = 180;

    public int ClickHoldMilliseconds { get; set; } = 40;

    public int ClickMarginPixels { get; set; } = 12;

    public bool InvertX { get; set; }

    public bool InvertY { get; set; }

    public double RotationDegrees { get; set; }

    public bool StopWhenArrived { get; set; } = true;

    /// <summary>Сколько ждать после финиша маршрута, прежде чем бежать к значку антагониста. 0 — сразу.</summary>
    public int AntagonistPauseMilliseconds { get; set; } = 3000;

    /// <summary>Желаемый отступ пути от стен и значков, в пикселях карты. В узком коридоре путь всё равно проходит по центру.</summary>
    public int WallClearancePixels { get; set; } = 6;

    public List<KeyPressConfig> Keys { get; set; } = [];

    /// <summary>Если выключено, по маршруту и у черепа клавиши не нажимаются.</summary>
    public bool PressKeys { get; set; } = true;

    /// <summary>Перезапуск клиента при долгой потере миникарты.</summary>
    public RestartConfig Restart { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public PilotTuning ToTuning(double? finalArrivalPixels = null) => new()
    {
        LookAheadPixels = LookAheadPixels,
        RejoinLookAheadPixels = RejoinLookAheadPixels,
        CorrectionDistancePixels = CorrectionDistancePixels,
        ArrivalDistancePixels = ArrivalDistancePixels,
        FinalArrivalDistancePixels = finalArrivalPixels ?? -1,
        StuckMovePixels = StuckMovePixels,
        StuckSampleCount = StuckSampleCount,
        HeadingToleranceDegrees = HeadingToleranceDegrees,
        ClickDistancePixels = ClickDistancePixels,
        InvertX = InvertX,
        InvertY = InvertY,
        RotationDegrees = RotationDegrees,
    };

    public bool HasMapInClient => MapInClient is { Width: >= 8, Height: >= 8 };

    public Rectangle MapRectangle(Rectangle? client = null)
    {
        if (client is Rectangle area && HasMapInClient)
            return new Rectangle(area.X + MapInClient.X, area.Y + MapInClient.Y, MapInClient.Width, MapInClient.Height);

        return new Rectangle(MapRegion.X, MapRegion.Y, MapRegion.Width, MapRegion.Height);
    }

    public Rectangle ViewRectangle(Rectangle? client = null)
    {
        if (client is Rectangle area)
            return area;
        if (View.UsePrimaryScreen)
            return Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;

        return new Rectangle(View.X, View.Y, Math.Max(1, View.Width), Math.Max(1, View.Height));
    }

    public Point CharacterAnchor(Rectangle? client = null)
    {
        if (Character.AbsoluteX is int x && Character.AbsoluteY is int y)
            return new Point(x, y);

        var view = ViewRectangle(client);
        return new Point(
            view.Left + view.Width / 2 + Character.OffsetFromCenterX,
            view.Top + view.Height / 2 + Character.OffsetFromCenterY);
    }

    public static FollowConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var created = new FollowConfig();
            created.Save(path);
            return created;
        }

        var config = JsonSerializer.Deserialize<FollowConfig>(File.ReadAllText(path), JsonOptions) ?? new FollowConfig();
        config.MapRegion ??= new RegionConfig();
        config.Window ??= new WindowLayoutConfig();
        config.MapInClient ??= new RegionConfig { X = 30, Y = 69, Width = 360, Height = 320 };
        config.View ??= new ViewConfig { UsePrimaryScreen = true, Width = 1920, Height = 1080 };
        config.Character ??= new CharacterConfig { OffsetFromCenterY = -40 };
        config.GameWindowTitleContains ??= "";
        config.RouteFile ??= "";
        config.Keys ??= [];
        config.StuckRecoveryKeys ??= [];
        config.Restart ??= new RestartConfig();
        MergeRestartSecrets(config, path);
        config.Restart.Normalize();
        config.CaptureIntervalMilliseconds = Math.Max(50, config.CaptureIntervalMilliseconds);
        config.StuckRecoveryMilliseconds = Math.Clamp(config.StuckRecoveryMilliseconds, 0, 120_000);
        config.StuckRecoveryShiftPixels = Math.Clamp(config.StuckRecoveryShiftPixels, 1, 80);
        config.AntagonistPauseMilliseconds = Math.Clamp(config.AntagonistPauseMilliseconds, 0, 120_000);
        config.WallClearancePixels = Math.Clamp(config.WallClearancePixels, 0, 40);
        return config;
    }

    /// <summary>
    /// Опциональный follow-config.secrets.json рядом с основным конфигом (в git не коммитится).
    /// Перекрывает login/пароль/путь exe и при желании весь блок restart.
    /// </summary>
    private static void MergeRestartSecrets(FollowConfig config, string configPath)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(configPath));
        if (string.IsNullOrWhiteSpace(folder))
            return;
        string secretsPath = Path.Combine(folder, "follow-config.secrets.json");
        if (!File.Exists(secretsPath))
            return;

        try
        {
            var patch = JsonSerializer.Deserialize<RestartSecretsPatch>(File.ReadAllText(secretsPath), JsonOptions);
            if (patch is null)
                return;
            if (patch.Restart is not null)
            {
                config.Restart = patch.Restart;
                return;
            }

            if (!string.IsNullOrWhiteSpace(patch.ExecutablePath))
                config.Restart.ExecutablePath = patch.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(patch.ProcessName))
                config.Restart.ProcessName = patch.ProcessName;
            if (patch.Username is not null)
                config.Restart.Username = patch.Username;
            if (patch.Password is not null)
                config.Restart.Password = patch.Password;
            if (patch.Enabled is bool enabled)
                config.Restart.Enabled = enabled;
        }
        catch
        {
            // битый secrets не валит весь конфиг
        }
    }

    public static bool TryParseRecoveryKeys(string? text, out List<string> keys, out string? error)
    {
        keys = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        foreach (string raw in text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryRecoveryKey(raw, out System.Windows.Forms.Keys key))
            {
                error = $"Не понял клавишу «{raw}». Пример: Space, D1. Одиночная цифра 1 — это клавиша 1.";
                return false;
            }

            if (key is System.Windows.Forms.Keys.F6 or System.Windows.Forms.Keys.F7 or System.Windows.Forms.Keys.F9)
            {
                error = $"{key} занята глобальной клавишей и сюда не ставится.";
                return false;
            }

            keys.Add(key.ToString());
        }

        return true;
    }

    public static bool TryRecoveryKey(string text, out System.Windows.Forms.Keys key)
    {
        key = System.Windows.Forms.Keys.None;
        if (text.Length == 1 && text[0] is >= '0' and <= '9')
        {
            key = System.Windows.Forms.Keys.D0 + (text[0] - '0');
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && key != System.Windows.Forms.Keys.None;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

internal sealed class WindowLayoutConfig
{
    public int ClientX { get; set; }

    public int ClientY { get; set; }

    public int ClientWidth { get; set; } = 1024;

    public int ClientHeight { get; set; } = 722;
}

internal sealed class RegionConfig
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

internal sealed class ViewConfig
{
    public bool UsePrimaryScreen { get; set; } = true;

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;
}

internal sealed class CharacterConfig
{
    public int? AbsoluteX { get; set; }

    public int? AbsoluteY { get; set; }

    public int OffsetFromCenterX { get; set; }

    public int OffsetFromCenterY { get; set; } = -40;
}

internal sealed class KeyPressConfig
{
    public string Key { get; set; } = "";

    public int IntervalMilliseconds { get; set; } = 1000;
}

/// <summary>
/// Перезапуск DSO при пропаже миникарты. Учётные данные — тестовые в git;
/// боевые лучше держать в follow-config.secrets.json (см. .gitignore).
/// </summary>
internal sealed class RestartConfig
{
    public bool Enabled { get; set; }

    /// <summary>Сколько мс без миникарты/маркера игрока до перезапуска. Во время «Войти в дверь» не считается.</summary>
    public int MinimapLossMilliseconds { get; set; } = 20_000;

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Путь к лаунчеру/клиенту (например thinclient.exe).</summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>Аргументы запуска (для DSO 64-bit: -x86_64).</summary>
    public string ExecutableArguments { get; set; } = "-x86_64";

    /// <summary>Имя процесса для завершения (без .exe). Пусто — только по заголовку окна.</summary>
    public string ProcessName { get; set; } = "";

    public string Username { get; set; } = "test_user";

    public string Password { get; set; } = "test_password";

    /// <summary>Имя сервера из списка <see cref="Servers"/>.</summary>
    public string Server { get; set; } = "Grimmag";

    public List<RestartClickTarget> Servers { get; set; } =
    [
        new() { Name = "Grimmag", X = 0.82, Y = 0.30 },
        new() { Name = "Heredur", X = 0.82, Y = 0.36 },
        new() { Name = "Werian", X = 0.82, Y = 0.42 },
        new() { Name = "Harold", X = 0.82, Y = 0.48 },
    ];

    /// <summary>Куда ставить окно лаунчера перед кликами (экранные координаты клиентской области).</summary>
    public WindowLayoutConfig LauncherWindow { get; set; } = new()
    {
        ClientX = 40,
        ClientY = 40,
        ClientWidth = 900,
        ClientHeight = 650,
    };

    /// <summary>Относительные клики формы логина (доля ширины/высоты клиента лаунчера).</summary>
    public double UsernameX { get; set; } = 0.78;

    public double UsernameY { get; set; } = 0.30;

    public double PasswordX { get; set; } = 0.78;

    public double PasswordY { get; set; } = 0.38;

    /// <summary>Клик по слоту персонажа (без имени — только координаты).</summary>
    public double CharacterX { get; set; } = 0.88;

    public double CharacterY { get; set; } = 0.22;

    /// <summary>Кнопка «Начать игру» на экране персонажей.</summary>
    public double StartGameX { get; set; } = 0.88;

    public double StartGameY { get; set; } = 0.92;

    public int StepTimeoutMilliseconds { get; set; } = 90_000;

    public int PollMilliseconds { get; set; } = 500;

    /// <summary>Пауза после каждого клика/ввода при запуске игры (мс).</summary>
    public int ClickDelayMilliseconds { get; set; } = 300;

    public bool IsArmed => Enabled && !string.IsNullOrWhiteSpace(ExecutablePath);

    public void Normalize()
    {
        ExecutablePath ??= "";
        ExecutableArguments ??= "";
        ProcessName ??= "";
        Username ??= "";
        Password ??= "";
        Server ??= "";
        Servers ??= [];
        LauncherWindow ??= new WindowLayoutConfig { ClientX = 40, ClientY = 40, ClientWidth = 900, ClientHeight = 650 };
        MinimapLossMilliseconds = Math.Clamp(MinimapLossMilliseconds, 1_000, 600_000);
        MaxAttempts = Math.Clamp(MaxAttempts, 1, 20);
        StepTimeoutMilliseconds = Math.Clamp(StepTimeoutMilliseconds, 5_000, 600_000);
        PollMilliseconds = Math.Clamp(PollMilliseconds, 100, 5_000);
        ClickDelayMilliseconds = Math.Clamp(ClickDelayMilliseconds, 300, 5_000);
        UsernameX = Clamp01(UsernameX);
        UsernameY = Clamp01(UsernameY);
        PasswordX = Clamp01(PasswordX);
        PasswordY = Clamp01(PasswordY);
        CharacterX = Clamp01(CharacterX);
        CharacterY = Clamp01(CharacterY);
        StartGameX = Clamp01(StartGameX);
        StartGameY = Clamp01(StartGameY);
        foreach (RestartClickTarget item in Servers)
            item.Normalize();
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
}

internal sealed class RestartClickTarget
{
    public string Name { get; set; } = "";

    /// <summary>Доля ширины клиентской области (0…1).</summary>
    public double X { get; set; } = 0.5;

    /// <summary>Доля высоты клиентской области (0…1).</summary>
    public double Y { get; set; } = 0.5;

    public void Normalize()
    {
        Name ??= "";
        X = Math.Clamp(X, 0, 1);
        Y = Math.Clamp(Y, 0, 1);
    }
}

internal sealed class RestartSecretsPatch
{
    public bool? Enabled { get; set; }

    public string? ExecutablePath { get; set; }

    public string? ProcessName { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public RestartConfig? Restart { get; set; }
}
