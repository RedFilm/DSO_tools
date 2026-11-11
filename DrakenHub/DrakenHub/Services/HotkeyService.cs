using System.Runtime.InteropServices;
using DrakenHub.Core;

namespace DrakenHub.Services;

internal sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly Dictionary<string, int> _idsByName = new();
    private IntPtr _hook = IntPtr.Zero;
    private WinApi.LowLevelProc? _proc;
    private int _nextId = 1;
    private Form? _host;
    private Action<Keys>? _onCaptured;

    public event Action<Keys, bool>? RawKey;

    public void Attach(Form host)
    {
        _host = host;
        host.HandleCreated += (_, _) => InstallHook();
        if (host.IsHandleCreated) InstallHook();
    }

    public void BeginCapture(Action<Keys> onCaptured)
    {
        _onCaptured = onCaptured;
    }

    public bool IsCapturing => _onCaptured != null;

    public void CancelCapture() => _onCaptured = null;

    public bool Register(string name, Keys key, Action handler)
    {
        Unregister(name);
        if (key == Keys.None) return true;
        if (_host == null || !_host.IsHandleCreated) return false;

        var id = _nextId++;
        if (!WinApi.RegisterHotKey(_host.Handle, id, 0, (uint)key))
            return false;

        _idsByName[name] = id;
        _handlers[id] = handler;
        return true;
    }

    public void Unregister(string name)
    {
        if (_host == null || !_idsByName.TryGetValue(name, out var id)) return;
        WinApi.UnregisterHotKey(_host.Handle, id);
        _idsByName.Remove(name);
        _handlers.Remove(id);
    }

    public void UnregisterAll()
    {
        foreach (var name in _idsByName.Keys.ToList())
            Unregister(name);
    }

    public bool ProcessMessage(ref Message m)
    {
        if (m.Msg != WinApi.WmHotkey) return false;
        var id = m.WParam.ToInt32();
        if (_handlers.TryGetValue(id, out var handler))
        {
            handler();
            return true;
        }
        return false;
    }

    public bool IsLaunchHotkeyUsed(Keys key, string? exceptName = null)
    {
        if (key == Keys.None) return false;
        foreach (var pair in _idsByName)
        {
            if (exceptName != null && pair.Key == exceptName) continue;
            // We don't store key by name here; caller checks settings.
        }
        return false;
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        using var cur = System.Diagnostics.Process.GetCurrentProcess();
        using var mod = cur.MainModule!;
        _hook = WinApi.SetWindowsHookEx(WinApi.WhKeyboardLl, _proc, WinApi.GetModuleHandle(mod.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<WinApi.KbdLlHookStruct>(lParam);
            var key = (Keys)info.VkCode;
            var down = wParam == (IntPtr)WinApi.WmKeyDown || wParam == (IntPtr)WinApi.WmSysKeyDown;
            var up = wParam == (IntPtr)WinApi.WmKeyUp || wParam == (IntPtr)WinApi.WmSysKeyUp;

            if (down || up)
                RawKey?.Invoke(key, down);

            if (down && _onCaptured != null)
            {
                var callback = _onCaptured;
                _onCaptured = null;
                if (_host != null)
                    _host.BeginInvoke(() => callback(key));
                return (IntPtr)1;
            }
        }

        return WinApi.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_hook != IntPtr.Zero)
        {
            WinApi.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
