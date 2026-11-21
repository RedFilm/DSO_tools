using System.Diagnostics;
using System.Text.Json;
using DrakenHub.Models;

namespace DrakenHub.Services;

/// <summary>
/// Hosts the bundled SpeedBoost / Cheat Engine trainer from SpeedBoostEngine/ and talks via IPC.
/// Offsets live in SpeedBoostEngine/DrakenHub.SpeedBoost.CETRAINER.
/// Engine process is started at most once per session — Enable/Disable only toggle IPC.
/// </summary>
internal sealed class SpeedBoostService : IDisposable
{
    public const float BoostBaseValue = 6f;
    public const float BoostMaxValue = 50f;
    public const float BoostStep = 0.1f;
    public const int BoostDisplayMin = 30;
    public static readonly int BoostDisplayMax =
        BoostDisplayMin + (int)Math.Floor(((BoostMaxValue - BoostBaseValue) / BoostStep) + 0.5f);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SettingsService _settings;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 400 };
    private readonly object _gate = new();
    private Process? _engine;
    private bool _enabled;
    private bool _disposed;
    private bool _launchInProgress;
    private bool _startAttempted;
    private int _cmdId;
    private string _lastDetail = "disabled";
    private string _engineState = "stopped";
    private DateTime _ignoreDisableUntil = DateTime.MinValue;
    private DateTime _ignoreEngineExitUntil = DateTime.MinValue;
    private DateTime _lastStatusTouchUtc = DateTime.MinValue;
    private DateTime _lastToggleUtc = DateTime.MinValue;
    private string? _pendingOp;

    public SpeedBoostService(SettingsService settings)
    {
        _settings = settings;
        _poll.Tick += (_, _) => PollStatus();
    }

    public bool IsEnabled => _enabled;
    public bool IsLaunching => _launchInProgress || string.Equals(_pendingOp, "enable", StringComparison.Ordinal);
    public string LastDetail => _lastDetail;

    public bool EngineRunning
    {
        get
        {
            RefreshEngineProcess();
            return _engine is { HasExited: false };
        }
    }

    public event Action<string>? StatusChanged;
    public event Action? StateChanged;

    public static IReadOnlyList<SpeedBoostProfile> Profiles { get; } =
    [
        new() { Id = "global", SpeedOff = 4f, SpeedOn = 6f, SliderEnabled = true },
        new() { Id = "dragonknight", SpeedOff = 4f, SpeedOn = -1f, CastOff = 0.5f, CastOn = -1f, SliderEnabled = false },
        new() { Id = "spellweaver", SpeedOff = 4f, SpeedOn = 6f, SliderEnabled = true },
        new() { Id = "ranger", SpeedOff = 4f, SpeedOn = 6f, SliderEnabled = true },
        new() { Id = "mechanicus", SpeedOff = 4f, SpeedOn = -1f, CastOff = 0.5f, CastOn = -1f, SliderEnabled = false },
    ];

    public static SpeedBoostProfile ResolveProfile(string? id)
        => Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles[0];

    public static float DisplayToSpeed(int display)
    {
        var value = BoostBaseValue + ((display - BoostDisplayMin) * BoostStep);
        return MathF.Round(value, 1);
    }

    public static string EngineDirectory => Path.Combine(AppContext.BaseDirectory, "SpeedBoostEngine");

    public static string IpcDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrakenHub", "SpeedBoostIpc");

    private static string CommandPath => Path.Combine(IpcDirectory, "command.json");
    private static string StatusPath => Path.Combine(IpcDirectory, "status.json");
    private static string EngineExePath => Path.Combine(EngineDirectory, "SpeedBoost.exe");

    public bool Toggle()
    {
        var now = DateTime.UtcNow;
        var deltaMs = (now - _lastToggleUtc).TotalMilliseconds;
        SpeedBoostLog.Host(
            $"Toggle enter enabled={_enabled} launching={IsLaunching} pending={_pendingOp} deltaMs={deltaMs:0}",
            includeStack: true);

        if (deltaMs < 800)
        {
            SpeedBoostLog.Host("Toggle ignored (debounce)");
            return _enabled;
        }
        _lastToggleUtc = now;

        if (IsLaunching)
        {
            SpeedBoostLog.Host("Toggle ignored (launch in progress)");
            return _enabled;
        }

        if (_enabled)
            Disable();
        else
            Enable();
        return _enabled;
    }

    public void Enable()
    {
        SpeedBoostLog.Host(
            $"Enable enter enabled={_enabled} launching={_launchInProgress} startAttempted={_startAttempted}",
            includeStack: true);

        lock (_gate)
        {
            if (_disposed || _enabled || _launchInProgress)
            {
                SpeedBoostLog.Host($"Enable skipped disposed={_disposed} enabled={_enabled} launching={_launchInProgress}");
                return;
            }
            _launchInProgress = true;
            _pendingOp = "enable";
            _ignoreDisableUntil = DateTime.UtcNow.AddSeconds(8);
            _ignoreEngineExitUntil = DateTime.UtcNow.AddSeconds(20);
        }

        try
        {
            if (!ValidateEngineFiles(out var error))
            {
                FailEnable(error);
                return;
            }

            Directory.CreateDirectory(IpcDirectory);

            if (!EnsureEngineProcess())
            {
                FailEnable("engine_start_failed");
                return;
            }

            _engineState = "starting";
            _poll.Start();
            _lastDetail = "starting";
            StatusChanged?.Invoke(_lastDetail);
            StateChanged?.Invoke();

            Task.Run(WaitForBridgeThenEnable);
            SpeedBoostLog.Host("Enable scheduled WaitForBridgeThenEnable");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            SpeedBoostLog.Host("Enable exception: " + ex);
            FailEnable("engine_start_failed");
        }
    }

    public void Disable()
    {
        SpeedBoostLog.Host($"Disable enter enabled={_enabled} pending={_pendingOp}", includeStack: true);
        lock (_gate)
        {
            _pendingOp = "disable";
            _enabled = false;
            _launchInProgress = false;
        }

        // Keep the engine process alive — only turn the hack off via IPC.
        SendCommand("disable", _settings.Current.SpeedBoost);
        _lastDetail = "disabled";
        StatusChanged?.Invoke(_lastDetail);
        StateChanged?.Invoke();
    }

    public void ReapplyIfEnabled()
    {
        if (!_enabled) return;
        SendCommand("configure", _settings.Current.SpeedBoost);
    }

    public void SwitchActiveProfile(string previousProfileId)
    {
        _ = previousProfileId;
        if (!_enabled) return;
        SendCommand("configure", _settings.Current.SpeedBoost);
    }

    /// <summary>
    /// Attach to an existing SpeedBoost, or start exactly one new process for this session.
    /// Never kills+restarts — that was the open-loop.
    /// </summary>
    private bool EnsureEngineProcess()
    {
        RefreshEngineProcess();
        if (_engine is { HasExited: false })
            return true;

        // Another instance already running (e.g. manual start) — attach, do not spawn.
        if (TryAttachAnyEngine())
            return true;

        lock (_gate)
        {
            if (_startAttempted)
            {
                // Already tried once this session; only reuse if process appeared.
                RefreshEngineProcess();
                return _engine is { HasExited: false };
            }
            _startAttempted = true;
        }

        TryDelete(StatusPath);
        TryDelete(CommandPath);
        _lastStatusTouchUtc = DateTime.MinValue;

        var table = Path.Combine(EngineDirectory, "DrakenHub.SpeedBoost.CETRAINER");
        File.Copy(table, Path.Combine(EngineDirectory, "CET_TRAINER.CETRAINER"), true);

        // GUI CE: do not use CreateNoWindow — it makes some CE builds exit immediately.
        var psi = new ProcessStartInfo
        {
            FileName = EngineExePath,
            Arguments = $"\"{table}\"",
            WorkingDirectory = EngineDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized
        };

        SpeedBoostLog.Host($"Process.Start BEGIN exe={EngineExePath} args={psi.Arguments}", includeStack: true);
        var started = Process.Start(psi);
        if (started == null)
        {
            SpeedBoostLog.Host("Process.Start returned null");
            return false;
        }

        lock (_gate)
        {
            _engine = started;
            _ignoreEngineExitUntil = DateTime.UtcNow.AddSeconds(20);
        }

        SpeedBoostLog.Host($"Process.Start OK pid={started.Id}");
        return true;
    }

    private void WaitForBridgeThenEnable()
    {
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !_disposed)
            {
                if (!string.Equals(_pendingOp, "enable", StringComparison.Ordinal))
                    return;

                RefreshEngineProcess();
                TouchStatusHeartbeat();

                if (_engineState is "ready" or "connected" || HasFreshStatus(seconds: 6))
                {
                    FinishEnable();
                    return;
                }

                Thread.Sleep(250);
            }

            if (!string.Equals(_pendingOp, "enable", StringComparison.Ordinal))
                return;

            if (EngineRunning || HasFreshStatus(seconds: 6))
            {
                FinishEnable();
                return;
            }

            FailEnable("engine_exited");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            FailEnable("engine_start_failed");
        }
    }

    private void FinishEnable()
    {
        if (_disposed) return;
        if (!string.Equals(_pendingOp, "enable", StringComparison.Ordinal))
            return;

        SpeedBoostLog.Host("FinishEnable -> send enable");
        SendCommand("enable", _settings.Current.SpeedBoost);
        lock (_gate)
        {
            _enabled = true;
            _launchInProgress = false;
            _pendingOp = null;
            _ignoreDisableUntil = DateTime.UtcNow.AddSeconds(6);
            _ignoreEngineExitUntil = DateTime.UtcNow.AddSeconds(15);
        }

        _lastDetail = "not_connected";
        StatusChanged?.Invoke(_lastDetail);
        StateChanged?.Invoke();
    }

    private void FailEnable(string error)
    {
        SpeedBoostLog.Host("FailEnable " + error);
        lock (_gate)
        {
            _enabled = false;
            _launchInProgress = false;
            _pendingOp = null;
        }

        _lastDetail = error;
        StatusChanged?.Invoke(_lastDetail);
        StateChanged?.Invoke();
    }

    private void SendCommand(string op, SpeedBoostSettings cfg)
    {
        try
        {
            Directory.CreateDirectory(IpcDirectory);
            var profile = ResolveProfile(cfg.ProfileId);
            var id = Interlocked.Increment(ref _cmdId);
            var payload = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["op"] = op,
                ["profileId"] = profile.Id,
                ["profile"] = Array.FindIndex(Profiles.ToArray(), p => p.Id == profile.Id),
                ["boost"] = Math.Clamp(cfg.BoostDisplay, BoostDisplayMin, BoostDisplayMax)
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var tmp = CommandPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, CommandPath, true);
            TryDelete(tmp);
            SpeedBoostLog.Host($"SendCommand op={op} id={id}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            SpeedBoostLog.Host("SendCommand failed: " + ex.Message);
        }
    }

    private void PollStatus()
    {
        RefreshEngineProcess();
        TouchStatusHeartbeat();

        var processAlive = _engine is { HasExited: false };
        var bridgeAlive = HasFreshStatus(seconds: 5);

        if (!processAlive && !bridgeAlive)
        {
            if (_launchInProgress || DateTime.UtcNow < _ignoreEngineExitUntil)
                return;

            if (_enabled)
            {
                _enabled = false;
                _engineState = "stopped";
                _lastDetail = "engine_exited";
                StatusChanged?.Invoke(_lastDetail);
                StateChanged?.Invoke();
            }

            if (!_launchInProgress && !_enabled)
                _poll.Stop();
            return;
        }

        try
        {
            if (!File.Exists(StatusPath)) return;
            var json = File.ReadAllText(StatusPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("engine", out var engine))
            {
                var state = engine.GetString();
                if (!string.IsNullOrWhiteSpace(state))
                    _engineState = state!;
            }

            if (root.TryGetProperty("message", out var message))
            {
                var msg = message.GetString() ?? _lastDetail;
                if (IsLaunching && msg is "disabled")
                    msg = "starting";

                if (!string.Equals(msg, _lastDetail, StringComparison.Ordinal))
                {
                    _lastDetail = msg;
                    StatusChanged?.Invoke(_lastDetail);
                }
            }

            if (root.TryGetProperty("enabled", out var enabledProp))
            {
                var engEnabled = enabledProp.GetBoolean();
                if (!engEnabled && _enabled && DateTime.UtcNow > _ignoreDisableUntil && !IsLaunching)
                {
                    var msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                    // Only trust an explicit disable/quit from the bridge — ignore boot "disabled".
                    if (msg is "disabled" or "quit")
                    {
                        // If we still have a pending enable ack window, keep local enabled.
                        // Otherwise sync down only when message is quit (engine stopping).
                        if (msg == "quit")
                        {
                            _enabled = false;
                            _lastDetail = msg;
                            StatusChanged?.Invoke(_lastDetail);
                            StateChanged?.Invoke();
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore transient IPC read races.
        }
    }

    private void TouchStatusHeartbeat()
    {
        try
        {
            if (!File.Exists(StatusPath))
                return;
            _lastStatusTouchUtc = File.GetLastWriteTimeUtc(StatusPath);
        }
        catch
        {
            // ignore
        }
    }

    private bool HasFreshStatus(int seconds)
    {
        TouchStatusHeartbeat();
        return _lastStatusTouchUtc != DateTime.MinValue &&
               (DateTime.UtcNow - _lastStatusTouchUtc).TotalSeconds <= seconds;
    }

    private bool TryAttachAnyEngine()
    {
        RefreshEngineProcess();
        return _engine is { HasExited: false };
    }

    private void RefreshEngineProcess()
    {
        if (_engine is { HasExited: false })
            return;

        try { _engine?.Dispose(); } catch { /* ignore */ }
        _engine = null;

        try
        {
            Process? fallback = null;
            foreach (var proc in Process.GetProcessesByName("SpeedBoost"))
            {
                try
                {
                    if (proc.HasExited)
                    {
                        proc.Dispose();
                        continue;
                    }

                    string? path = null;
                    try { path = proc.MainModule?.FileName; }
                    catch { /* access denied */ }

                    if (path != null &&
                        path.StartsWith(EngineDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        fallback?.Dispose();
                        _engine = proc;
                        return;
                    }

                    if (fallback == null)
                        fallback = proc;
                    else
                        proc.Dispose();
                }
                catch
                {
                    try { proc.Dispose(); } catch { /* ignore */ }
                }
            }

            _engine = fallback;
        }
        catch
        {
            // ignore
        }
    }

    private void KillEngineProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("SpeedBoost"))
            {
                try
                {
                    string? path = null;
                    try { path = proc.MainModule?.FileName; } catch { /* access denied */ }

                    var ours = path == null ||
                               path.StartsWith(EngineDirectory, StringComparison.OrdinalIgnoreCase);
                    if (!ours) continue;

                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(1500);
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    try { proc.Dispose(); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // ignore
        }

        lock (_gate)
        {
            try { _engine?.Dispose(); } catch { /* ignore */ }
            _engine = null;
        }
    }

    private static bool ValidateEngineFiles(out string error)
    {
        error = "";
        var required = new[]
        {
            "SpeedBoost.exe",
            "lua53-64.dll",
            "bridge.lua",
            "DrakenHub.SpeedBoost.CETRAINER"
        };

        if (!Directory.Exists(EngineDirectory))
        {
            error = "engine_assets_missing";
            return false;
        }

        foreach (var name in required)
        {
            if (!File.Exists(Path.Combine(EngineDirectory, name)))
            {
                error = name == "SpeedBoost.exe" ? "engine_missing" : "engine_assets_missing";
                return false;
            }
        }

        return true;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SpeedBoostLog.Host("Dispose begin", includeStack: true);
        _poll.Stop();
        _poll.Dispose();

        try
        {
            SendCommand("quit", _settings.Current.SpeedBoost);
        }
        catch { /* ignore */ }

        try
        {
            // Force-kill: quit IPC alone left an orphan SpeedBoost after app close.
            Thread.Sleep(300);
            KillEngineProcesses();
            Thread.Sleep(200);
            KillEngineProcesses();
        }
        catch (Exception ex)
        {
            SpeedBoostLog.Host("Dispose kill failed: " + ex.Message);
        }
        finally
        {
            _engine = null;
            _enabled = false;
            _launchInProgress = false;
            _startAttempted = false;
            SpeedBoostLog.Host("Dispose end");
        }
    }
}

internal sealed class SpeedBoostProfile
{
    public required string Id { get; init; }
    public float SpeedOff { get; init; }
    public float SpeedOn { get; init; }
    public float? CastOff { get; init; }
    public float? CastOn { get; init; }
    public bool SliderEnabled { get; init; } = true;
}
