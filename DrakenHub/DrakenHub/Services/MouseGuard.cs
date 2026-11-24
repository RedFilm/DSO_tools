using System.Runtime.InteropServices;
using DrakenHub.Core;

namespace DrakenHub.Services;

/// <summary>
/// While a macro runs, physical mouse moves and clicks are dropped and the cursor
/// is pinned to the point the macro just placed. Injected clicks (our SendInput) pass.
/// The emergency-stop key still works: only the mouse is filtered.
/// </summary>
internal sealed class MouseGuard : IDisposable
{
    private readonly object _gate = new();
    private readonly InputService _input;
    private IntPtr _hook = IntPtr.Zero;
    private WinApi.LowLevelProc? _proc;
    private int _depth;
    private int _active;

    public MouseGuard(InputService input) => _input = input;

    public bool IsActive => Volatile.Read(ref _active) != 0;

    public void Attach(Form host)
    {
        host.HandleCreated += (_, _) => Install();
        if (host.IsHandleCreated) Install();
    }

    public void Enter()
    {
        lock (_gate)
        {
            _depth++;
            Volatile.Write(ref _active, 1);
            if (_depth == 1)
                ReleaseHeldButtons();
        }
    }

    public void Leave()
    {
        lock (_gate)
        {
            _depth = Math.Max(0, _depth - 1);
            if (_depth > 0) return;
            Volatile.Write(ref _active, 0);
            WinApi.ClipCursorRelease(IntPtr.Zero);
        }
    }

    /// <summary>Keep the physical mouse from dragging the cursor off this pixel.</summary>
    public void Pin(int x, int y)
    {
        if (!IsActive) return;
        var rect = new WinApi.Rect { Left = x, Top = y, Right = x + 1, Bottom = y + 1 };
        WinApi.ClipCursorRect(ref rect);
    }

    /// <summary>Lift the clip while the hook is still dropping physical moves.</summary>
    public void Unpin() => WinApi.ClipCursorRelease(IntPtr.Zero);

    public void Dispose()
    {
        Volatile.Write(ref _active, 0);
        WinApi.ClipCursorRelease(IntPtr.Zero);
        if (_hook == IntPtr.Zero) return;
        WinApi.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        using var cur = System.Diagnostics.Process.GetCurrentProcess();
        using var mod = cur.MainModule!;
        _hook = WinApi.SetWindowsHookEx(WinApi.WhMouseLl, _proc, WinApi.GetModuleHandle(mod.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsActive)
        {
            var info = Marshal.PtrToStructure<WinApi.MsllHookStruct>(lParam);
            var injected = (info.Flags & (WinApi.LlmhfInjected | WinApi.LlmhfLowerIlInjected)) != 0;
            if (!injected && IsPhysicalMouse(wParam.ToInt32()))
                return (IntPtr)1;
        }

        return WinApi.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsPhysicalMouse(int msg) => msg is
        WinApi.WmMouseMove or
        WinApi.WmLButtonDown or WinApi.WmLButtonUp or
        WinApi.WmRButtonDown or WinApi.WmRButtonUp or
        WinApi.WmMButtonDown or WinApi.WmMButtonUp or
        WinApi.WmMouseWheel or
        WinApi.WmXButtonDown or WinApi.WmXButtonUp;

    /// <summary>
    /// A button already held when the macro starts would stay down, because its later
    /// release is swallowed. Release it here, at the cursor's current spot.
    /// </summary>
    private void ReleaseHeldButtons()
    {
        if (IsDown(0x01)) _input.ReleaseButton(WinApi.MouseeventfLeftUp);
        if (IsDown(0x02)) _input.ReleaseButton(WinApi.MouseeventfRightUp);
        if (IsDown(0x04)) _input.ReleaseButton(WinApi.MouseeventfMiddleUp);
    }

    private static bool IsDown(int vk) => (WinApi.GetAsyncKeyState(vk) & 0x8000) != 0;
}
