using DrakenHub.Core;
using DrakenHub.Models;

namespace DrakenHub.Services;

internal sealed class AutoClickerController : IDisposable
{
    private readonly SettingsService _settings;
    private readonly InputService _input;
    private readonly System.Windows.Forms.Timer _leftTimer = new();
    private readonly System.Windows.Forms.Timer _rightTimer = new();
    private bool _leftToggleOn;
    private bool _rightToggleOn;
    private bool _leftHeld;
    private bool _rightHeld;

    public AutoClickerController(SettingsService settings, InputService input)
    {
        _settings = settings;
        _input = input;
        _leftTimer.Tick += (_, _) =>
        {
            if (ShouldClickLeft()) _input.LeftClick(Cursor.Position.X, Cursor.Position.Y, 0);
        };
        _rightTimer.Tick += (_, _) =>
        {
            if (ShouldClickRight()) _input.RightClick(Cursor.Position.X, Cursor.Position.Y, 0);
        };
    }

    public void AttachRawKeys(HotkeyService hotkeys)
    {
        hotkeys.RawKey += OnRawKey;
    }

    public void ApplySettings()
    {
        var s = _settings.Current.AutoClicker;
        _leftTimer.Interval = Math.Max(1, s.LeftDelayMs);
        _rightTimer.Interval = Math.Max(1, s.RightDelayMs);
        UpdateTimers();
    }

    public void StopAll()
    {
        _leftToggleOn = false;
        _rightToggleOn = false;
        _leftHeld = false;
        _rightHeld = false;
        _leftTimer.Stop();
        _rightTimer.Stop();
    }

    private void OnRawKey(Keys key, bool down)
    {
        var s = _settings.Current.AutoClicker;
        var left = KeyNames.Parse(s.LeftHotkey);
        var right = KeyNames.Parse(s.RightHotkey);

        if (key == left && left != Keys.None)
        {
            if (s.LeftMode == "toggle")
            {
                if (down)
                {
                    _leftToggleOn = !_leftToggleOn;
                    UpdateTimers();
                }
            }
            else
            {
                _leftHeld = down;
                UpdateTimers();
            }
        }

        if (key == right && right != Keys.None)
        {
            if (s.RightMode == "toggle")
            {
                if (down)
                {
                    _rightToggleOn = !_rightToggleOn;
                    UpdateTimers();
                }
            }
            else
            {
                _rightHeld = down;
                UpdateTimers();
            }
        }
    }

    private bool ShouldClickLeft()
    {
        var s = _settings.Current.AutoClicker;
        return s.LeftMode == "toggle" ? _leftToggleOn : _leftHeld;
    }

    private bool ShouldClickRight()
    {
        var s = _settings.Current.AutoClicker;
        return s.RightMode == "toggle" ? _rightToggleOn : _rightHeld;
    }

    private void UpdateTimers()
    {
        if (ShouldClickLeft()) { if (!_leftTimer.Enabled) _leftTimer.Start(); }
        else _leftTimer.Stop();

        if (ShouldClickRight()) { if (!_rightTimer.Enabled) _rightTimer.Start(); }
        else _rightTimer.Stop();
    }

    public void Dispose()
    {
        _leftTimer.Dispose();
        _rightTimer.Dispose();
    }
}
