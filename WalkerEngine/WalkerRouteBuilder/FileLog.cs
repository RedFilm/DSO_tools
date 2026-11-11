using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using WalkerEngine;

namespace WalkerRouteBuilder;

internal static class FileLog
{
    private static readonly object Gate = new();
    private static readonly BlockingCollection<Shot> Shots = new(24);
    private static string? _lastNote;
    private static int _frame;
    private static string? _shotFolder;
    private static int _writerStarted;
    private static bool _text = true;
    private static bool _shots = true;

    static FileLog()
    {
        try
        {
            string path = OptionsPath();
            if (!File.Exists(path))
                return;
            var options = JsonSerializer.Deserialize<LogOptions>(File.ReadAllText(path));
            if (options is null)
                return;
            _text = options.Text;
            _shots = options.Shots;
        }
        catch
        {
            // Настройки по умолчанию: пишутся и текст, и снимки.
        }
    }

    public static bool TextEnabled => _text;

    public static bool ShotsEnabled => _shots;

    public static string Caption => (_text, _shots) switch
    {
        (true, true) => "Лог: текст и снимки",
        (true, false) => "Лог: текст",
        (false, true) => "Лог: снимки",
        _ => "Лог выкл",
    };

    public static void Configure(bool text, bool shots)
    {
        lock (Gate)
        {
            _text = text;
            _shots = shots;
        }

        try
        {
            File.WriteAllText(OptionsPath(), JsonSerializer.Serialize(new LogOptions { Text = text, Shots = shots }));
        }
        catch
        {
            // Переключатель в этом сеансе всё равно уже применён.
        }
    }

    public static void Write(string? message)
    {
        Append("note", message, skipDuplicate: true);
    }

    public static void Trace(string tag, string? message)
    {
        Append(tag, message, skipDuplicate: false);
    }

    public static int Frame(string? text, string? data)
    {
        if (!TextEnabled && !ShotsEnabled)
            return 0;

        int number;
        string? folder;
        lock (Gate)
        {
            number = ++_frame;
            if (_shots && _shotFolder is null)
                _shotFolder = NewShotFolder();
            folder = _shots ? _shotFolder : null;
        }

        if (!TextEnabled)
            return number;

        string sentence = string.IsNullOrWhiteSpace(text) ? "-" : text.ReplaceLineEndings(" ").Trim();
        string shot = folder is null ? "-" : $"{Path.GetFileName(folder)}/{number:000000}.jpg";
        Append("frame", $"#{number} shot={shot} «{sentence}» | {data}", skipDuplicate: false);
        return number;
    }

    public static void KeepShot(int number, Bitmap bitmap)
    {
        if (!ShotsEnabled || number <= 0)
        {
            bitmap.Dispose();
            return;
        }

        string folder;
        lock (Gate)
        {
            _shotFolder ??= NewShotFolder();
            folder = _shotFolder;
        }

        EnsureWriter();
        string path = Path.Combine(folder, number.ToString("000000") + ".jpg");
        if (!Shots.TryAdd(new Shot(path, bitmap)))
        {
            bitmap.Dispose();
            Trace("shot", $"drop #{number} очередь записи заполнена");
        }
    }

    public static void RunStarted(FollowConfig config, Scenario scenario, bool wholeScenario, string? name)
    {
        if (!TextEnabled && !ShotsEnabled)
            return;

        string? folder = null;
        lock (Gate)
        {
            _frame = 0;
            if (_shots)
            {
                folder = NewShotFolder();
                _shotFolder = folder;
            }
            else
            {
                _shotFolder = null;
            }
        }

        if (folder is not null)
            Trace("shot", $"dir={folder}");
        Trace("run",
            $"mode={(wholeScenario ? "scenario" : "blocks")} name={Q(name)} loop={scenario.Loop} pressKeys={scenario.PressKeys} map={Q(scenario.MapFile)} blocks={scenario.Blocks.Count}");
        Trace("config",
            $"title={Q(config.GameWindowTitleContains)} window={config.Window.ClientX},{config.Window.ClientY} {config.Window.ClientWidth}x{config.Window.ClientHeight} " +
            $"map={config.MapInClient.X},{config.MapInClient.Y} {config.MapInClient.Width}x{config.MapInClient.Height} " +
            $"interval={config.CaptureIntervalMilliseconds} clickDist={Num(config.ClickDistancePixels)} arrival={Num(config.ArrivalDistancePixels)} " +
            $"correction={Num(config.CorrectionDistancePixels)} clearance={config.WallClearancePixels} rot={Num(config.RotationDegrees)} " +
            $"invert={config.InvertX},{config.InvertY} char={config.Character.OffsetFromCenterX},{config.Character.OffsetFromCenterY} " +
            $"hold={config.ClickHoldMilliseconds} margin={config.ClickMarginPixels} " +
            $"stuckMs={config.StuckRecoveryMilliseconds} stuckShift={config.StuckRecoveryShiftPixels} stuckKeys={(config.StuckRecoveryKeys.Count == 0 ? "-" : string.Join(",", config.StuckRecoveryKeys))}");
        int index = 0;
        foreach (ScenarioBlock block in Scenario.Walk(scenario.Blocks))
            Trace("plan", $"{index++} {Describe(block)}");
    }

    private static string Describe(ScenarioBlock block)
        => block switch
        {
            FollowBlock follow => $"{follow.Title} travel={follow.Travel} anchors={follow.Anchors.Count} [{string.Join(",", follow.Anchors.Select(anchor => anchor.Source))}] delay={follow.DelayMilliseconds} chaseLost={follow.ChaseLostMilliseconds} pauseAfter={follow.PauseAfterMilliseconds}",
            LockBlock locked => $"{locked.Title} target={locked.Target} radius={locked.RadiusPixels} wait={locked.WaitMilliseconds} keys={locked.Keys.Count} [{string.Join(",", locked.Keys.Select(key => key.Key))}] pauseAfter={locked.PauseAfterMilliseconds}",
            DoorBlock door => $"{door.Title} difficulty={door.Difficulty}:{door.DifficultyName} pauseAfter={door.PauseAfterMilliseconds}",
            OpenChestBlock chest => $"{chest.Title} wait={chest.WaitMilliseconds} pauseAfter={chest.PauseAfterMilliseconds}",
            ClickQuestMarkBlock quest => $"{quest.Title} mark={quest.Mark} pauseAfter={quest.PauseAfterMilliseconds}",
            AcceptDialogBlock dialog => $"{dialog.Title} pauseAfter={dialog.PauseAfterMilliseconds}",
            MountHorsesBlock mount => $"{mount.Title} keyInterval={mount.KeyIntervalMilliseconds} pauseAfter={mount.PauseAfterMilliseconds}",
            UseMapBlock useMap => $"{useMap.Title} map={Q(useMap.MapFile)} pauseAfter={useMap.PauseAfterMilliseconds}",
            SequenceBlock sequence => $"{sequence.Title} clicks={sequence.Clicks.Count} [{string.Join(" | ", sequence.Clicks.Select(click => $"{Q(click.Name)} {click.X},{click.Y} after={click.PauseMilliseconds} hold={click.HoldMilliseconds}"))}] pauseAfter={sequence.PauseAfterMilliseconds}",
            RepeatBlock repeat => $"{repeat.Title} condition={repeat.Condition} wait={repeat.WaitMilliseconds} children={repeat.Children.Count} pauseAfter={repeat.PauseAfterMilliseconds}",
            _ => block.Title,
        };

    private static void Append(string tag, string? message, bool skipDuplicate)
    {
        if (!TextEnabled || string.IsNullOrWhiteSpace(message))
            return;

        string line = message.ReplaceLineEndings(" ").Trim();
        lock (Gate)
        {
            if (skipDuplicate && line == _lastNote)
                return;
            if (skipDuplicate)
                _lastNote = line;
            try
            {
                string folder = LogFolder();
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd") + ".txt");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {tag,-8} {line}{Environment.NewLine}", Encoding.UTF8);
            }
            catch
            {
                // Сбой записи не должен останавливать прогон.
            }
        }
    }

    private static string LogFolder()
        => Path.Combine(Path.GetDirectoryName(MapLibrary.Folder) ?? AppContext.BaseDirectory, "logs");

    private static string OptionsPath()
        => Path.Combine(AppContext.BaseDirectory, "log-options.json");

    private static string NewShotFolder()
        => Path.Combine(LogFolder(), DateTime.Now.ToString("yyyy-MM-dd"), DateTime.Now.ToString("HHmmss"));

    private sealed class LogOptions
    {
        public bool Text { get; set; } = true;

        public bool Shots { get; set; } = true;
    }

    private static void EnsureWriter()
    {
        if (Interlocked.Exchange(ref _writerStarted, 1) != 0)
            return;

        var thread = new Thread(WriteShots)
        {
            IsBackground = true,
            Name = "FrameShots",
        };
        thread.Start();
    }

    private static void WriteShots()
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(item => item.MimeType == "image/jpeg");
        foreach (Shot shot in Shots.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(shot.Path) ?? ".");
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
                shot.Image.Save(shot.Path, codec, parameters);
                parameters.Param[0].Dispose();
            }
            catch
            {
                // Сбой снимка не останавливает прогон.
            }
            finally
            {
                shot.Image.Dispose();
            }
        }
    }

    private sealed record Shot(string Path, Bitmap Image);

    private static string Q(string? text)
        => string.IsNullOrWhiteSpace(text) ? "-" : text.ReplaceLineEndings(" ").Trim();

    private static string Num(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
