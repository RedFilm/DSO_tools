using System.Runtime.InteropServices;
using System.Text;

namespace DrakenHub.Core;

/// <summary>
/// Duplicated from WalkerEngine GameWindowFocus (no shared library yet).
/// Fullscreen-oriented: find / focus / client rect only — no SetWindowPos / window chrome placement.
/// </summary>
internal static class GameWindowFocus
{
    public static bool TryFocus(string titlePart, out Rectangle client)
    {
        client = Rectangle.Empty;
        if (!TryFind(titlePart, out IntPtr hwnd))
            return false;

        Focus(hwnd);
        return TryClientScreen(hwnd, out client) && client.Width >= 8 && client.Height >= 8;
    }

    public static bool TryActivate(string titlePart, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (!TryFind(titlePart, out hwnd))
            return false;

        Focus(hwnd);
        if (BelongsTo(hwnd, GetForegroundWindow()))
            return true;

        ForceForeground(hwnd);
        return BelongsTo(hwnd, GetForegroundWindow());
    }

    public static bool TryFindWindow(string titlePart, out IntPtr hwnd)
        => TryFind(titlePart, out hwnd);

    public static bool TryGetClient(string titlePart, out Rectangle client)
    {
        client = Rectangle.Empty;
        return TryFind(titlePart, out IntPtr hwnd)
               && TryClientScreen(hwnd, out client)
               && client.Width >= 8
               && client.Height >= 8;
    }

    private static bool TryFind(string titlePart, out IntPtr found)
    {
        found = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(titlePart))
            return false;

        IntPtr match = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var title = new StringBuilder(512);
            _ = GetWindowText(hwnd, title, title.Capacity);
            if (!title.ToString().Contains(titlePart, StringComparison.OrdinalIgnoreCase))
                return true;

            match = hwnd;
            return false;
        }, IntPtr.Zero);

        found = match;
        return found != IntPtr.Zero;
    }

    private static void Focus(IntPtr hwnd)
    {
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint targetThread = GetWindowThreadProcessId(hwnd, out _);
        bool attachForeground = foregroundThread != 0 && foregroundThread != currentThread &&
                                AttachThreadInput(currentThread, foregroundThread, true);
        bool attachTarget = targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread &&
                            AttachThreadInput(currentThread, targetThread, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        if (attachTarget)
            AttachThreadInput(currentThread, targetThread, false);
        if (attachForeground)
            AttachThreadInput(currentThread, foregroundThread, false);
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        uint previous = 0;
        bool read = SystemParametersInfoGet(GetForegroundLockTimeout, 0, out previous, 0);
        SystemParametersInfoSet(SetForegroundLockTimeout, 0, 0, 0);
        try
        {
            uint currentThread = GetCurrentThreadId();
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
            bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                            AttachThreadInput(foregroundThread, currentThread, true);
            ShowWindow(hwnd, 5); // SW_SHOW
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            if (attached)
                AttachThreadInput(foregroundThread, currentThread, false);
        }
        finally
        {
            if (read)
                SystemParametersInfoSet(SetForegroundLockTimeout, 0, (nuint)previous, 0);
        }
    }

    private static bool BelongsTo(IntPtr root, IntPtr window)
        => window != IntPtr.Zero && (window == root || GetAncestor(window, RootAncestor) == root);

    private static bool TryClientScreen(IntPtr hwnd, out Rectangle client)
    {
        client = Rectangle.Empty;
        if (!GetClientRect(hwnd, out Rect rect))
            return false;

        var origin = new PointStruct();
        if (!ClientToScreen(hwnd, ref origin))
            return false;

        client = new Rectangle(origin.X, origin.Y, Math.Max(0, rect.Right), Math.Max(0, rect.Bottom));
        return true;
    }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref PointStruct point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    private const uint GetForegroundLockTimeout = 0x2000;
    private const uint SetForegroundLockTimeout = 0x2001;
    private const uint RootAncestor = 2;

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfoGet(uint action, uint param, out uint value, uint ini);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool SystemParametersInfoSet(uint action, uint param, nuint value, uint ini);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint threadId, uint threadTo, bool attach);
}
