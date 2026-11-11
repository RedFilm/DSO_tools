using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WalkerRouteBuilder;

internal static class MouseClicker
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    private static bool _held;

    public static void LeftClick(int screenX, int screenY, int holdMilliseconds)
    {
        Release();
        SetCursorPos(screenX, screenY);
        Thread.Sleep(20);
        Send(LeftDown, 0, 0);
        Thread.Sleep(Math.Max(40, holdMilliseconds));
        Send(LeftUp, 0, 0);
        Thread.Sleep(30);
    }

    public static bool IsHeld => _held;

    /// <summary>Зажать ЛКМ и вести курсор. Повторный вызов только двигает курсор, кнопку не отжимает.</summary>
    public static void Hold(int screenX, int screenY)
    {
        SetCursorPos(screenX, screenY);
        if (_held)
            return;

        Thread.Sleep(20);
        Send(LeftDown, 0, 0);
        _held = true;
    }

    public static void Move(int screenX, int screenY) => SetCursorPos(screenX, screenY);

    public static void Release()
    {
        if (!_held)
            return;

        Send(LeftUp, 0, 0);
        _held = false;
        Thread.Sleep(30);
    }

    public static Point Clamp(Point click, Rectangle view, int margin)
    {
        int left = view.Left + margin;
        int top = view.Top + margin;
        int right = view.Right - margin - 1;
        int bottom = view.Bottom - margin - 1;
        if (right < left)
            right = left;
        if (bottom < top)
            bottom = top;
        return new Point(Math.Clamp(click.X, left, right), Math.Clamp(click.Y, top, bottom));
    }

    private static void Send(uint flags, int x, int y)
    {
        var input = new Input
        {
            Type = 0,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    X = x,
                    Y = y,
                    Flags = flags,
                },
            },
        };

        if (SendInput(1, [input], Marshal.SizeOf<Input>()) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось отправить клик мыши.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint Extra;
    }
}
