using System.Runtime.InteropServices;
using System.Text;

namespace WalkerRouteBuilder;

internal static class GameWindowFocus
{
    public static bool TryPlace(string titlePart, int clientX, int clientY, int clientWidth, int clientHeight, out Rectangle client)
        => TryPlace(titlePart, clientX, clientY, clientWidth, clientHeight, prefer: null, out client);

    public static bool TryPlace(
        string titlePart,
        int clientX,
        int clientY,
        int clientWidth,
        int clientHeight,
        Func<IntPtr, Rectangle, bool>? prefer,
        out Rectangle client)
    {
        client = Rectangle.Empty;
        if (!TryPick(titlePart, prefer, out IntPtr hwnd))
            return false;

        ShowWindow(hwnd, 9);
        if (clientWidth >= 8 && clientHeight >= 8 && TryWindowChrome(hwnd, out int extraLeft, out int extraTop, out int extraRight, out int extraBottom))
        {
            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                clientX - extraLeft,
                clientY - extraTop,
                clientWidth + extraLeft + extraRight,
                clientHeight + extraTop + extraBottom,
                ShowWindowFlag);
        }

        Focus(hwnd);
        if (!TryClientScreen(hwnd, out client))
            return false;
        return client.Width >= 8 && client.Height >= 8;
    }

    public static bool TryFocus(string titlePart, out Rectangle client)
        => TryFocus(titlePart, prefer: null, out client);

    public static bool TryFocus(string titlePart, Func<IntPtr, Rectangle, bool>? prefer, out Rectangle client)
    {
        client = Rectangle.Empty;
        if (!TryPick(titlePart, prefer, out IntPtr hwnd))
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

    /// <summary>Все видимые окна, в заголовке которых есть titlePart.</summary>
    public static List<IntPtr> ListWindows(string titlePart)
    {
        var found = new List<IntPtr>();
        if (string.IsNullOrWhiteSpace(titlePart))
            return found;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            var title = new StringBuilder(512);
            _ = GetWindowText(hwnd, title, title.Capacity);
            if (!title.ToString().Contains(titlePart, StringComparison.OrdinalIgnoreCase))
                return true;

            found.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// Выбор окна. Если задан prefer — только окно, для которого предикат true (без fallback на «первое»).
    /// Иначе первое совпадение по заголовку.
    /// </summary>
    public static bool TryPick(string titlePart, Func<IntPtr, Rectangle, bool>? prefer, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        List<IntPtr> all = ListWindows(titlePart);
        if (all.Count == 0)
            return false;

        if (prefer is null)
        {
            hwnd = all[0];
            return true;
        }

        foreach (IntPtr candidate in all)
        {
            if (!TryClientScreen(candidate, out Rectangle client) || client.Width < 8 || client.Height < 8)
                continue;
            try
            {
                if (!prefer(candidate, client))
                    continue;
                hwnd = candidate;
                return true;
            }
            catch
            {
                // предикат не смог оценить окно
            }
        }

        return false;
    }

    public static bool TryGetProcessId(IntPtr hwnd, out uint processId)
    {
        processId = 0;
        if (hwnd == IntPtr.Zero)
            return false;
        _ = GetWindowThreadProcessId(hwnd, out processId);
        return processId != 0;
    }

    /// <summary>Закрыть окна/процессы по фрагменту заголовка и/или имени процесса.</summary>
    public static void KillByTitleOrProcess(string? titlePart, string? processName)
    {
        var ids = new HashSet<uint>();
        if (!string.IsNullOrWhiteSpace(titlePart))
        {
            foreach (IntPtr hwnd in ListWindows(titlePart))
            {
                if (TryGetProcessId(hwnd, out uint fromWindow))
                    ids.Add(fromWindow);
            }
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    ids.Add((uint)process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        foreach (uint id in ids)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)id);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(8_000);
                }
            }
            catch
            {
                // процесс уже ушёл
            }
        }
    }

    /// <summary>
    /// Фокус кликом по заголовку окна (вне клиентской области) — игра не получает click-to-move.
    /// </summary>
    public static bool TryFocusViaTitleBar(string titlePart, int holdMilliseconds = 40)
    {
        if (!TryFind(titlePart, out IntPtr hwnd) || !GetWindowRect(hwnd, out Rect window))
            return false;

        ShowWindow(hwnd, 9);
        int titleBottom = window.Top + 8;
        if (TryWindowChrome(hwnd, out _, out int extraTop, out _, out _) && extraTop > 4)
            titleBottom = window.Top + Math.Max(4, extraTop / 2);

        int x = (window.Left + window.Right) / 2;
        int y = Math.Clamp(titleBottom, window.Top + 2, Math.Max(window.Top + 2, window.Bottom - 2));
        MouseClicker.LeftClick(x, y, Math.Max(40, holdMilliseconds));
        Focus(hwnd);
        if (BelongsTo(hwnd, GetForegroundWindow()))
            return true;

        ForceForeground(hwnd);
        return BelongsTo(hwnd, GetForegroundWindow());
    }

    public static IntPtr TargetAt(IntPtr root, Point screen)
    {
        var point = new PointStruct { X = screen.X, Y = screen.Y };
        IntPtr under = WindowFromPoint(point);
        if (under != IntPtr.Zero && BelongsTo(root, under))
            return under;
        return root;
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
            ShowWindow(hwnd, 5);
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

    private static bool TryWindowChrome(IntPtr hwnd, out int extraLeft, out int extraTop, out int extraRight, out int extraBottom)
    {
        extraLeft = extraTop = extraRight = extraBottom = 0;
        if (!GetWindowRect(hwnd, out Rect window) || !GetClientRect(hwnd, out Rect client))
            return false;

        var origin = new PointStruct();
        if (!ClientToScreen(hwnd, ref origin))
            return false;

        extraLeft = origin.X - window.Left;
        extraTop = origin.Y - window.Top;
        extraRight = window.Right - (origin.X + client.Right);
        extraBottom = window.Bottom - (origin.Y + client.Bottom);
        return true;
    }

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

    private const uint ShowWindowFlag = 0x0040;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

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

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(PointStruct point);

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
