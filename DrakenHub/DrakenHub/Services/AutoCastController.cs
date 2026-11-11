using DrakenHub.Core;
using DrakenHub.Models;

namespace DrakenHub.Services;

internal sealed class AutoCastController : IDisposable
{
    private readonly SettingsService _settings;
    private readonly InputService _input;
    private readonly System.Windows.Forms.Timer[] _timers = new System.Windows.Forms.Timer[5];
    private Action? _uiRefresh;

    public AutoCastController(SettingsService settings, InputService input)
    {
        _settings = settings;
        _input = input;
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            _timers[i] = new System.Windows.Forms.Timer();
            _timers[i].Tick += (_, _) => PressRow(index);
        }
    }

    public void SetUiRefresh(Action refresh) => _uiRefresh = refresh;

    public void ApplySettings()
    {
        var rows = _settings.Current.AutoCast.Rows;
        for (var i = 0; i < 5; i++)
        {
            var row = rows[i];
            _timers[i].Interval = Math.Max(50, row.IntervalMs);
            if (row.Enabled) _timers[i].Start();
            else _timers[i].Stop();
        }
        _uiRefresh?.Invoke();
    }

    public void Toggle(int index)
    {
        var row = _settings.Current.AutoCast.Rows[index];
        row.Enabled = !row.Enabled;
        ApplySettings();
        _settings.Save();
    }

    public void StopAll()
    {
        foreach (var row in _settings.Current.AutoCast.Rows)
            row.Enabled = false;
        ApplySettings();
    }

    private void PressRow(int index)
    {
        var row = _settings.Current.AutoCast.Rows[index];
        if (!row.Enabled) return;
        var key = KeyNames.Parse(row.SkillHotkey);
        if (key != Keys.None) _input.KeyPress(key);
    }

    public void Dispose()
    {
        foreach (var t in _timers) t.Dispose();
    }
}
